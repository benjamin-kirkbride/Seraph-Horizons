using System.Collections.Concurrent;
using HarmonyLib;
using SeraphHorizons.Mod.MapReveal.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace SeraphHorizons.Mod.MapReveal;

/// <summary>
/// The server half of Map Reveal: the <c>/revealmap</c> command, and the jobs it starts.
///
/// A job first saves the world (what <c>/autosavenow</c> does), so terrain generated or changed since
/// the last autosave is in the savegame, and waits for the chunk thread to finish writing it. A
/// worker thread of this class's own then reads each column from the savegame
/// (<see cref="SavegameReader"/>, its own read-only connection: nothing is loaded into the world or
/// generated), samples it for the map (<see cref="ColumnSampler"/>) and encodes the columns in
/// batches (<see cref="RevealCodec"/>). The main thread sends each player at most one batch per
/// <see cref="PumpIntervalMs"/>, and the worker stays at most <see cref="BatchesAhead"/> batches
/// ahead of it, so neither the server tick nor the client is flooded and memory stays bounded.
/// </summary>
internal sealed class MapRevealServer : IDisposable
{
    public const string CommandName = "revealmap";
    public const int ColumnsPerBatch = 64;
    public const int BatchesAhead = 4;
    public const int PumpIntervalMs = 100;
    private const int ProgressIntervalMs = 5000;
    private const int SaveTimeoutMs = 120_000;

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _channel;
    private readonly object _lock = new();
    private readonly Dictionary<string, RevealJob> _jobs = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private volatile bool _stopping;
    private BlockTraits? _traits;

    // The chunk thread's "save chunks now" flag, set by a save and cleared once its chunks are written.
    private static readonly AccessTools.FieldRef<ServerMain, ChunkServerThread>? ChunkThread =
        Try(() => AccessTools.FieldRefAccess<ServerMain, ChunkServerThread>("chunkThread"));

