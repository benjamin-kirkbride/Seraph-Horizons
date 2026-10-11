using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// Checks the deposits traders would sell maps to before anyone asks (#693). The seed gives each ore
/// cell's spots but not which one takes the deposit or what ore it is (that follows the host rock),
/// so an offer lists only a checked deposit (<see cref="DepositCandidate.Surveyed"/>), and the check
/// runs ahead of the player:
/// <list type="bullet">
/// <item><b>Approach</b>: every <see cref="ScanSeconds"/> the camps of the trader grid within
/// <see cref="ApproachMetres"/> of an online player (the placed camp, or the spot its cell waits
/// for) have what they would offer queued: a prospector's ore maps (up to four metals within 5 km)
/// and every camp's gravel map. A camp is queued again after <see cref="RecheckSeconds"/>.</item>
/// <item><b>Throttled</b> (<see cref="SurveyQueue"/>): one background check at a time, at least
/// <see cref="SurveyQueue.PauseSeconds"/> after the last that did work. A check is
/// <see cref="DepositService.Verify"/>: an ore deposit's nine columns loaded (generated if need be)
/// and counted, a gravel field's one column.</item>
/// <item><b>Fallback</b>: a trade window opening on a deposit still "being surveyed" asks for its
/// check urgently (<see cref="MapsSystem"/>'s TradeOpened), which runs at once.</item>
/// <item><b>Landing</b>: loaded traders showing the deposit "being surveyed" get the real offer in
/// its place (<see cref="MapsSystem.RefreshShelves"/>), and a camp the check was queued for has its
/// would-be offers queued again (a cell that turned out to have none, or a worked-out deposit,
/// brings the metal's next deposit).</item>
/// </list>
/// Server side, main thread.
/// </summary>
public sealed class DepositSurveys
{
    /// <summary>How often online players are looked at for camps they near.</summary>
    public const double ScanSeconds = 5;

    /// <summary>A camp queued is not queued again for this long.</summary>
    public const double RecheckSeconds = 300;

    private readonly ICoreServerAPI _api;
    private readonly MapsSystem _maps;
    private readonly long _listener;
    private readonly Dictionary<string, HashSet<(int X, int Z, bool Ore)>> _origins = new();
    private readonly Dictionary<CellKey, double> _campsQueued = new();
    private readonly HashSet<string> _gaveUp = new();
    private double _nextScan;

    /// <summary>Deposits a check could not settle (every spot tried without the cell deciding): not
    /// offered or checked again until the server restarts.</summary>
    public IReadOnlySet<string> GaveUp => _gaveUp;

    /// <summary>How near a player comes to a camp before its deposits are checked; 0: only when a trade opens.</summary>
    public int ApproachMetres { get; }

    public SurveyQueue Queue { get; }

    /// <summary>Checks run and landed since the server started, for the admin and the tests.</summary>
    public int Checked { get; private set; }

    public DepositSurveys(ICoreServerAPI api, MapsSystem maps, int approachMetres, double pauseSeconds)
    {
        _api = api;
        _maps = maps;
        ApproachMetres = Math.Max(0, approachMetres);
        Queue = new SurveyQueue(pauseSeconds);
        _listener = api.Event.RegisterGameTickListener(_ => Tick(), 1000);
    }

    public void Dispose() => _api.Event.UnregisterGameTickListener(_listener);

    private double Now => _api.World.ElapsedMilliseconds / 1000.0;

    private DepositService? Deposits => _api.ModLoader.GetModSystem<OreSystem>()?.Deposits;

    /// <summary>Asks for a deposit's check; <paramref name="origin"/>, a camp (or trader) whose
    /// would-be offers are queued again when it lands. False when it is waiting or running already.</summary>
    public bool Want(DepositKey key, bool urgent, (int X, int Z, bool Ore)? origin = null)
    {
        if (_gaveUp.Contains(key.Id)) return false;
        if (origin is { } o)
        {
            if (!_origins.TryGetValue(key.Id, out var set)) _origins[key.Id] = set = new();
            set.Add(o);
        }
        bool asked = Queue.Want(key.Id, urgent);
        // Urgent: started on the next tick, not from inside the caller (a shelf being filled, whose
        // slot must hold the "being surveyed" entry before the check can land on it).
        if (asked && urgent) _api.Event.RegisterCallback(_ => Pump(), 1);
        return asked;
    }

    /// <summary>Whether a deposit's check is waiting or running.</summary>
    public bool Pending(DepositKey key) => Queue.IsWaiting(key.Id) || Queue.IsRunning(key.Id);

    private void Tick()
    {
        double now = Now;
        if (ApproachMetres > 0 && now >= _nextScan)
        {
            _nextScan = now + ScanSeconds;
            try
            {
                Scan(now);
            }
            catch (Exception e)
            {
                _api.Logger.Warning("[seraphhorizons] Trader maps: looking for camps players near failed: {0}", e.Message);
            }
        }
        Pump();
    }

    /// <summary>Queues the would-be offers of every camp within <see cref="ApproachMetres"/> of an
    /// online player, at most once per <see cref="RecheckSeconds"/>.</summary>
    private void Scan(double now)
    {
        if (TradingSystem.Of(_api) is not { GridReady: true } trading || Deposits is null) return;
        int reach = ApproachMetres / TraderGrid.CellSize + 1;
        foreach (var player in _api.World.AllOnlinePlayers)
        {
            if (player.Entity?.Pos is not { } pos) continue;
            var own = TraderGrid.CellOf((int)pos.X, (int)pos.Z);
            foreach (var cell in CampLeads.CellsAround(own, reach))
            {
                if (_campsQueued.TryGetValue(cell, out double at) && now - at < RecheckSeconds) continue;
                if (_maps.SiteOf(cell) is not { } site || CampLeads.Distance(site.X, site.Z, pos.X, pos.Z) > ApproachMetres) continue;
                _campsQueued[cell] = now;
                bool prospector = trading.Grid!.IsProspector(cell);
                int asked = _maps.QueueChecks(site.X, site.Z, prospector, urgent: false);
                if (asked > 0)
                    SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("surveys", $"{player.PlayerName} nears the {site.Type} camp {cell}: {asked} deposit check(s) queued");
            }
        }
    }

    private void Pump()
    {
        while (Queue.Next(Now) is { } id) Start(id);
    }

    private void Start(string id)
    {
        if (!DepositKey.TryParse(id, out var key) || Deposits is not { } deposits)
        {
            Queue.Done(id, Now, worked: false);
            return;
        }
        var candidate = deposits.Candidate(key);
        if (candidate is null || candidate.Surveyed || candidate.Record.State != DepositState.Unsold)
        {
            // Settled meanwhile (by a sale, an admin, or another check): only the shelves to update.
            Landed(key, worked: false);
            return;
        }
        deposits.Verify(key, _ => Landed(key, worked: true));
    }

    private void Landed(DepositKey key, bool worked)
    {
        Queue.Done(key.Id, Now, worked);
        if (worked)
        {
            Checked++;
            // A cell whose spots the check could not settle stays unchecked: not tried again (nor
            // offered) until the server restarts, so it can't loop.
            if (Deposits?.Candidate(key) is { Surveyed: false, Record.State: DepositState.Unsold }) _gaveUp.Add(key.Id);
        }
        try
        {
            _maps.RefreshShelves(key);
            if (_origins.Remove(key.Id, out var origins))
                foreach (var (x, z, ore) in origins)
                    _maps.QueueChecks(x, z, ore, urgent: false);
        }
        catch (Exception e)
        {
            _api.Logger.Warning("[seraphhorizons] Trader maps: after checking {0}: {1}", key, e);
        }
    }
}
