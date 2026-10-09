using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Fells a tree as the eidolon, by the path a player's axe takes (#677; README "Eidolon", felling).
/// When a player breaks a log, the server raises its <c>BreakBlock</c> event, where Logging Expanded
/// fells (<c>FellingListener.OnBreakBlock</c>: it finds the tree, marks its logs and branchy leaves to
/// drop nothing, throws the trunk and raises <c>OnTreeFelled</c>), and then calls the held axe's
/// <c>ItemAxe.OnBlockBrokenWith</c>, which breaks every block of the tree and wears the axe (and
/// <see cref="FellingWear"/> makes that a flat figure per tree).
///
/// <para>The eidolon is no player: the server raises <c>BreakBlock</c> only for players, Logging
/// Expanded's handler starts from the player's hotbar, and no stand-in player can be made (the game's
/// <c>IPlayer</c> has an internal member, so nothing outside the game implements it). So this runs the
/// handler's steps itself with Logging Expanded's own members, found by name on its listener
/// (<c>LoggingMod.Core._fellingListener</c>): its tree manager's <c>GetWoodType</c> and
/// <c>IsResinBearingLog</c>, its <c>FindTreeCompat</c>, <c>IsLeafBlock</c>, <c>IsBranchyLeaf</c> and
/// <c>SpawnTrunk</c> (with no player), <c>FellingDropSuppression.MarkRange</c> and its config's
/// <c>MinLogsForTrunk</c> and <c>StickYieldRatio</c>, counting as the handler counts. What it leaves
/// out needs a player: its <c>LogYieldModifier</c> and <c>OnTreeFelled</c> callbacks (no mod in the
/// pack sets the modifier; <see cref="FellingWear"/>, the one listener, is told directly with
/// <see cref="FellingWear.NoteFelled"/>). Then the axe's own <c>OnBlockBrokenWith</c> with the eidolon
/// as the entity, so the trunk falls as a trunk entity (<c>TrunkEntities</c>) and the axe wears as a
/// player's. Without Logging Expanded (or with its members not as expected: one warning) the axe alone
/// fells it, and the logs drop as the game's.</para>
/// </summary>
public static class EidolonFeller
{
    public const string CoreType = "LoggingMod.Core";
    public const string ListenerField = "_fellingListener";

    private static ICoreServerAPI? _boundTo;
    private static Bridge? _bridge;

    /// <summary>Logging Expanded's felling members (one listener's).</summary>
    private sealed class Bridge
    {
        public required object Listener;
        public required object Trees;
        public required MethodInfo WoodType;
        public required MethodInfo ResinLog;
        public required MethodInfo FindTreeCompat;
        public required MethodInfo IsLeaf;
        public required MethodInfo IsBranchy;
        public required MethodInfo SpawnTrunk;
        public required MethodInfo MarkRange;
        public required PropertyInfo Config;
        public required PropertyInfo MinLogs;
        public required PropertyInfo StickRatio;
    }

    /// <summary>Whether Logging Expanded's felling was found (after the first felling).</summary>
    public static bool LoggingExpanded => _bridge != null;

    private static Bridge? Bind(ICoreServerAPI api)
    {
        if (ReferenceEquals(_boundTo, api))
            return _bridge;
        _boundTo = api;
        _bridge = null;
        var core = api.ModLoader.GetModSystem(CoreType);
        if (core == null)
            return null;
        try
        {
            var listener = AccessTools.Field(core.GetType(), ListenerField)?.GetValue(core);
            var type = listener?.GetType();
            var trees = type == null ? null : AccessTools.Field(type, "_treeManager")?.GetValue(listener);
            var config = AccessTools.TypeByName("LoggingMod.LoggingConfig");
            var suppression = AccessTools.TypeByName("LoggingMod.FellingDropSuppression");
            Bridge? bridge = listener == null || trees == null || config == null || suppression == null ? null : new Bridge
            {
                Listener = listener,
                Trees = trees,
                WoodType = AccessTools.Method(trees.GetType(), "GetWoodType", [typeof(AssetLocation)]),
                ResinLog = AccessTools.Method(trees.GetType(), "IsResinBearingLog", [typeof(AssetLocation)]),
                FindTreeCompat = AccessTools.Method(type, "FindTreeCompat", [typeof(IWorldAccessor), typeof(BlockPos)]),
                IsLeaf = AccessTools.Method(type, "IsLeafBlock", [typeof(AssetLocation), typeof(string)]),
                IsBranchy = AccessTools.Method(type, "IsBranchyLeaf", [typeof(AssetLocation)]),
                SpawnTrunk = AccessTools.Method(type, "SpawnTrunk",
                    [typeof(IServerPlayer), typeof(BlockPos), typeof(string), typeof(int), typeof(int), typeof(int)]),
                MarkRange = AccessTools.Method(suppression, "MarkRange", [typeof(IEnumerable<BlockPos>)]),
                Config = AccessTools.Property(config, "Current"),
                MinLogs = AccessTools.Property(config, "MinLogsForTrunk"),
                StickRatio = AccessTools.Property(config, "StickYieldRatio"),
            };
            if (bridge != null && new object?[] { bridge.WoodType, bridge.ResinLog, bridge.FindTreeCompat, bridge.IsLeaf, bridge.IsBranchy,
                    bridge.SpawnTrunk, bridge.MarkRange, bridge.Config, bridge.MinLogs, bridge.StickRatio }.All(m => m != null))
                _bridge = bridge;
        }
        catch (Exception e)
        {
            api.Logger.VerboseDebug($"[seraphhorizons] Eidolon felling: binding Logging Expanded: {e}");
        }
        if (_bridge == null)
            api.Logger.Warning("[seraphhorizons] Logging Expanded's felling (LoggingMod.FellingListener, TreeManager, LoggingConfig, "
                               + "FellingDropSuppression) is not as expected; an eidolon's felling drops the game's logs, not a trunk");
        return _bridge;
    }

