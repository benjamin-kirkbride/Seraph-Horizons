using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/BuckingSawmill: the bucking sawmill against the pinned Immersive Woodworking and
/// Logging Expanded. The mill is driven by a real mechanical power network (a vanilla creative
/// rotor against its power face). BuckingSawmillSettings in ModConfig/seraphhorizons.json is seeded from
/// fixtures/buckingsawmill with RevolutionsPerStoredLog at 0.5 and RaiseRevolutions at 1, so an empty
/// cycle (down and back up) is two turns and a cut takes seconds; every other setting is the default. Each scenario builds its mill in its own patch of sky.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/buckingsawmill", TargetPath = "ModConfig")]
public class BuckingSawmillScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private BuckingSawmillSystem Mod => BuckingSawmillSystem.Of(World.Api);
    private Rig Rig => Mod.Rig ?? throw new Xunit.Sdk.XunitException("the rig did not load");

    private const string Iw = "immersivewoodworking";

    private Block BlockOf(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    private ItemStack ItemOf(string code, int size = 1) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item, size) : throw new Xunit.Sdk.XunitException($"no item {code}");

    /// <summary>A spot high above the ground, cleared well beyond the mill's footprint.</summary>
    private BlockPos Sky(int dx, int dz)
    {
        var origin = World.Spawn.AddCopy(dx, 30, dz);
        for (int x = -9; x <= 9; x++)
        for (int y = -1; y <= 6; y++)
        for (int z = -9; z <= 9; z++)
            W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        return origin;
    }

    private async Task<BEBuckingMill> PlaceMill(BlockPos pos, string side)
    {
        World.SetBlock($"seraphhorizons:buckingmill-frame-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BEBuckingMill
               ?? throw new Xunit.Sdk.XunitException($"no mill block entity at {pos}");
    }

    private async Task<IPlayer> Player(string name)
    {
        var player = (await World.JoinPlayer(name)).Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        return player;
    }

    // Whether the last Click was taken by the block.
    private bool _handled;

    /// <summary>Right-clicks <paramref name="at"/> holding <paramref name="held"/>; returns what is
    /// left in the hand.</summary>
    private ItemStack? Click(IPlayer player, BlockPos at, ItemStack? held, bool ctrl = false)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.CtrlKey = ctrl;
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        _handled = W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
        player.Entity.Controls.CtrlKey = false;
        return slot.Itemstack;
    }

    private static string Info(BEBuckingMill mill, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        mill.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private void Assemble(BEBuckingMill mill, IPlayer player, string metal = "copper")
    {
        foreach (var path in new[] { "sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers", "sawmillblade-" + metal, "sawmillblade-" + metal })
            Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:{path}")), player), $"could not fit {path}");
        Assert.True(mill.Complete);
    }

    /// <summary>A creative rotor against the power face; waits until the mill's shaft turns.</summary>
    private async Task Power(BEBuckingMill mill)
    {
        var ghost = mill.CellPos(Rig.PowerCell);
        var face = Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        World.SetBlock($"game:creativerotor-{face.Code}", ghost.AddCopy(face));
        await World.Until(() => mill.ShaftSpeed >= Mod.Config.MinSpeed, 2000);
    }

    private ItemStack Trunk(string wood, int logs, bool branched = false)
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-sm-{(branched ? "yes" : "no")}-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        if (branched)
            stack.Attributes.SetInt("branchCount", 3);
        return stack;
    }

    /// <summary>The stacks of item entities in a tall box around <paramref name="around"/>, by code.</summary>
    private Dictionary<string, int> ItemsNear(BlockPos around, int radius = 8)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 40, around.Z - radius, around.X + radius, around.Y + 8, around.Z + radius);
        return World.EntitiesIn(box).OfType<EntityItem>().Where(e => e.Alive)
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
    }

    private void KillItemsNear(BlockPos around, int radius = 8)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 40, around.Z - radius, around.X + radius, around.Y + 8, around.Z + radius);
        foreach (var e in World.EntitiesIn(box).OfType<EntityItem>())
            e.Die(EnumDespawnReason.Removed);
    }

    // ---- Loading ----

    [AtlasScenario]
    public void The_mod_loads_cleanly_with_its_blocks_and_recipe()
    {
        Assert.True(BuckingSawmillSystem.Applies(World.Api));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("bucking", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.NotNull(Mod.Rig);
        foreach (var side in Sides.All)
        {
            Assert.IsType<BlockBuckingMill>(BlockOf($"seraphhorizons:buckingmill-frame-{side.Code()}"));
            Assert.IsType<BlockMillGhostPower>(BlockOf($"seraphhorizons:buckingmill-ghostpower-{side.Code()}"));
        }
        Assert.IsType<BlockMillGhost>(BlockOf("seraphhorizons:buckingmill-ghost"));

        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:buckingmill-frame-north");
        Assert.NotNull(recipe.Output!.ResolvedItemStack);
        var ingredients = (recipe.ResolvedIngredients ?? []).OfType<CraftingRecipeIngredient>().ToList();
        var codes = ingredients.Select(i => i.Code!.ToString()).ToList();
        Assert.Equal(2, codes.Count(c => c == $"{Iw}:sawmill-frame-north"));
        Assert.Equal(4, codes.Count(c => c == "game:supportbeam-*"));
        Assert.Contains(ingredients, i => i.IsTool && i.Code!.Path.StartsWith("hammer"));
    }

    // Fails when Logging Expanded renames or reshapes a member the mill reads: update LoggingBridge.
    [AtlasScenario]
    public void The_Logging_Expanded_bridge_resolves_every_member()
    {
        var bridge = LoggingBridge.Resolve(World.Api, out var problems);
        Assert.True(bridge != null, "unresolved: " + string.Join("; ", problems));
        Assert.Empty(problems);
        Assert.NotNull(Mod.Logging);
        Assert.Equal(new AssetLocation("game:log-placed-oak-ud"), bridge.PlacedLogCode("oak"));
        Assert.True(bridge.RequireBranchRemoval);
    }

    // ---- Placing and breaking ----

    public static TheoryData<string, int> Facings() => new() { { "north", 0 }, { "east", 1 }, { "south", 2 }, { "west", 3 } };

    [AtlasTheory, MemberData(nameof(Facings))]
    public async Task Placing_stamps_the_ghosts_and_breaking_drops_the_parts(string side, int index)
    {
        var pos = Sky(-60 + 30 * index, 60);
        var player = await Player("breaker" + index);
        var mill = await PlaceMill(pos, side);

        var cells = mill.GhostCells().ToList();
        Assert.Equal(Rig.Cells.Count - 1, cells.Count);
        foreach (var (cell, power) in cells)
        {
            var block = W.BlockAccessor.GetBlock(cell);
            Assert.Equal(power ? $"seraphhorizons:buckingmill-ghostpower-{side}" : "seraphhorizons:buckingmill-ghost", block.Code.ToString());
            var ghost = Assert.IsType<BEMillGhost>(W.BlockAccessor.GetBlockEntity(cell));
            Assert.Equal(pos, ghost.Principal);
        }
        // The power ghost takes the axle on the rig's power face, turned with the mill.
        var powerGhost = Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(mill.CellPos(Rig.PowerCell)));
        Assert.Equal(Footprint.ToWorld(Rig.PowerFace, mill.Side).Code(), powerGhost.PowerFace.Code);

        Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:sawmillsash")), player));
        Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:sawmillsash")), player));
        Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:sawmillcrankshaft")), player));
        Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:sawmilllevers")), player));
        Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:sawmillblade-iron")), player));
        KillItemsNear(pos);

        // West breaks a ghost: it breaks the whole mill.
        var broken = side == "west" ? cells[^1].Pos : pos;
        W.BlockAccessor.GetBlock(broken).OnBlockBroken(W, broken, player);
        await World.Ticks(2);

        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
        var drops = ItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:buckingmill-frame-north"));
        Assert.Equal(2, drops.GetValueOrDefault($"{Iw}:sawmillsash"));
        Assert.Equal(1, drops.GetValueOrDefault($"{Iw}:sawmillcrankshaft"));
        Assert.Equal(1, drops.GetValueOrDefault($"{Iw}:sawmillblade-iron"));
        Assert.Equal(1, drops.GetValueOrDefault($"{Iw}:sawmilllevers"));
    }

    /// <summary>A player standing a few blocks back from the target, looking along <paramref name="look"/>,
    /// places the frame item: the mill extends away from them.</summary>
    [AtlasTheory, MemberData(nameof(Facings))]
    public async Task A_placed_mill_extends_away_from_the_player(string look, int index)
    {
        var pos = Sky(-30 + 30 * index, 30);
        var player = await Player("placer" + index);
        var d = BlockFacing.FromCode(look).Normali;
        var at = new Vec3d(pos.X + 0.5 - 3 * d.X, pos.Y, pos.Z + 0.5 - 3 * d.Z);
        player.Entity.Pos.SetPos(at);
        player.Entity.ServerPos.SetPos(at);
        var item = BlockOf("seraphhorizons:buckingmill-frame-north");
        var sel = new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        string failure = "";
        Assert.True(item.TryPlaceBlock(W, player, new ItemStack(item), sel, ref failure), $"not placed: {failure}");
        await World.Ticks(5);

        // the clicked block is the controller, turned so the axle end is the far one
        var mill = Assert.IsType<BEBuckingMill>(W.BlockAccessor.GetBlockEntity(pos));
        Assert.True(Sides.TryParse(look, out var lookSide));
        Assert.Equal(Footprint.PlacedFacing(lookSide), mill.Side);
        int Along(BlockPos p) => (p.X - pos.X) * d.X + (p.Z - pos.Z) * d.Z;
        var cells = mill.GhostCells().Select(c => c.Pos).ToList();
        Assert.Equal(0, cells.Min(Along));
        var power = mill.CellPos(Rig.PowerCell);
        Assert.Equal(cells.Max(Along), Along(power));
        Assert.Equal(5, Along(power));
        Assert.Equal(look, Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(power)).PowerFace.Code);
        // the rack's cells are beyond the far end, the logs come out of the near end
        Assert.All(Rig.InfeedNeighbours(), n => Assert.Equal(6, Along(mill.CellPos(n))));
        var o = Footprint.ToWorld(Rig.OutputPos, mill.Side);
        Assert.True((o.X - 0.5) * d.X + (o.Z - 0.5) * d.Z < -0.5, $"output {o} not in front of the near end");
    }

    [AtlasScenario]
    public async Task A_lost_ghost_is_restamped_after_reload()
    {
        var pos = Sky(60, 60);
        var mill = await PlaceMill(pos, "south");
        var (cell, _) = mill.GhostCells().First();
        W.BlockAccessor.SetBlock(0, cell);
        Assert.Equal(0, W.BlockAccessor.GetBlock(cell).Id);
        mill.EnsureGhosts();
        Assert.Equal(pos, Assert.IsType<BEMillGhost>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
    }

    // ---- Assembly ----

    [AtlasScenario]
    public async Task Assembly_follows_the_rules()
    {
        var pos = Sky(-60, -60);
        var player = await Player("assembler");
        var mill = await PlaceMill(pos, "east");
        var ghost = mill.GhostCells().First().Pos;

        // Info and help lead the player through it (built on the server here; the client's help
        // also lists the part stacks).
        string info = Info(mill, player);
        Assert.Contains("Unassembled", info);
        Assert.Contains("2 × Saw sash", info);
        Assert.Contains("Sawmill crankshaft", info);
        Assert.Contains("Sawmill feed levers", info);
        Assert.Contains(W.BlockAccessor.GetBlock(pos).GetPlacedBlockInteractionHelp(W, new BlockSelection { Position = pos }, player),
            wi => wi.ActionLangCode == "seraphhorizons:blockhelp-buckingmill-fitpart");

        // A blade kit needs a sash: refused, and kept in hand.
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.Equal(0, mill.BladeCount);
        // Parts go in through a ghost as well as the frame, and are used up.
        Assert.Null(Click(player, ghost, ItemOf($"{Iw}:sawmillsash")));
        Assert.Equal(1, mill.SashCount);
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.Equal(1, mill.BladeCount);
        Assert.Equal("copper", mill.BladeMetal);
        // The second kit needs the second sash.
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:sawmillsash")));
        // And the first kit's metal.
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-iron")));
        Assert.Equal(1, mill.BladeCount);
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.Equal(2, mill.BladeCount);
        Assert.False(mill.Complete);
        // A third sash is refused.
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillsash")));
        // The carriage is not a part of this mill: holding it, the click is not the mill's.
        Assert.NotNull(Click(player, ghost, ItemOf($"{Iw}:sawmillcarriage")));
        Assert.False(_handled);
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:sawmillcrankshaft")));
        // The levers are: without them the mill is not complete.
        Assert.False(mill.Complete);
        Assert.False(mill.HasLevers);
        Assert.Contains("Sawmill feed levers", Info(mill, player));
        Assert.Null(Click(player, ghost, ItemOf($"{Iw}:sawmilllevers")));
        Assert.True(mill.HasLevers);
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmilllevers")));
        Assert.True(mill.Complete);
        // Not turning: stopped, with the saws at the top where a trunk can go on.
        Assert.Equal(MillPhase.Stopped, mill.Phase);
        info = Info(mill, player);
        Assert.DoesNotContain("Unassembled", info);
        Assert.Contains("250 / 250", info);
        Assert.Contains("Stopped", info);
        Assert.Contains("Saws at the top", info);
        Assert.Equal("Bucking sawmill", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Equal(Mod.Config.Resistance, mill.Power!.GetResistance());

        // Ctrl takes a blade kit back (into the empty hand), and the mill is no longer complete.
        Assert.Equal("sawmillblade-copper", Click(player, pos, null, ctrl: true)?.Collectible.Code.Path);
        Assert.Equal(1, mill.BladeCount);
        Assert.False(mill.Complete);
        Assert.Equal(BEBehaviorMillMP.IncompleteResistance, mill.Power!.GetResistance());
    }

    // ---- Trunks ----

    /// <summary>Turns the (unpowered) mill's cycle by hand: <paramref name="turns"/> shaft turns.</summary>
    private static void Turn(BEBuckingMill mill, float turns) => mill.Advance(turns * 2 * MathF.PI);

    /// <summary>Turns an empty mill by hand from wherever it is to the top, just as it starts down again.</summary>
    private void TurnToTop(BEBuckingMill mill)
    {
        float rr = Mod.Config.RaiseRevolutions;
        Turn(mill, ((mill.Rising ? 0 : 1 - mill.Depth) + (mill.Rising ? mill.Depth : 1)) * rr);
        Assert.True(SawDepth.AtTop(mill.Depth), $"depth {mill.Depth} after turning to the top");
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_full_cut_gives_the_configured_logs_and_wears_the_blades()
    {
        var pos = Sky(0, -60);
        var player = await Player("sawyer");
        var mill = await PlaceMill(pos, "south");

        // Not before it is assembled.
        var trunk = Trunk("oak", 4);
        Assert.NotNull(Click(player, pos, trunk.Clone()));
        Assert.Null(mill.Trunk);

        Assemble(mill, player);
        var worn = mill.Blades[1];
        worn.Collectible.SetDurability(worn, 1);
        int max = mill.Blades[0].Collectible.GetMaxDurability(mill.Blades[0]);

        // A branched trunk is refused while Logging Expanded requires debranching.
        Assert.NotNull(Click(player, pos, Trunk("oak", 4, branched: true)));
        Assert.Null(mill.Trunk);

        // Loaded by hand at the top, and taken back intact before it is cut.
        Assert.Equal(0f, mill.Depth);
        Assert.Null(Click(player, pos, trunk.Clone()));
        Assert.NotNull(mill.Trunk);
        float touch = SawDepth.Touch(Rig.Saw, Rig.TrunkBed, "sm");
        Assert.Equal(touch, mill.Depth, 4);
        Click(player, pos, null, ctrl: true);
        Assert.Null(mill.Trunk);
        var back = player.InventoryManager.GetOwnInventory("hotbar").Concat(player.InventoryManager.GetOwnInventory("backpack"))
            .Select(s => s.Itemstack).FirstOrDefault(s => s != null && Trunks.IsTrunk(s));
        Assert.NotNull(back);
        Assert.True(back.Equals(W, trunk, GlobalConstants.IgnoredStackAttributes), "the trunk came back changed");
        Assert.Equal(2, mill.BladeCount);
        // The saws dropped onto it and stay there: no trunk goes on until they have been round to the top.
        Assert.Equal(MillPhase.Stopped, mill.Phase);
        Assert.Equal(touch, mill.Depth, 4);
        Click(player, pos, null);
        Assert.Null(mill.Trunk);
        TurnToTop(mill);

        // With an empty hand, the trunk is found in the inventory.
        Assert.Null(Click(player, pos, null));
        Assert.NotNull(mill.Trunk);

        KillItemsNear(pos);
        await Power(mill);
        await World.Until(() => mill.Trunk == null, 6000);

        var items = ItemsNear(pos);
        Assert.Equal(Cutting.LogYield(4, Mod.Config.LogsPerStoredLog), items.GetValueOrDefault("game:log-placed-oak-ud"));
        Assert.Equal(8, items.GetValueOrDefault("game:log-placed-oak-ud"));
        // Each kit lost ceil(4 × 0.25) = 1: the one left at 1 broke, and the mill stops for a new one.
        var left = Assert.Single(mill.Blades);
        Assert.Equal(max - Cutting.BladeWear(4, Mod.Config.BladeWearPerStoredLog), left.Collectible.GetRemainingDurability(left));
        Assert.Equal(max - 1, left.Collectible.GetRemainingDurability(left));
        Assert.False(mill.Complete);
        Assert.Equal(0f, mill.Progress);

        // The saws are at the bed, the lift thrown in, and wait there while a blade kit is missing.
        Assert.Equal(MillPhase.Stopped, mill.Phase);
        Assert.True(mill.Rising);
        Assert.Equal(1f, mill.Depth);
        await World.Ticks(20);
        Assert.Equal(1f, mill.Depth);
        // With a new kit fitted they wind back up, pass the top and carry on round.
        Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:sawmillblade-copper")), player));
        await World.Until(() => mill.Phase == MillPhase.Raising && mill.Depth < 0.9f, 6000);
        await World.Until(() => mill.Phase == MillPhase.Sinking, 6000);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_powered_empty_mill_cycles_and_takes_a_trunk_only_at_the_top()
    {
        var pos = Sky(30, -60);
        var player = await Player("cycler");
        var mill = await PlaceMill(pos, "east");
        Assemble(mill, player);
        await Power(mill);

        // Empty, it goes on round: down to the bed, up to the top, and down again.
        await World.Until(() => mill.Phase == MillPhase.Sinking && mill.Depth > 0.5f, 6000);
        Assert.Contains("Saws coming down, no trunk", Info(mill, player));
        await World.Until(() => mill.Phase == MillPhase.Raising, 6000);
        Assert.Contains("Raising the saws", Info(mill, player));
        await World.Until(() => mill.Phase == MillPhase.Raising && mill.Depth < 0.5f, 6000);

        // A trunk offered mid-cycle stays in the hand.
        var trunk = Trunk("oak", 40);
        Assert.False(SawDepth.AtTop(mill.Depth));
        Assert.NotNull(Click(player, pos, trunk.Clone()));
        Assert.True(_handled);
        Assert.Null(mill.Trunk);

        // At the top it goes on, and the saws drop onto it.
        await World.Until(() => SawDepth.AtTop(mill.Depth), 6000);
        Assert.Null(Click(player, pos, trunk.Clone()));
        Assert.NotNull(mill.Trunk);
        // (taken on the last of the rise, the saws finish coming up before they drop onto it)
        await World.Until(() => mill.Phase == MillPhase.Cutting, 2000);
        float touch = SawDepth.Touch(Rig.Saw, Rig.TrunkBed, "sm");
        Assert.InRange(mill.Depth, touch, touch + 0.05f);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task After_a_cut_the_rack_waits_for_the_saws_to_come_up()
    {
        var pos = Sky(-60, 0);
        var player = await Player("winder");
        var mill = await PlaceMill(pos, "south");
        Assemble(mill, player);

        var logging = Mod.Logging!;
        var infeed = Footprint.ToWorld(Rig.InfeedSide, mill.Side).Code();
        var rackPos = mill.CellPos(Rig.InfeedNeighbours().First());
        World.SetBlock($"loggingmod:trunkstorage-oak-empty-{infeed}", rackPos);
        await World.Ticks(2);
        var rack = W.BlockAccessor.GetBlockEntity(rackPos)!;
        Assert.True(logging.IsRack(rack), $"no rack at {rackPos}");
        var push = rack.GetType().GetMethod("PushTrunk")!;
        push.Invoke(rack, [Trunk("oak", 2)]);
        push.Invoke(rack, [Trunk("oak", 2)]);
        rack.MarkDirty(true);
        int Count()
        {
            var be = W.BlockAccessor.GetBlockEntity(rackPos)!;
            return (int)be.GetType().GetProperty("TrunkCount")!.GetValue(be)!;
        }

        // The first trunk is pulled and cut through.
        await Power(mill);
        await World.Until(() => mill.Trunk != null, 3000);
        Assert.Equal(1, Count());
        await World.Until(() => mill.Trunk == null, 6000);

        // The saws are at the bed and the next trunk stays on the rack while they rise.
        Assert.Equal(MillPhase.Raising, mill.Phase);
        Assert.True(mill.Depth > 0.75f, $"depth {mill.Depth} right after the cut");
        Assert.False(mill.PullFromRack());
        Assert.Equal(1, Count());

        // It is pulled only once they are at the top.
        float lowest = mill.Depth;
        var until = DateTime.UtcNow.AddSeconds(10);
        while (mill.Trunk == null && DateTime.UtcNow < until)
        {
            lowest = Math.Min(lowest, mill.Depth);
            await World.Ticks(1);
        }
        Assert.NotNull(mill.Trunk);
        Assert.True(lowest <= 0.2f, $"the next trunk came with the saws still at {lowest}");
        Assert.Equal(0, Count());
        await World.Until(() => mill.Phase == MillPhase.Cutting, 2000);
    }

    [AtlasScenario]
    public async Task Taking_a_trunk_out_early_lets_the_saws_carry_on_empty()
    {
        var pos = Sky(-60, -30);
        var player = await Player("changer");
        var mill = await PlaceMill(pos, "north");
        Assemble(mill, player);
        float rr = Mod.Config.RaiseRevolutions;

        // Unpowered, so only this scenario moves the saws. A one-high trunk on the default travel
        // (3.0 to 0.5 over a bed at 0.5) drops them to 0.6.
        var trunk = Trunk("oak", 4);
        Assert.Null(Click(player, pos, trunk.Clone()));
        Assert.NotNull(mill.Trunk);
        float touch = SawDepth.Touch(Rig.Saw, Rig.TrunkBed, "sm");
        Assert.Equal(touch, mill.Depth, 4);
        // A tenth of the cut: four logs at 0.5 turns each.
        Turn(mill, 0.1f * 4 * Mod.Config.RevolutionsPerStoredLog);
        float depth = SawDepth.Cutting(touch, 0.1f);
        Assert.Equal(depth, mill.Depth, 3);

        Click(player, pos, null, ctrl: true);
        Assert.Null(mill.Trunk);
        Assert.Equal(MillPhase.Stopped, mill.Phase);
        Assert.False(mill.Rising);
        Assert.Equal(depth, mill.Depth, 3);
        Assert.Contains("Stopped", Info(mill, player));
        Assert.DoesNotContain("Saws at the top", Info(mill, player));

        // No trunk goes on mid-cycle: it stays in the hand.
        Assert.NotNull(Click(player, pos, trunk.Clone()));
        Assert.Null(mill.Trunk);

        // The saws go on down at the empty rate: a tenth of the travel in a tenth of the turns.
        Turn(mill, 0.1f * rr);
        Assert.Equal(depth + 0.1f, mill.Depth, 3);
        Assert.False(mill.Rising);
        // To the bed, where the lift is thrown in, and a quarter of the way back up.
        Turn(mill, (0.9f - depth + 0.25f) * rr);
        Assert.True(mill.Rising);
        Assert.Equal(0.75f, mill.Depth, 3);

        // Depth and direction survive saving: moved on after the save, loading puts them back.
        var tree = new TreeAttribute();
        mill.ToTreeAttributes(tree);
        Assert.Equal(0.75f, tree.GetFloat("depth"), 3);
        Turn(mill, 1 * rr);
        Assert.False(mill.Rising);
        mill.FromTreeAttributes(tree, W);
        Assert.Equal(0.75f, mill.Depth, 3);
        Assert.True(mill.Rising);
        Assert.True(mill.HasLevers);
        Assert.True(mill.Complete);

        // Still refused on the way up, and taken at the top.
        Assert.NotNull(Click(player, pos, trunk.Clone()));
        Turn(mill, 0.7f * rr);
        Assert.True(mill.Rising);
        Assert.True(SawDepth.AtTop(mill.Depth));
        Assert.Contains("Saws at the top", Info(mill, player));
        Assert.Null(Click(player, pos, trunk.Clone()));
        Assert.NotNull(mill.Trunk);
        // Loaded on the last of the rise: the saws come up to the top, then drop onto the trunk.
        Turn(mill, 0.06f * rr);
        Assert.False(mill.Rising);
        Assert.InRange(mill.Depth, touch, touch + 0.01f);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_rack_at_the_infeed_feeds_debranched_trunks_only()
    {
        var pos = Sky(60, -60);
        var player = await Player("racker");
        var mill = await PlaceMill(pos, "west");
        Assemble(mill, player);
        await Power(mill);

        var logging = Mod.Logging!;
        var infeed = Footprint.ToWorld(Rig.InfeedSide, mill.Side).Code();
        var rackPos = mill.CellPos(Rig.InfeedNeighbours().First());
        World.SetBlock($"loggingmod:trunkstorage-oak-empty-{infeed}", rackPos);
        await World.Ticks(2);
        var rack = W.BlockAccessor.GetBlockEntity(rackPos);
        Assert.True(logging.IsRack(rack), $"no rack at {rackPos}");
        // The rack's second cell is a filler, away from the mill.
        Assert.IsType<BlockMultiblock>(W.BlockAccessor.GetBlock(rackPos.AddCopy(BlockFacing.FromCode(infeed))));

        var push = rack!.GetType().GetMethod("PushTrunk")!;
        var straight = Trunk("birch", 40);
        push.Invoke(rack, [straight]);
        push.Invoke(rack, [Trunk("birch", 40, branched: true)]);
        rack.MarkDirty(true);

        // The branched trunk on top holds the line.
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until)
            await World.Ticks(5);
        Assert.Null(mill.Trunk);
        Assert.Equal(2, (int)rack.GetType().GetProperty("TrunkCount")!.GetValue(rack)!);

        // Taken away, the next one is pulled the next time the saws are at the top.
        Assert.NotNull(logging.PopTrunk(rack));
        await World.Until(() => mill.Trunk != null, 8000);
        Assert.Equal("no", mill.Trunk!.Block.Variant["branches"]);
        Assert.Equal(40, Trunks.StoredLogs(mill.Trunk, W));
        rack = W.BlockAccessor.GetBlockEntity(rackPos);
        Assert.Equal(0, (int)rack!.GetType().GetProperty("TrunkCount")!.GetValue(rack)!);
        Assert.Equal("empty", W.BlockAccessor.GetBlock(rackPos).Variant["fill"]);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_rack_is_found_through_its_filler_cell()
    {
        var pos = Sky(0, 0);
        var player = await Player("filler");
        var mill = await PlaceMill(pos, "north");
        Assemble(mill, player);
        await Power(mill);

        // The rack faces the mill, so its filler cell is the one touching the infeed side.
        var infeed = BlockFacing.FromCode(Footprint.ToWorld(Rig.InfeedSide, mill.Side).Code());
        var touching = mill.CellPos(Rig.InfeedNeighbours().First());
        var rackPos = touching.AddCopy(infeed);
        World.SetBlock($"loggingmod:trunkstorage-oak-empty-{infeed.Opposite.Code}", rackPos);
        await World.Ticks(2);
        Assert.IsType<BlockMultiblock>(W.BlockAccessor.GetBlock(touching));
        var rack = W.BlockAccessor.GetBlockEntity(rackPos)!;
        rack.GetType().GetMethod("PushTrunk")!.Invoke(rack, [Trunk("oak", 40)]);
        rack.MarkDirty(true);

        await World.Until(() => mill.Trunk != null, 8000);
        Assert.Equal(40, Trunks.StoredLogs(mill.Trunk!, W));
    }
}

/// <summary>
/// The bucking sawmill with its switch off (<c>"BuckingSawmill": false</c> in
/// ModConfig/seraphhorizons.json, seeded from fixtures/buckingsawmill-off): its blocks and its
/// recipe are not in the game, and nothing is logged about them.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/buckingsawmill-off", TargetPath = "ModConfig")]
public class BuckingSawmillOffScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public void With_the_switch_off_the_mill_is_not_in_the_game()
    {
        var world = World.Api.World;
        Assert.False(World.Api.LoadModConfig("seraphhorizons.json")["BuckingSawmill"].AsBool(true));
        Assert.False(BuckingSawmillSystem.Applies(World.Api));
        Assert.DoesNotContain(world.Blocks, b => b?.Code is { Domain: "seraphhorizons" } code && code.Path.StartsWith("buckingmill"));
        Assert.DoesNotContain(world.GridRecipes, r => r.Output?.Code?.Path.StartsWith("buckingmill") == true);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("buckingmill", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("bucking sawmill", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }
}
