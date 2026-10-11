using HarmonyLib;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// An eidolon at work keeps its chunks loaded (README "Eidolon", chunk loading): every
/// <see cref="PassMs"/> each loaded eidolon that can work and whose order has work left
/// (<see cref="IEidolonOrder.HasWorkLeft"/>) holds the columns round it and its order's work areas
/// (<see cref="EidolonChunks.Wanted"/>), forced loaded with the game's <c>KeepLoaded</c>; any other lets
/// them go, as does one no longer loaded. The holds are counted (<see cref="ChunkHolds"/>), so two
/// eidolons share a column, and a column the server forced for something else is left to it.
///
/// <para>The game lets a forced column go only by unloading it at once (<c>UnloadChunkColumn</c>, even
/// under a player), so a column is let go by taking it off the server's list of forced columns
/// (<c>ServerMain.RemoveChunkColumnFromForceLoadedList</c>), and it unloads as any other does once no
/// player is near. Loaded is not enough to work: the game makes a creature no player is within its
/// simulation range of inactive (no AI, no animations) and skips its physics while no player tracks it,
/// so an eidolon holding its chunks is <see cref="Entity.AlwaysActive"/>
/// (<see cref="EntityLaborEidolon.KeepsChunksLoaded"/>) and a postfix on the physics manager's tracking
/// check keeps it tracked.</para>
///
/// <para>The holds are saved with the world (<see cref="SaveKey"/>) and loaded again as the server
/// starts, so an eidolon at work when it stopped is loaded and carries on; a hold whose eidolon has not
/// loaded within <see cref="RestoreSeconds"/> is let go. Server side, with the <c>Eidolon</c> switch and
/// <see cref="EidolonConfig.KeepWorkChunksLoaded"/> on.</para>
/// </summary>
public class EidolonChunkSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.eidolonchunks";
    public const string SaveKey = "seraphhorizons:eidolonChunks";

    /// <summary>How often the eidolons are looked over.</summary>
    public const int PassMs = 2000;

    /// <summary>How long a hold loaded from the savegame waits for its eidolon to load.</summary>
    public const double RestoreSeconds = 120;

    private static readonly AccessTools.FieldRef<ServerMain, HashSet<long>>? ForcedColumns =
        AccessTools.Field(typeof(ServerMain), "forceLoadedChunkColumns") is { } field
            ? AccessTools.FieldRefAccess<ServerMain, HashSet<long>>(field)
            : null;

    private ICoreServerAPI? _api;
    private EidolonConfig _config = EidolonConfig.Defaults;
    private Harmony? _harmony;
    private readonly Dictionary<long, double> _restoredUntil = [];

    public static EidolonChunkSystem? Of(ICoreAPI? api) => api?.ModLoader.GetModSystem<EidolonChunkSystem>();

    /// <summary>The columns held, by eidolon.</summary>
    public ChunkHolds Holds { get; } = new();

    /// <summary>Whether eidolons keep their chunks loaded on this server.</summary>
    public bool Running => _api != null;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (EidolonSystem.Of(api) is not { Enabled: true } eidolons || !eidolons.Config.KeepWorkChunksLoaded)
            return;
        if (api.World is not ServerMain || ForcedColumns == null)
        {
            api.Logger.Warning("[seraphhorizons] Eidolon: the server's forced chunk columns are not where they were; eidolons do not keep their chunks loaded");
            return;
        }
        _api = api;
        _config = eidolons.Config;
        _harmony = new Harmony(HarmonyId);
        if (AccessTools.Method(typeof(PhysicsManager), "UpdateTrackedEntityState") is { } tracked)
            _harmony.Patch(tracked, postfix: new HarmonyMethod(typeof(EidolonChunkSystem), nameof(TrackedPostfix)));
        else
            api.Logger.Warning("[seraphhorizons] Eidolon: PhysicsManager.UpdateTrackedEntityState not found; an eidolon at work keeps its chunks loaded but does not move with no player near");
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => Restore(RestoreSeconds));
        api.Event.GameWorldSave += Save;
        api.Event.RegisterGameTickListener(_ => Pass(), PassMs);
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }

    // An eidolon holding its chunks is tracked by the physics manager as if a player were near (its
    // physics skipped otherwise), and made active again if it was inactive when it began to.
    private static void TrackedPostfix(Entity entity, ref bool __result)
    {
        if (entity is not EntityLaborEidolon { KeepsChunksLoaded: true })
            return;
        if (entity.IsTracked == 0)
            entity.IsTracked = 1;
        if (entity.State == EnumEntityState.Inactive)
        {
            entity.State = EnumEntityState.Active;
            __result = true;
        }
    }

    /// <summary>Looks the loaded eidolons over: each holds the columns it should and no others, and holds
    /// whose eidolon is gone are let go. Every <see cref="PassMs"/>; at once for a test.</summary>
    public void Pass()
    {
        if (_api == null)
            return;
        double now = _api.World.ElapsedMilliseconds / 1000.0;
        var seen = new HashSet<long>();
        foreach (var eidolon in _api.World.LoadedEntities.Values.OfType<EntityLaborEidolon>())
        {
            long id = eidolon.EntityId;
            seen.Add(id);
            _restoredUntil.Remove(id);
            var order = eidolon.Orders?.Current;
            bool keeps = eidolon.Alive && eidolon.Pos.Dimension == 0
                && EidolonChunks.Keeps(true, eidolon.CanWork, order?.HasWorkLeft == true);
            if (keeps)
                Apply(id, EidolonChunks.Wanted(eidolon.Pos.X, eidolon.Pos.Z, _config.ChunkLoadRadius, order!.WorkAreas, _config.MaxWorkAreaChunks));
            else if (Holds.Of(id).Count > 0)
                Apply(id, []);
            eidolon.KeepsChunksLoaded = keeps;
        }
        foreach (long holder in Holds.Holders.ToList())
        {
            if (seen.Contains(holder) || (_restoredUntil.TryGetValue(holder, out double until) && now < until))
                continue;
            Apply(holder, []);
            _restoredUntil.Remove(holder);
        }
    }

    /// <summary>Whether the server has <paramref name="column"/> on its list of forced columns.</summary>
    public bool IsForced(ChunkColumn column) =>
        _api?.World is ServerMain server && ForcedColumns!(server).Contains(_api.WorldManager.MapChunkIndex2D(column.X, column.Z));

    private void Apply(long holder, IEnumerable<ChunkColumn> columns)
    {
        var api = _api!;
        var (force, release) = Holds.Set(holder, columns.Where(InMap), IsForced);
        foreach (var c in force)
            api.WorldManager.LoadChunkColumnPriority(c.X, c.Z, new ChunkLoadOptions { KeepLoaded = true });
        foreach (var c in release)
            ((ServerMain)api.World).RemoveChunkColumnFromForceLoadedList(api.WorldManager.MapChunkIndex2D(c.X, c.Z));
    }

    private bool InMap(ChunkColumn c) =>
        c.X >= 0 && c.Z >= 0 && c.X < _api!.WorldManager.MapSizeX / EidolonChunks.ChunkSize
        && c.Z < _api.WorldManager.MapSizeZ / EidolonChunks.ChunkSize;

    private void Save() => _api?.WorldManager.SaveGame.StoreData(SaveKey, Holds.ToBytes());

    /// <summary>Loads the columns held when the world was last saved, each until its eidolon has loaded
    /// and says what it holds, or for <paramref name="seconds"/>.</summary>
    private void Restore(double seconds)
    {
        var api = _api!;
        List<SavedHold> saved;
        try
        {
            saved = ChunkHolds.FromBytes(api.WorldManager.SaveGame.GetData(SaveKey));
        }
        catch (Exception e)
        {
            api.Logger.Error($"[seraphhorizons] Eidolon: could not read the held chunks from the savegame, dropping them: {e.Message}");
            return;
        }
        double until = api.World.ElapsedMilliseconds / 1000.0 + seconds;
        foreach (var hold in saved)
        {
            Apply(hold.EntityId, hold.Columns);
            _restoredUntil[hold.EntityId] = until;
        }
        if (saved.Count > 0)
            api.Logger.Notification($"[seraphhorizons] Eidolon: loading the chunks of {saved.Count} eidolon(s) at work when the world was saved");
    }

    /// <summary>For a test: loads <paramref name="saved"/> as the server's start does from the savegame,
    /// each hold waiting <paramref name="seconds"/> for its eidolon.</summary>
    public void RestoreFrom(byte[] saved, double seconds = RestoreSeconds)
    {
        if (_api == null)
            return;
        _api.WorldManager.SaveGame.StoreData(SaveKey, saved);
        Restore(seconds);
    }

    /// <summary>For a test: the holds as they would be saved now.</summary>
    public byte[] SavedNow() => Holds.ToBytes();
}