    /// <summary>
    /// Fells the tree whose stump is <paramref name="stump"/> with the axe in <paramref name="hand"/>,
    /// as <paramref name="eidolon"/>. Returns false (and does nothing) when there is no axe or no tree
    /// there. Server side.
    /// </summary>
    public static bool Fell(EntityLaborEidolon eidolon, ItemSlot hand, BlockPos stump)
    {
        if (eidolon.Api is not ICoreServerAPI api || hand.Itemstack?.Collectible is not ItemAxe axe)
            return false;
        var world = eidolon.World;
        if (axe.FindTree(world, stump, out _, out _).Count == 0)
            return false;
        var sel = new BlockSelection { Position = stump.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        if (Bind(api) is { } bridge)
        {
            try
            {
                Trunk(bridge, world, axe, stump, FellingWear.FellerId(eidolon)!);
            }
            catch (Exception e)
            {
                api.Logger.Error($"[seraphhorizons] Eidolon felling: Logging Expanded's felling failed at {stump}: {(e as TargetInvocationException)?.InnerException ?? e}");
            }
        }
        axe.OnBlockBrokenWith(world, eidolon, hand, sel);
        return true;
    }

    // FellingListener.OnBreakBlock, its steps after the player's hotbar (Logging Expanded 0.3.6).
    private static void Trunk(Bridge lx, IWorldAccessor world, ItemAxe axe, BlockPos at, string fellerId)
    {
        var blocks = world.BlockAccessor;
        if (blocks.GetBlock(at)?.Code is not { } code || lx.WoodType.Invoke(lx.Trees, [code]) is not string wood)
            return;
        var tree = axe.FindTree(world, at, out _, out _);
        if (tree.Count == 0)
            tree = (Stack<BlockPos>)lx.FindTreeCompat.Invoke(lx.Listener, [world, at])!;
        if (tree.Count == 0)
            return;
        int logs = 0, branches = 0, resin = 0;
        var stump = at;
        var marked = new List<BlockPos>(tree.Count);
        foreach (var pos in tree)
        {
            if (pos.Y < stump.Y)
                stump = pos;
            if (blocks.GetBlock(pos)?.Code is not { } c)
                continue;
            if (lx.WoodType.Invoke(lx.Trees, [c]) as string == wood)
            {
                logs++;
                if ((bool)lx.ResinLog.Invoke(lx.Trees, [c])!)
                    resin++;
                marked.Add(pos);
            }
            else if ((bool)lx.IsLeaf.Invoke(null, [c, wood])! && (bool)lx.IsBranchy.Invoke(null, [c])!)
            {
                branches++;
                marked.Add(pos);
            }
        }
        var config = lx.Config.GetValue(null);
        if (logs < (int)lx.MinLogs.GetValue(config)!)
            return;
        branches = (int)Math.Round(branches * (double)lx.StickRatio.GetValue(config)!, MidpointRounding.AwayFromZero);
        lx.MarkRange.Invoke(null, [marked]);
        lx.SpawnTrunk.Invoke(lx.Listener, [null, stump, wood, logs, branches, resin]);
        FellingWear.NoteFelled(fellerId, stump);
    }

    /// <summary>
    /// Plants a sapling of <paramref name="wood"/> at <paramref name="stump"/> from what it carries
    /// (<see cref="EidolonCarrying.CarriedInventory"/>): that tree's sapling or its seed, one, taken.
    /// False when it carries none, or the sapling cannot stand there (the ground is not soil, the
    /// place not clear). Server side.
    /// </summary>
    public static bool Replant(EntityLaborEidolon eidolon, BlockPos stump, string wood)
    {
        if (eidolon.CarriedInventory() is not { } inventory)
            return false;
        var world = eidolon.World;
        if (world.GetBlock(new AssetLocation(EidolonFelling.SaplingCode(wood))) is not { Id: > 0 } sapling)
            return false;
        foreach (var slot in inventory)
        {
            if (slot?.Itemstack is not { } stack || stack.Collectible?.Code is not { } code || !EidolonFelling.Replants(code.ToString(), wood))
                continue;
            string failure = "";
            var sel = new BlockSelection { Position = stump.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0, 0.5) };
            if (!sapling.TryPlaceBlock(world, null, new ItemStack(sapling), sel, ref failure))
            {
                world.Logger.Debug($"[seraphhorizons] Eidolon {eidolon.EntityId} could not replant {wood} at {stump}: {failure}");
                return false;
            }
            slot.TakeOut(1);
            slot.MarkDirty();
            world.PlaySoundAt(new AssetLocation("game", "sounds/block/dirt1"), stump.X + 0.5, stump.Y, stump.Z + 0.5, null);
            return true;
        }
        return false;
    }
}
