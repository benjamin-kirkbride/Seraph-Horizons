using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/TrunkEntities/Game/TrunkToolBehavior.cs: knives, shears, axes, saws and
/// Immersive Woodworking's bark spud worked on a trunk entity where it lies, with Logging Expanded's
/// placed-trunk rules (<see cref="TrunkHarvest"/>). A player holds the tool on the trunk the way a
/// client's hold reaches the server: the held collectible's <c>OnHeldInteractStart</c>, one
/// <c>OnHeldInteractStep</c> and <c>OnHeldInteractStop</c> with the seconds held, an
/// <see cref="EntitySelection"/> of the trunk. What the work throws out is caught as it spawns.
/// </summary>
[AtlasWorld]
public class TrunkToolScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const string Axe = "game:axe-felling-iron";
    private const string Hammer = "game:hammer-iron";
    private const string Knife = "game:knife-generic-iron";
    private const string Shears = "game:shears-iron";
    private const string Saw = "game:saw-iron";
    private const string Spud = "immersivewoodworking:barkspud-iron";

    private IWorldAccessor W => World.Api.World;

    private TrunkEntitySystem Mod => TrunkEntitySystem.Of(World.Api);

    private LoggingBridge Logging => Mod.Logging ?? throw new Xunit.Sdk.XunitException("no Logging Expanded bridge");

    private Block BlockOf(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    private ItemStack Stack(string code) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item) : new ItemStack(BlockOf(code));

    /// <summary>A trunk of <paramref name="wood"/> holding <paramref name="logs"/> logs, with
    /// <paramref name="branches"/> branches counted (and the <c>yes</c> state) when above 0.</summary>
    private ItemStack Trunk(int logs, int branches = 0, string size = "md", string wood = "oak")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-{(branches > 0 ? "yes" : "no")}-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        if (branches > 0)
            stack.Attributes.SetInt(Trunks.BranchCountKey, branches);
        return stack;
    }

    // One player for the class's world.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ITestPlayer> Players = new();

    /// <summary>A cleared granite floor 40 above spawn at <paramref name="dx"/>, a trunk lying on
    /// it along z, and the player three blocks off its side, in survival with empty hands.</summary>
    private async Task<(EntityTrunk Trunk, ITestPlayer Player)> Setup(int dx, ItemStack trunk)
    {
        var origin = World.Spawn.AddCopy(dx, 40, -60);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null, 30000);
        int granite = BlockOf("game:rock-granite").Id;
        for (int x = -5; x <= 5; x++)
        for (int z = -4; z <= 4; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 4; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        if (!Players.TryGetValue(World.Api, out var player))
            Players.Add(World.Api, player = await World.JoinPlayer("trunktooler"));
        await player.TeleportTo(origin.AddCopy(3, 0, 0));
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Entity.Controls.ShiftKey = false;
        player.Entity.LeftHandItemSlot.Itemstack = null;
        player.Entity.LeftHandItemSlot.MarkDirty();
        var entity = TrunkSpawns.Spawn(W, trunk, origin.ToVec3d().Add(0.5, 0, 0.5), 0) ?? throw new Xunit.Sdk.XunitException("no trunk entity");
        await World.Ticks(2);
        return (entity, player);
    }

    /// <summary>The result of one hold: whether it was taken, what the step said, and the item
    /// stacks it threw out.</summary>
    private sealed record Held(bool Taken, bool StepGoesOn, List<ItemStack> Drops, ItemSlot Slot);

    /// <summary>Holds <paramref name="tool"/> on <paramref name="trunk"/> for
    /// <paramref name="seconds"/> and lets go, with <paramref name="offhand"/> in the left hand.</summary>
    private Held Hold(ITestPlayer player, EntityTrunk trunk, string tool, float seconds, string? offhand = null)
    {
        var entity = player.Entity;
        entity.LeftHandItemSlot.Itemstack = offhand == null ? null : Stack(offhand);
        entity.LeftHandItemSlot.MarkDirty();
        var slot = player.Player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = Stack(tool);
        slot.MarkDirty();
        var selection = new EntitySelection { Entity = trunk, Position = trunk.Pos.XYZ.Add(0, 0.5, 0), HitPosition = new Vec3d(0, 0.5, 0) };
        var drops = new List<ItemStack>();
        void Catch(Entity e)
        {
            if (e is EntityItem item && item.Itemstack != null)
                drops.Add(item.Itemstack.Clone());
        }
        ((Vintagestory.API.Server.ICoreServerAPI)World.Api).Event.OnEntitySpawn += Catch;
        try
        {
            var collectible = slot.Itemstack.Collectible;
            var handling = EnumHandHandling.NotHandled;
            collectible.OnHeldInteractStart(slot, entity, null, selection, true, ref handling);
            if (handling == EnumHandHandling.NotHandled)
                return new Held(false, false, drops, slot);
            bool goesOn = collectible.OnHeldInteractStep(seconds, slot, entity, null, selection);
            collectible.OnHeldInteractStop(seconds, slot, entity, null, selection);
            return new Held(true, goesOn, drops, slot);
        }
        finally
        {
            ((Vintagestory.API.Server.ICoreServerAPI)World.Api).Event.OnEntitySpawn -= Catch;
            // what was thrown out stays out of the next scenario
            foreach (var e in W.GetEntitiesAround(trunk.Pos.XYZ, 10, 6, e => e is EntityItem))
                e.Die(EnumDespawnReason.Removed);
        }
    }

    private static int Count(List<ItemStack> drops, AssetLocation? code) =>
        drops.Where(d => d.Collectible.Code.Equals(code)).Sum(d => d.StackSize);

    [AtlasScenario]
    public void Every_tool_kind_gets_the_behaviour()
    {
        foreach (var code in new[] { Axe, Knife, Shears, Saw, Spud })
            Assert.NotNull(Stack(code).Collectible.GetCollectibleBehavior<TrunkToolBehavior>(true));
        Assert.Null(Stack(Hammer).Collectible.GetCollectibleBehavior<TrunkToolBehavior>(true));
        Assert.Equal(1, Logging.TreeTrunkLogYield);
        Assert.Equal(6, Logging.TreeTrunkPlankYield);
        Assert.NotNull(Logging.PlankCode("oak"));
    }

    [AtlasScenario]
    public async Task An_axe_takes_a_log_off_a_trunk()
    {
        var (trunk, player) = await Setup(0, Trunk(10));
        var placed = Logging.PlacedLogCode("oak");
        Assert.NotNull(placed);

        // let go early: nothing
        var early = Hold(player, trunk, Axe, 0.3f);
        Assert.True(early.Taken);
        Assert.Empty(early.Drops);
        Assert.Equal(10, trunk.Logs);

        var held = Hold(player, trunk, Axe, TrunkHarvest.ToolSeconds);
        Assert.True(held.Taken);
        Assert.False(held.StepGoesOn);
        Assert.Equal(Logging.TreeTrunkLogYield, Count(held.Drops, placed));
        // two logs off while it holds two or more, as Logging Expanded counts them
        Assert.Equal(8, trunk.Logs);
        var axe = held.Slot.Itemstack!;
        Assert.Equal(axe.Collectible.GetMaxDurability(axe) - 1, axe.Collectible.GetRemainingDurability(axe));
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task An_axe_with_a_hammer_in_the_offhand_gives_a_debarked_log()
    {
        var (trunk, player) = await Setup(14, Trunk(6));
        var debarked = Logging.DebarkedLogCode("oak");
        Assert.NotNull(debarked);
        var held = Hold(player, trunk, Axe, TrunkHarvest.ToolSeconds, Hammer);
        Assert.Equal(Logging.TreeTrunkDebarkYield, Count(held.Drops, debarked));
        Assert.Equal(4, trunk.Logs);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_knife_cuts_branches_into_sticks_and_leaves_a_clean_trunk()
    {
        var (trunk, player) = await Setup(28, Trunk(10, branches: 20));
        var stick = new AssetLocation("game:stick");
        float rate = player.Entity.Stats.GetBlended("stickDropRate");

        var first = Hold(player, trunk, Knife, TrunkHarvest.BranchSeconds);
        Assert.True(first.Taken);
        Assert.Equal((int)Math.Round(Logging.StickYield(player.Player, 12) * rate), Count(first.Drops, stick));
        Assert.Equal(8, trunk.Trunk!.Attributes.GetInt(Trunks.BranchCountKey));
        Assert.Equal("loggingmod:treetrunk-oak-md-yes-north", trunk.Trunk.Collectible.Code.ToString());
        Assert.Equal(10, trunk.Logs);

        var second = Hold(player, trunk, Knife, TrunkHarvest.BranchSeconds);
        Assert.Equal((int)Math.Round(Logging.StickYield(player.Player, 8) * rate), Count(second.Drops, stick));
        Assert.Equal("loggingmod:treetrunk-oak-md-no-north", trunk.Trunk!.Collectible.Code.ToString());
        Assert.False(trunk.Trunk.Attributes.HasAttribute(Trunks.BranchCountKey));
        Assert.Equal(10, trunk.Logs);

        // nothing left to cut: the knife passes the click on
        Assert.False(Hold(player, trunk, Knife, TrunkHarvest.BranchSeconds).Taken);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task Shears_make_a_sapling_from_twelve_branches()
    {
        var (trunk, player) = await Setup(42, Trunk(10, branches: 17));
        var sapling = new AssetLocation("game:sapling-oak-free");
        var held = Hold(player, trunk, Shears, TrunkHarvest.BranchSeconds);
        Assert.True(held.Taken);
        Assert.InRange(Count(held.Drops, sapling), 1, 2);
        Assert.Equal(5, trunk.Trunk!.Attributes.GetInt(Trunks.BranchCountKey));
        // fewer than twelve: no hold
        Assert.False(Hold(player, trunk, Shears, TrunkHarvest.BranchSeconds).Taken);
        Assert.Equal(5, trunk.Trunk!.Attributes.GetInt(Trunks.BranchCountKey));
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_saw_cuts_a_log_into_planks()
    {
        var (trunk, player) = await Setup(56, Trunk(5));
        var held = Hold(player, trunk, Saw, TrunkHarvest.ToolSeconds);
        Assert.True(held.Taken);
        Assert.Equal(Logging.TreeTrunkPlankYield, Count(held.Drops, Logging.PlankCode("oak")));
        Assert.Equal(4, trunk.Logs);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task An_axe_and_a_saw_refuse_a_branched_trunk()
    {
        Assert.True(Logging.RequireBranchRemoval);
        var (trunk, player) = await Setup(70, Trunk(10, branches: 5));
        foreach (var tool in new[] { Axe, Saw })
        {
            var held = Hold(player, trunk, tool, 1f);
            // taken, so the click goes nowhere else, and idles until let go
            Assert.True(held.Taken);
            Assert.True(held.StepGoesOn);
            Assert.Empty(held.Drops);
            Assert.Equal(10, trunk.Logs);
        }
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_bark_spud_debarks_a_clean_trunk_in_one_hold()
    {
        var (trunk, player) = await Setup(84, Trunk(10, branches: 4));
        float seconds = TrunkWeight.SpudSeconds(10, Mod.Config);
        Assert.Equal(5f, seconds);

        // branches first
        var refused = Hold(player, trunk, Spud, seconds);
        Assert.True(refused.Taken);
        Assert.Empty(refused.Drops);
        Assert.False(Trunks.IsDebarked(trunk.Trunk));
        trunk.SetTrunk(TrunkHarvest.WithBranches(trunk.Trunk!, 0, W));

        // short of its time: nothing
        Assert.Empty(Hold(player, trunk, Spud, 2f).Drops);
        Assert.False(Trunks.IsDebarked(trunk.Trunk));

        var held = Hold(player, trunk, Spud, seconds);
        Assert.True(held.Taken);
        Assert.False(held.StepGoesOn);
        Assert.True(Trunks.IsDebarked(trunk.Trunk));
        Assert.Equal("loggingmod:treetrunk-oak-md-debarked-north", trunk.Trunk!.Collectible.Code.ToString());
        Assert.Equal(10, trunk.Logs);
        int bark = held.Drops.Where(d => d.Collectible.Code.Domain == "immersivewoodworking").Sum(d => d.StackSize);
        output.WriteLine($"bark: {string.Join(", ", held.Drops.Select(d => $"{d.StackSize} {d.Collectible.Code}"))}");
        Assert.True(bark > 0, "no bark");
        // merged: one stack per kind
        Assert.Equal(held.Drops.Count, held.Drops.Select(d => d.Collectible.Code.ToString()).Distinct().Count());
        var spud = held.Slot.Itemstack!;
        Assert.True(spud.Collectible.GetRemainingDurability(spud) < spud.Collectible.GetMaxDurability(spud));

        // already debarked
        var again = Hold(player, trunk, Spud, seconds);
        Assert.True(again.Taken);
        Assert.Empty(again.Drops);

        // its axe gives debarked logs
        var axed = Hold(player, trunk, Axe, TrunkHarvest.ToolSeconds);
        Assert.Equal(Logging.TreeTrunkLogYield, Count(axed.Drops, Logging.DebarkedLogCode("oak")));
        Assert.Equal(8, trunk.Logs);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_trunk_at_its_last_log_is_gone_after_the_axe()
    {
        var (trunk, player) = await Setup(98, Trunk(1, size: "xs"));
        var held = Hold(player, trunk, Axe, TrunkHarvest.ToolSeconds);
        Assert.Equal(Logging.TreeTrunkLogYield, Count(held.Drops, Logging.PlacedLogCode("oak")));
        Assert.False(trunk.Alive);
    }
}
