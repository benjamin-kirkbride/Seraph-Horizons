using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The game's treasure hunter in a world with the trader grid (<see cref="StoryTraders"/>), server
/// side. Its house's spawner spawns the pack's treasure hunter from now on, but spawns again only
/// when its trader is gone, so one spawned before (a save from before the pack took it over) would
/// stay the game's for good. Such a trader is converted where it stands, as it loads or spawns:
/// the pack's treasure hunter of the same gender and outfit set takes its place, with what the quest
/// and the player remember of it.
///
/// <list type="bullet">
/// <item>Kept: its dialogue variables (the entity's <c>variables</c> tree, where the game keeps
/// <c>entity.*</c>: <c>hasmet</c>, <c>requestbronze</c>, <c>bronzereceived</c>, <c>heardlazaret</c>,
/// <c>offeredelk</c>, and the per-player ones). <c>player.*</c> variables (<c>boughtelk</c>,
/// <c>heardalchemistlocations</c>, <c>hasmet</c>) are the game's per player in the savegame
/// (<c>VariablesModSystem</c>, <c>dialogueData</c>), not on the trader, and need nothing. Also its
/// name and who it was revealed to (<c>nametag</c>), personality, outfit, skin, where it was
/// spawned from and its revive position.</item>
/// <item>Not kept: its shelves and wallet, which were the game's list: the pack's trader stocks
/// from its own list as any new one does.</item>
/// <item>Its spawner is told: the old trader's id is swapped for the new one's in the spawner that
/// spawned it (<c>BlockEntitySpawner.spawnedEntities</c>, by reflection), so the house does not
/// spawn a second one when the old is gone.</item>
/// <item>A dead one (lying until it revives) is converted once it is alive again.</item>
/// </list>
/// Only in grid worlds (<see cref="TradingSystem.GridActive"/>); without the grid it stays the game's,
/// as every trader does.
/// </summary>
public sealed class StoryTraderConversion
{
    private static readonly FieldInfo? SpawnedField = AccessTools.Field(typeof(BlockEntitySpawner), "spawnedEntities");

    /// <summary>What the pack's trader takes over from the game's, of its watched attributes.</summary>
    public static readonly string[] KeptWatched = ["variables", "nametag", "personality", "outfitslots", "outfitcodes", "outfitversion", "skinConfig"];

    /// <summary>And of its (unsynced) attributes: where it came from, and where it revives.</summary>
    public static readonly string[] KeptAttributes = ["origin", "spawnX", "spawnY", "spawnZ"];

    private readonly ICoreServerAPI _api;
    private readonly TradingSystem _system;
    private readonly HashSet<long> _dead = new();

    /// <summary>Raised after a conversion: the game's trader (despawned) and the pack's in its place.</summary>
    public event Action<Entity, EntitySeraphTrader>? Converted;

    public StoryTraderConversion(ICoreServerAPI api, TradingSystem system)
    {
        _api = api;
        _system = system;
    }

    public void Register()
    {
        _api.Event.OnEntityLoaded += Consider;
        _api.Event.OnEntitySpawn += Consider;
        _api.Event.RegisterGameTickListener(_ => RetryDead(), 5000);
    }

    /// <summary>Whether an entity is a game trader the pack takes over as a story trader.</summary>
    public static bool IsConvertible(Entity entity) =>
        entity is EntityTrader and not EntitySeraphTrader && entity.Code is { } code && StoryTraders.PackPath(code.Domain, code.Path) != null;

    private void Consider(Entity entity)
    {
        if (!IsConvertible(entity) || !_system.GridActive) return;
        long id = entity.EntityId;
        // Not inside the load or spawn: the entity lists are being walked.
        _api.Event.RegisterCallback(_ => TryConvert(id), 0);
    }

    private void RetryDead()
    {
        if (_dead.Count == 0) return;
        foreach (long id in _dead.ToList())
        {
            if (!_api.World.LoadedEntities.TryGetValue(id, out var e) || e.State == EnumEntityState.Despawned) _dead.Remove(id);
            else if (e.Alive) TryConvert(id);
        }
    }

    private void TryConvert(long id)
    {
        if (!_api.World.LoadedEntities.TryGetValue(id, out var old) || old.State == EnumEntityState.Despawned || !IsConvertible(old)) return;
        if (!old.Alive)
        {
            _dead.Add(id);
            return;
        }
        _dead.Remove(id);
        Convert(old);
    }

