using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>The tools that work a trunk entity where it lies.</summary>
public enum TrunkTool
{
    None,
    Knife,
    Shears,
    Axe,
    Saw,
    Spud,
}

/// <summary>
/// What a tool does to a trunk entity (<see cref="TrunkToolBehavior"/> holds it): Logging
/// Expanded's rules for a placed trunk (<c>BlockTreeTrunk.OnBlockInteractStart/Stop</c>, 0.3.6),
/// read through <see cref="LoggingBridge"/>, plus Immersive Woodworking's bark spud.
///
/// - Knife, 2 s: min(branches, 12) branches off, as sticks through Logging Expanded's
///   <c>StickYieldModifier</c> and the player's <c>stickDropRate</c>; at 0 branches the trunk
///   becomes the clean (<c>no</c>) one (<see cref="Trunks.Debranch"/>). The knife takes as much
///   damage as branches came off.
/// - Shears, 2 s, 12 branches at least: 12 branches off for the wood's free sapling, and a second
///   one on Logging Expanded's <c>BonusSaplingRoll</c>; at 0 branches the trunk becomes the clean
///   one (Logging Expanded leaves a placed trunk branchy with none counted, which it reads as clean).
/// - Axe, 0.75 s: one log off (two while it holds two or more, as Logging Expanded counts them) for
///   <c>TreeTrunkLogYield</c> of the wood's placed log, or with a hammer in the offhand
///   <c>TreeTrunkDebarkYield</c> of its debarked log. A debarked trunk gives debarked logs at the
///   plain yield, as the Rosser switch makes a placed one do (<c>DebarkedTrunks</c>).
/// - Saw, 0.75 s: one log off for <c>TreeTrunkPlankYield</c> of the wood's planks.
/// - Axe and saw refuse while branches are counted and <c>RequireBranchRemovalForProcessing</c> holds.
/// - Bark spud: the whole trunk in one hold of <see cref="TrunkWeight.SpudSeconds"/>; it becomes
///   the debarked trunk (<see cref="Trunks.Debark"/>) and every stored log rolls Immersive
///   Woodworking's bark once, with the spud's chance multiplier and no water
///   (<see cref="BarkDrops"/>). Refused while branches are counted or once debarked; nothing at all
///   without the debarked trunk (the Rosser switch off).
///
/// Everything made is thrown toward the player from the trunk's nearest point, as Logging Expanded
/// throws it from a placed trunk. The entity's stack is rewritten with <see cref="EntityTrunk.SetTrunk"/>,
/// which removes it at no logs.
/// </summary>
public static class TrunkHarvest
{
    public const float BranchSeconds = 2f;
    public const float ToolSeconds = 0.75f;
    public const int BranchBatch = 12;
    public const int SaplingBranches = 12;

    /// <summary>Logging Expanded's lead on a hold's end: a hold released this much short of its
    /// time still counts.</summary>
    public const float HoldTolerance = 0.1f;

    public const string BranchesFirstError = "loggingmod:treetrunk-branches-first";
    public const string DebarkedError = "seraphhorizons:rosser-error-already-debarked";

    public static readonly AssetLocation LeavesSound = new("game", "sounds/block/leaves");
    public static readonly AssetLocation WoodSound = new("game", "sounds/block/wood");
    public static readonly AssetLocation SawSound = new("game", "sounds/tool/saw");

    /// <summary>What a hold on a trunk would do: its tool and seconds, or the lang code of the
    /// error that refuses it.</summary>
    public sealed record Plan(TrunkTool Tool, float Seconds, string? Error)
    {
        public bool Refused => Error != null;
    }