    public MapRevealServer(ICoreServerAPI api, IServerNetworkChannel channel)
    {
        _api = api;
        _channel = channel;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "seraphhorizons map reveal" };
        _worker.Start();
        api.Event.RegisterGameTickListener(_ => Pump(), PumpIntervalMs);
        api.Event.PlayerDisconnect += player => Cancel(player.PlayerUID);
        if (ChunkThread is null)
            api.Logger.Warning("[seraphhorizons] Map Reveal: ServerMain.chunkThread is gone; it can't tell when a save is written, so it waits a few seconds instead");
    }

    public void RegisterCommand()
    {
        var parsers = _api.ChatCommands.Parsers;
        _api.ChatCommands.Create(CommandName)
            .WithDescription("Reveal on your world map the terrain already generated within radius chunks of you "
                             + $"(1 to {RevealArea.MaxRadius}; a chunk is 32 blocks), without going there. "
                             + "Saves the world first; generates nothing. '/revealmap stop' stops it.")
            // Open to everyone here; OnReveal lets in creative players and controlserver holders.
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            // One word, not an int and a "stop" subcommand: the game parses the root command's
            // arguments before it looks for a subcommand, so "stop" would be refused as a radius.
            .WithArgs(parsers.Word("radius", ["stop"]))
            .HandleWith(OnCommand);
    }

    private TextCommandResult OnCommand(TextCommandCallingArgs args)
    {
        var player = (IServerPlayer)args.Caller.Player;
        string word = (string)args[0];
        if (word == "stop")
            return Cancel(player.PlayerUID)
                ? TextCommandResult.Success(Text(player, "mapreveal-stopped"))
                : TextCommandResult.Error(Text(player, "mapreveal-none"));
        if (!int.TryParse(word, out int radius) || radius < 1 || radius > RevealArea.MaxRadius)
            return TextCommandResult.Error(Text(player, "mapreveal-radius", RevealArea.MaxRadius));
        return OnReveal(args, player, radius);
    }

    private TextCommandResult OnReveal(TextCommandCallingArgs args, IServerPlayer player, int radius)
    {
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative && !player.HasPrivilege(Privilege.controlserver))
            return TextCommandResult.Error(Text(player, "mapreveal-denied"));
        if (SavegameReader.Unsupported is { } why)
            return TextCommandResult.Error($"Map reveal does not work on this game version: {why}");
        var pos = player.Entity.Pos;
        if (pos.Dimension != 0)
            return TextCommandResult.Error(Text(player, "mapreveal-dimension"));
        int chunkSize = GlobalConstants.ChunkSize;
        var columns = RevealArea.Columns((int)pos.X / chunkSize, (int)pos.Z / chunkSize, radius,
            _api.WorldManager.MapSizeX / chunkSize, _api.WorldManager.MapSizeZ / chunkSize);
        bool colorAccurate = _api.World.Config.GetAsBool("colorAccurateWorldmap")
                             || player.Privileges.Contains("colorAccurateWorldmap");
        var job = new RevealJob(player, args.Caller, columns, colorAccurate, Traits());
        lock (_lock)
        {
            if (_jobs.Remove(player.PlayerUID, out var old)) old.Cancelled = true;
            _jobs[player.PlayerUID] = job;
        }
        return TextCommandResult.Success(Text(player, "mapreveal-started", columns.Count, radius));
    }

    /// <summary>Stops the player's job, if any. Main thread.</summary>
    public bool Cancel(string playerUid)
    {
        lock (_lock)
        {
            if (!_jobs.Remove(playerUid, out var job)) return false;
            job.Cancelled = true;
            return true;
        }
    }

    // Main thread, every PumpIntervalMs: save for the jobs that need it, send each player at most
    // one batch, report progress and completion.
    private void Pump()
    {
        RevealJob[] jobs;
        lock (_lock) jobs = _jobs.Values.ToArray();
        if (jobs.Length == 0) return;
        long now = Environment.TickCount64;

        if (jobs.Any(j => j.State == JobState.NeedsSave))
        {
            // One save serves every job waiting for one.
            var first = jobs.First(j => j.State == JobState.NeedsSave);
            _api.ChatCommands.ExecuteUnparsed("/autosavenow", new TextCommandCallingArgs { Caller = first.Caller },
                result => _api.Logger.Notification("[seraphhorizons] Map Reveal: saving first: {0}", result.StatusMessage));
            foreach (var job in jobs.Where(j => j.State == JobState.NeedsSave))
            {
                job.SaveStartedMs = now;
                job.State = JobState.Saving;
            }
        }

        foreach (var job in jobs)
        {
            if (job.State == JobState.Saving && !SaveBeingWritten(now - job.SaveStartedMs))
            {
                job.State = JobState.Reading;
                job.LastProgressMs = now;
                _wake.Set();
            }
            if (job.Outbox.TryDequeue(out var batch))
            {
                _channel.SendPacket(batch.Packet, job.Player);
                job.Sent += batch.Count;
                _wake.Set();
            }
            switch (job.State)
            {
                case JobState.Failed:
                    job.Player.SendMessage(GlobalConstants.GeneralChatGroup, Text(job.Player, "mapreveal-failed", job.Error ?? "?"), EnumChatType.CommandError);
                    Remove(job);
                    break;
                case JobState.Done when job.Outbox.IsEmpty:
                    job.Player.SendMessage(GlobalConstants.GeneralChatGroup,
                        Text(job.Player, "mapreveal-done", job.Revealed, job.Skipped), EnumChatType.CommandSuccess);
                    Remove(job);
                    break;
                case JobState.Reading or JobState.Done when now - job.LastProgressMs >= ProgressIntervalMs:
                    job.LastProgressMs = now;
                    job.Player.SendMessage(GlobalConstants.GeneralChatGroup,
                        Text(job.Player, "mapreveal-progress", job.Read, job.Columns.Count, job.Sent), EnumChatType.Notification);
                    break;
            }
        }
    }

    private void Remove(RevealJob job)
    {
        lock (_lock)
            if (_jobs.TryGetValue(job.Player.PlayerUID, out var current) && current == job)
                _jobs.Remove(job.Player.PlayerUID);
    }

    private bool SaveBeingWritten(long waitedMs)
    {
        if (waitedMs >= SaveTimeoutMs) return false;
        if (ChunkThread is null || _api.World is not ServerMain server) return waitedMs < 3000;
        return ChunkThread(server)?.runOffThreadSaveNow ?? false;
    }

    // The worker: one batch at a time, taking the jobs in turn, skipping those whose player still
    // has BatchesAhead batches waiting.
    private void WorkerLoop()
    {
        var readers = new Dictionary<RevealJob, SavegameReader>();
        int turn = 0;
        while (!_stopping)
        {
            RevealJob[] jobs;
            lock (_lock) jobs = _jobs.Values.ToArray();
            foreach (var (gone, reader) in readers.Where(r => r.Key.Cancelled || r.Key.State is JobState.Done or JobState.Failed).ToArray())
            {
                reader.Dispose();
                readers.Remove(gone);
            }
            var ready = jobs.Where(j => j.State == JobState.Reading && !j.Cancelled && j.Outbox.Count < BatchesAhead).ToArray();
            if (ready.Length == 0)
            {
                _wake.WaitOne(250);
                continue;
            }
            var job = ready[turn++ % ready.Length];
            try
            {
                if (!readers.TryGetValue(job, out var reader))
                    readers[job] = reader = SavegameReader.Open(_api);
                ReadBatch(job, reader);
            }
            catch (Exception e)
            {
                _api.Logger.Error("[seraphhorizons] Map Reveal for {0} failed: {1}", job.Player.PlayerName, e);
                job.Error = e.Message;
                job.State = JobState.Failed;
            }
        }
        foreach (var reader in readers.Values) reader.Dispose();
    }

    private void ReadBatch(RevealJob job, SavegameReader reader)
    {
        var batch = new List<ColumnSample>(ColumnsPerBatch);
        while (batch.Count < ColumnsPerBatch && job.Next < job.Columns.Count && !job.Cancelled && !_stopping)
        {
            var column = job.Columns[job.Next++];
            ColumnSample? sample;
            try
            {
                sample = ColumnSampler.Sample(reader, column, job.Traits, reader.SectionsY, job.ColorAccurate);
            }
            catch (Exception e)
            {
                // A column the game can't read either (corrupt, or from an unknown format).
                _api.Logger.Warning("[seraphhorizons] Map Reveal: could not read chunk column {0}/{1}: {2}", column.X, column.Z, e.Message);
                sample = null;
            }
            if (sample is null) Interlocked.Increment(ref job.Skipped);
            else batch.Add(sample);
            Interlocked.Increment(ref job.Read);
        }
        if (batch.Count > 0)
        {
            job.Outbox.Enqueue((new MapRevealPacket { Columns = RevealCodec.Encode(batch) }, batch.Count));
            Interlocked.Add(ref job.Revealed, batch.Count);
        }
        if (job.Next >= job.Columns.Count) job.State = JobState.Done;
    }

    // Blocks don't change once the world runs, so this is built once.
    private BlockTraits Traits() => _traits ??= MapRevealSystem.MapTraits(_api.World.Blocks);

    internal static string Text(IServerPlayer player, string key, params object[] args) =>
        Lang.GetL(player.LanguageCode, "seraphhorizons:" + key, args);

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _worker.Join(2000);
        lock (_lock)
        {
            foreach (var job in _jobs.Values) job.Cancelled = true;
            _jobs.Clear();
        }
    }

    private static T? Try<T>(Func<T> f) where T : class
    {
        try { return f(); }
        catch (Exception) { return null; }
    }

    private enum JobState { NeedsSave, Saving, Reading, Done, Failed }

    private sealed class RevealJob(IServerPlayer player, Caller caller, List<ColumnPos> columns, bool colorAccurate, BlockTraits traits)
    {
        public readonly IServerPlayer Player = player;
        public readonly Caller Caller = caller;
        public readonly List<ColumnPos> Columns = columns;
        public readonly bool ColorAccurate = colorAccurate;
        public readonly BlockTraits Traits = traits;
        public readonly ConcurrentQueue<(MapRevealPacket Packet, int Count)> Outbox = new();

        public volatile JobState State = JobState.NeedsSave;
        public volatile bool Cancelled;
        public volatile string? Error;
        public long SaveStartedMs, LastProgressMs; // main thread
        public int Next;                          // worker
        public int Read, Revealed, Skipped, Sent;
    }
}