    /// <summary>Puts the pack's treasure hunter in the place of the game's one, now; null when the
    /// entity is not one or the pack's type is missing.</summary>
    public EntitySeraphTrader? Convert(Entity old)
    {
        if (!IsConvertible(old) || StoryTraders.PackPath(old.Code.Domain, old.Code.Path) is not { } path) return null;
        var code = new AssetLocation(SeraphHorizonsSystem.HarmonyId, path);
        if (_api.World.GetEntityType(code) is not { } props || _api.World.ClassRegistry.CreateEntity(props) is not EntitySeraphTrader trader)
        {
            _api.Logger.Error("[seraphhorizons] Trading: no entity {0} to take over the game's treasure hunter at {1}", code, old.Pos.AsBlockPos);
            return null;
        }
        trader.Pos.SetFrom(old.Pos);
        if (old is EntityAgent oldAgent) trader.HerdId = oldAgent.HerdId;
        CopyKept(old, trader);
        _api.World.SpawnEntity(trader);
        // Spawning rolls a personality, and a name and outfit where it has none: what was there wins.
        CopyKept(old, trader);
        if (old.WatchedAttributes.GetString("personality") is { } personality) trader.Personality = personality;
        trader.WatchedAttributes.MarkAllDirty();
        int spawners = Repoint(old, trader);
        old.Die(EnumDespawnReason.Removed);
        _api.Logger.Notification("[seraphhorizons] Trading: the game's treasure hunter at {0} is now the pack's ({1}, standing id {2}); its dialogue variables kept; {3} spawner(s) repointed",
            old.Pos.AsBlockPos, code, trader.WatchedAttributes.GetString(Standing.StandingSystem.TraderIdAttr, "not set"), spawners);
        Converted?.Invoke(old, trader);
        return trader;
    }

    private static void CopyKept(Entity from, Entity to)
    {
        foreach (string key in KeptWatched)
        {
            if (from.WatchedAttributes[key] is not { } value) continue;
            if (key == "variables" && to.WatchedAttributes[key] is ITreeAttribute mine && value is ITreeAttribute theirs)
            {
                // The pack's own variables (the standing option's) stay; the game's quest ones win.
                foreach (var (name, attr) in theirs) mine[name] = attr.Clone();
                continue;
            }
            to.WatchedAttributes[key] = value.Clone();
        }
        foreach (string key in KeptAttributes)
            if (from.Attributes[key] is { } value) to.Attributes[key] = value.Clone();
    }

    /// <summary>Swaps the old trader's id for the new one's in the spawners that count it, in the
    /// loaded chunks of its story structure (or around it when it stands in none).</summary>
    private int Repoint(Entity old, Entity now)
    {
        if (SpawnedField is null)
        {
            _api.Logger.Warning("[seraphhorizons] Trading: BlockEntitySpawner has no spawnedEntities; the treasure hunter's house may spawn a second one");
            return 0;
        }
        var at = old.Pos.AsBlockPos;
        Cuboidi area = new(at.X - 32, at.Y - 32, at.Z - 32, at.X + 32, at.Y + 32, at.Z + 32);
        if (_system.Camps?.StoryStructureAt(at) is { } structure
            && _api.ModLoader.GetModSystem<GenStoryStructures>()?.Structures is { } structures
            && structures.Get(structure)?.Location is { } story)
            area = story.Clone();
        int n = 0;
        const int size = GlobalConstants.ChunkSize;
        for (int cx = area.X1 / size; cx <= area.X2 / size; cx++)
            for (int cy = Math.Max(0, area.Y1 / size); cy <= area.Y2 / size; cy++)
                for (int cz = area.Z1 / size; cz <= area.Z2 / size; cz++)
                {
                    if (_api.World.BlockAccessor.GetChunk(cx, cy, cz)?.BlockEntities is not { } blockEntities) continue;
                    foreach (var be in blockEntities.Values)
                        if (be is BlockEntitySpawner spawner && SpawnedField.GetValue(spawner) is HashSet<long> spawned && spawned.Remove(old.EntityId))
                        {
                            spawned.Add(now.EntityId);
                            spawner.MarkDirty();
                            n++;
                        }
                }
        return n;
    }
}