    /// <summary>The trunk tool <paramref name="collectible"/> is: a knife, shears, an axe, a saw by
    /// its tool type, or Immersive Woodworking's bark spud by its code.</summary>
    public static TrunkTool ToolOf(CollectibleObject? collectible) =>
        collectible == null ? TrunkTool.None
        : collectible is Item && BarkDrops.IsSpud(new ItemStack(collectible)) ? TrunkTool.Spud
        : collectible.Tool switch
        {
            EnumTool.Knife => TrunkTool.Knife,
            EnumTool.Shears => TrunkTool.Shears,
            EnumTool.Axe => TrunkTool.Axe,
            EnumTool.Saw => TrunkTool.Saw,
            _ => TrunkTool.None,
        };

    /// <summary>Branches counted on the trunk, as Logging Expanded counts a placed trunk's.</summary>
    public static int Branches(ItemStack trunk) => trunk.Attributes.GetInt(Trunks.BranchCountKey);

    /// <summary>The hold <paramref name="tool"/> would make on <paramref name="trunk"/>, or null
    /// when it does nothing there (the hold is not taken and the click goes on as usual).</summary>
    public static Plan? PlanFor(TrunkTool tool, EntityTrunk trunk, LoggingBridge logging, TrunkEntityConfig config)
    {
        if (trunk.Trunk is not { } stack || trunk.World is not { } world || trunk.Logs < 1)
            return null;
        int branches = Branches(stack);
        switch (tool)
        {
            case TrunkTool.Knife:
                return branches > 0 ? new Plan(tool, BranchSeconds, null) : null;
            case TrunkTool.Shears:
                return branches >= SaplingBranches ? new Plan(tool, BranchSeconds, null) : null;
            case TrunkTool.Axe:
            case TrunkTool.Saw:
                return branches > 0 && logging.RequireBranchRemoval
                    ? new Plan(tool, ToolSeconds, BranchesFirstError)
                    : new Plan(tool, ToolSeconds, null);
            case TrunkTool.Spud:
                if (Trunks.IsDebarked(stack))
                    return new Plan(tool, 0, DebarkedError);
                if (Trunks.Debark(stack, world) == null)
                    return null;
                return branches > 0
                    ? new Plan(tool, 0, BranchesFirstError)
                    : new Plan(tool, TrunkWeight.SpudSeconds(trunk.Logs, config), null);
            default:
                return null;
        }
    }

    /// <summary>Whether a hold of <paramref name="seconds"/> completes one of <paramref name="needed"/>.</summary>
    public static bool IsDone(float seconds, float needed) => seconds >= needed - HoldTolerance;

    /// <summary>
    /// Does <paramref name="tool"/>'s work on <paramref name="trunk"/> for <paramref name="player"/>
    /// with the tool in <paramref name="toolSlot"/>, if it still may (the trunk can have changed
    /// during the hold). Server side. Returns whether anything was done.
    /// </summary>
    public static bool Apply(TrunkTool tool, EntityTrunk trunk, IPlayer player, ItemSlot toolSlot, LoggingBridge logging,
                             TrunkEntityConfig config, bool barkBound)
    {
        var world = trunk.World;
        if (world?.Side != EnumAppSide.Server || !trunk.Alive || trunk.Trunk is not { } stack
            || PlanFor(tool, trunk, logging, config) is not { Refused: false })
            return false;
        string? wood = Trunks.Wood(stack, world);
        int logs = trunk.Logs;
        switch (tool)
        {
            case TrunkTool.Knife:
            {
                int branches = Branches(stack);
                int batch = Math.Min(branches, BranchBatch);
                float rate = player.Entity.Stats.GetBlended("stickDropRate");
                int sticks = (int)Math.Round(logging.StickYield(player, batch) * rate);
                trunk.SetTrunk(WithBranches(stack, branches - batch, world));
                if (world.GetItem(new AssetLocation("game", "stick")) is { } stick)
                {
                    int max = stick.MaxStackSize > 0 ? stick.MaxStackSize : 64;
                    for (int left = sticks; left > 0; left -= max)
                        SpawnTowardPlayer(world, trunk, player, new ItemStack(stick, Math.Min(max, left)));
                }
                Damage(world, player, toolSlot, batch);
                PlaySound(world, trunk, LeavesSound);
                return true;
            }
            case TrunkTool.Shears:
            {
                if (wood == null || world.GetBlock(new AssetLocation("game", $"sapling-{wood}-free")) is not { Id: > 0 } sapling)
                    return false;
                trunk.SetTrunk(WithBranches(stack, Branches(stack) - SaplingBranches, world));
                SpawnTowardPlayer(world, trunk, player, new ItemStack(sapling));
                if (logging.BonusSapling(player))
                    SpawnTowardPlayer(world, trunk, player, new ItemStack(sapling));
                Damage(world, player, toolSlot, 1);
                PlaySound(world, trunk, LeavesSound);
                return true;
            }
            case TrunkTool.Axe:
            {
                if (wood == null)
                    return false;
                bool hammer = player.Entity.LeftHandItemSlot?.Itemstack?.Collectible?.Tool == EnumTool.Hammer;
                var code = hammer ? logging.DebarkedLogCode(wood)
                    : Trunks.IsDebarked(stack) ? logging.DebarkedLogCode(wood) ?? logging.PlacedLogCode(wood)
                    : logging.PlacedLogCode(wood);
                if (code == null || world.GetBlock(code) is not { Id: > 0 } log)
                    return false;
                var output = new ItemStack(log, hammer ? logging.TreeTrunkDebarkYield : logging.TreeTrunkLogYield);
                trunk.SetTrunk(WithLogs(stack, logs - (logs >= 2 ? 2 : 1), world));
                SpawnTowardPlayer(world, trunk, player, output);
                Damage(world, player, toolSlot, 1);
                PlaySound(world, trunk, WoodSound, 0.75f);
                return true;
            }
            case TrunkTool.Saw:
            {
                if (wood == null || logging.PlankCode(wood) is not { } code || world.GetItem(code) is not { Id: > 0 } plank)
                    return false;
                trunk.SetTrunk(WithLogs(stack, logs - 1, world));
                SpawnTowardPlayer(world, trunk, player, new ItemStack(plank, logging.TreeTrunkPlankYield));
                Damage(world, player, toolSlot, 1);
                PlaySound(world, trunk, SawSound);
                return true;
            }
            case TrunkTool.Spud:
            {
                if (Trunks.Debark(stack, world) is not { } debarked)
                    return false;
                var spud = toolSlot.Itemstack;
                trunk.SetTrunk(debarked.Trunk);
                if (barkBound)
                {
                    double chance = BarkDrops.ChanceMultiplier(world.Api, spud, player.Entity.LeftHandItemSlot?.Itemstack);
                    string? species = SawhorseWorks.Species(wood);
                    var drops = new List<ItemStack>();
                    for (int i = 0; i < debarked.Logs; i++)
                    {
                        if (BarkDrops.Roll(world, species, chance) is not { StackSize: > 0 } bark)
                            continue;
                        if (drops.FirstOrDefault(d => d.Equals(world, bark, GlobalConstants.IgnoredStackAttributes)) is { } same)
                            same.StackSize += bark.StackSize;
                        else
                            drops.Add(bark);
                    }
                    foreach (var drop in drops)
                        SpawnTowardPlayer(world, trunk, player, drop);
                }
                int perLog = barkBound ? BarkDrops.DurabilityPerLog(world.Api) : 1;
                Damage(world, player, toolSlot, perLog * debarked.Logs);
                var sounds = SplittingBlockUpgrades.DebarkSounds;
                PlaySound(world, trunk, sounds[world.Rand.Next(sounds.Length)]);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>A copy of <paramref name="trunk"/> holding <paramref name="logs"/> logs, of the
    /// size Logging Expanded gives that many (<see cref="TrunkCode.SizeFor"/>, as it re-sizes a
    /// trunk it picks up), its wood, branches state (debarked too) and side kept: an xl trunk axed
    /// down to 24 logs is an lg one.</summary>
    public static ItemStack WithLogs(ItemStack trunk, int logs, IWorldAccessor world)
    {
        var copy = trunk.Clone();
        if (copy.Attributes["slots"] is ITreeAttribute slots && Trunks.StoredLogStack(copy, world) is { } stored)
        {
            stored.StackSize = Math.Max(0, logs);
            slots.SetItemstack("0", stored);
        }
        if (logs > 0 && copy.Block is { Code: { } code } block
            && TrunkCode.Parse(code.Path) is { } parsed && parsed.Size != TrunkCode.SizeFor(logs)
            && world.GetBlock(new AssetLocation(code.Domain, parsed.WithSize(TrunkCode.SizeFor(logs)).Path)) is { Id: > 0 } resized)
            return new ItemStack(resized, copy.StackSize) { Attributes = copy.Attributes };
        return copy;
    }

    /// <summary>A copy of <paramref name="trunk"/> with <paramref name="branches"/> branches
    /// counted: at none, the clean trunk (<see cref="Trunks.Debranch"/>).</summary>
    public static ItemStack WithBranches(ItemStack trunk, int branches, IWorldAccessor world)
    {
        if (branches <= 0)
        {
            if (Trunks.Debranch(trunk, world) is { } clean)
                return clean;
            var bare = trunk.Clone();
            bare.Attributes.RemoveAttribute(Trunks.BranchCountKey);
            return bare;
        }
        var copy = trunk.Clone();
        copy.Attributes.SetInt(Trunks.BranchCountKey, branches);
        return copy;
    }

    private static void Damage(IWorldAccessor world, IPlayer player, ItemSlot slot, int amount)
    {
        if (amount > 0 && slot.Itemstack is { } tool)
            tool.Collectible.DamageItem(world, player.Entity, slot, amount);
    }

    // Heard by everyone, the player too: the client plays nothing of its own.
    private static void PlaySound(IWorldAccessor world, EntityTrunk trunk, AssetLocation sound, float volume = 1f) =>
        world.PlaySoundAt(sound, trunk.Pos.X, trunk.Pos.InternalY + 0.5, trunk.Pos.Z, null, true, 16, volume);

    /// <summary>The point of the trunk's boxes nearest <paramref name="to"/>, in world coordinates.</summary>
    public static Vec3d NearestPoint(EntityTrunk trunk, Vec3d to)
    {
        Vec3d? best = null;
        double bestDistance = double.MaxValue;
        var pos = trunk.Pos;
        foreach (var b in TrunkBoxes.Turned(trunk.TypeClass, pos.Yaw))
        {
            var p = new Vec3d(
                Math.Clamp(to.X, pos.X + b.X1, pos.X + b.X2),
                Math.Clamp(to.Y, pos.InternalY + b.Y1, pos.InternalY + b.Y2),
                Math.Clamp(to.Z, pos.Z + b.Z1, pos.Z + b.Z2));
            double d = p.SquareDistanceTo(to);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }
        return best ?? pos.XYZ;
    }

    /// <summary>Spawns <paramref name="output"/> just off the trunk's side facing the player,
    /// thrown toward them as Logging Expanded throws a placed trunk's yield.</summary>
    public static void SpawnTowardPlayer(IWorldAccessor world, EntityTrunk trunk, IPlayer player, ItemStack output)
    {
        var target = player.Entity.Pos.XYZ.Add(0, player.Entity.LocalEyePos.Y * 0.5, 0);
        var from = NearestPoint(trunk, target);
        var toward = target.SubCopy(from);
        if (toward.Length() > 1e-6)
            from.Add(toward.Clone().Normalize().Mul(0.3));
        var velocity = target.SubCopy(from);
        if (velocity.Length() > 1e-6)
            velocity.Normalize().Mul(0.15);
        world.SpawnItemEntity(output, from, velocity);
    }
}
