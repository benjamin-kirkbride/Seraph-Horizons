using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/BuckingSawmill: the bucking sawmill against the pinned Immersive Woodworking and
/// Logging Expanded. The mill is driven by a real mechanical power network (a vanilla creative
/// rotor against its power face). BuckingSawmillSettings in ModConfig/seraphhorizons.json is seeded from
/// fixtures/buckingsawmill (the [AtlasDataFiles] on <see cref="WoodworkingScenarios"/>) with
/// RevolutionsPerStoredLog at 0.5 and RaiseRevolutions at 1, so an empty cycle (down and back up)
/// is two turns and a cut takes seconds; every other setting is the default. Each scenario builds
/// its mill in its own patch of sky.
/// </summary>
public partial class WoodworkingScenarios
{
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

    // One player for every mill scenario (millhand): the world takes at most 16 clients, more than
    // the scenarios would join each with its own.
    private static IPlayer? _shared;
    private static object? _sharedWorld;

    /// <summary>The mill scenarios' player, in survival with an empty inventory and its keys up. The name is
    /// only for reading the scenarios.</summary>
    private async Task<IPlayer> Player(string name)
    {
        if (_shared == null || !ReferenceEquals(_sharedWorld, World.Api))
        {
            _shared = (await World.JoinPlayer("millhand")).Player;
            _sharedWorld = World.Api;
        }
        var player = _shared;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        player.Entity.Controls.CtrlKey = player.Entity.Controls.ShiftKey = false;
        return player;
    }

    // Whether the last Click was taken by the block.
    private bool _handled;

    /// <summary>Right-clicks <paramref name="at"/> (at <paramref name="hit"/> in that cell, its
    /// middle by default) holding <paramref name="held"/>; returns what is left in the hand.
    /// <paramref name="creative"/> clicks as a player in creative mode, back in survival after.</summary>
    private ItemStack? Click(IPlayer player, BlockPos at, ItemStack? held, bool ctrl = false, bool shift = false,
        bool creative = false, Vec3d? hit = null)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.CtrlKey = ctrl;
        player.Entity.Controls.ShiftKey = shift;
        if (creative)
            player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        try
        {
            var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = hit ?? new Vec3d(0.5, 0.5, 0.5) };
            _handled = W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
        }
        finally
        {
            player.Entity.Controls.CtrlKey = false;
            player.Entity.Controls.ShiftKey = false;
            player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        }
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
        foreach (var path in new[] { "sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers", "sawmillblade-" + metal })
            Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:{path}")), player), $"could not fit {path}");
        Assert.True(mill.Complete);
    }

    /// <summary>A creative rotor against the power face; waits until the mill's shaft turns. With
    /// <paramref name="full"/>, the rotor is set to its top speed and torque (10 and 10, as a player
    /// gets by right-clicking it), and this waits until the shaft is up to speed.</summary>
    private async Task Power(BEBuckingMill mill, bool full = false)
    {
        var rotorPos = RotorPos(mill);
        var face = Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(mill.CellPos(Rig.PowerCell))).PowerFace;
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        if (full)
        {
            await World.Ticks(2);
            var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
            HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
            HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
            rotor.Blockentity.MarkDirty(true);
            await World.Until(() => mill.ShaftSpeed >= 0.8f, 10000);
        }
        await World.Until(() => mill.ShaftSpeed >= Mod.Config.MinSpeed, 2000);
    }

    private BlockPos RotorPos(BEBuckingMill mill)
    {
        var ghost = mill.CellPos(Rig.PowerCell);
        return ghost.AddCopy(Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace);
    }

    /// <summary>Takes the rotor away; waits until the shaft has run down below the mill's speed.</summary>
    private async Task Unpower(BEBuckingMill mill)
    {
        W.BlockAccessor.SetBlock(0, RotorPos(mill));
        await World.Until(() => mill.ShaftSpeed < Mod.Config.MinSpeed, 20000);
    }

    private ItemStack Trunk(string wood, int logs, bool branched = false, string size = "sm")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-{(branched ? "yes" : "no")}-north"));
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

    [AtlasScenario, ReadsBootLog]
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
        // Not at z 60: the parts would fall into ChopperOutput's hopper room at x -60.
        var pos = Sky(-60 + 30 * index, -90);
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

        // The blade kit needs both sashes: refused, and kept in hand.
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.False(mill.HasBladeKit);
        // Parts go in through a ghost as well as the frame, and are used up.
        Assert.Null(Click(player, ghost, ItemOf($"{Iw}:sawmillsash")));
        Assert.Equal(1, mill.SashCount);
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.False(mill.HasBladeKit);
        // No help line for the kit until it can go in.
        Assert.DoesNotContain(W.BlockAccessor.GetBlock(pos).GetPlacedBlockInteractionHelp(W, new BlockSelection { Position = pos }, player),
            wi => wi.ActionLangCode == "seraphhorizons:blockhelp-buckingmill-fitblades");
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:sawmillsash")));
        Assert.Contains(W.BlockAccessor.GetBlock(pos).GetPlacedBlockInteractionHelp(W, new BlockSelection { Position = pos }, player),
            wi => wi.ActionLangCode == "seraphhorizons:blockhelp-buckingmill-fitblades");
        // One kit puts a blade in both saws.
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.True(mill.HasBladeKit);
        Assert.Equal("copper", mill.BladeMetal);
        // A second kit, of any metal, is refused.
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-copper")));
        Assert.NotNull(Click(player, pos, ItemOf($"{Iw}:sawmillblade-iron")));
        Assert.Equal("copper", mill.BladeMetal);
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
        // Immersive Woodworking's 250, tripled (DurableSawmillBlades), and copper's speed.
        Assert.Contains("750 / 750", info);
        Assert.Contains("Cuts 1× as fast as copper blades", info);
        Assert.Contains("Stopped", info);
        Assert.Contains("Saws at the top", info);
        Assert.Equal("Bucking sawmill", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Equal(Mod.Config.Resistance, mill.Power!.GetResistance());

        // Ctrl takes the blade kit back (into the empty hand), and the mill is no longer complete.
        Assert.Equal("sawmillblade-copper", Click(player, pos, null, ctrl: true)?.Collectible.Code.Path);
        Assert.False(mill.HasBladeKit);
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
    public async Task A_full_cut_gives_the_configured_logs_and_wears_the_blade_a_log_per_stored_log()
    {
        var pos = Sky(0, -60);
        var player = await Player("sawyer");
        var mill = await PlaceMill(pos, "south");

        // Not before it is assembled.
        var trunk = Trunk("oak", 4);
        Assert.NotNull(Click(player, pos, trunk.Clone()));
        Assert.Null(mill.Trunk);

        Assemble(mill, player);
        // Six left: the first 4-log trunk leaves 2, the second breaks the kit.
        var kit = mill.BladeKit!;
        kit.Collectible.SetDurability(kit, 6);

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
        Assert.True(mill.HasBladeKit);
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
        // The kit lost one per stored log: 4.
        Assert.Equal(1f, Mod.Config.BladeWearPerStoredLog);
        Assert.Same(kit, mill.BladeKit);
        Assert.Equal(6 - Cutting.BladeWear(4, Mod.Config.BladeWearPerStoredLog), kit.Collectible.GetRemainingDurability(kit));
        Assert.Equal(2, kit.Collectible.GetRemainingDurability(kit));

        // The next trunk goes on at the top; cutting it wears the kit past 0: it breaks, and the
        // mill stops for a new one.
        await World.Until(() => SawDepth.AtTop(mill.Depth), 6000);
        Assert.Null(Click(player, pos, trunk.Clone()));
        Assert.NotNull(mill.Trunk);
        await World.Until(() => mill.Trunk == null, 6000);
        // The first cut's logs are thrown clear of the mill and land some 9 blocks out by now.
        Assert.Equal(16, ItemsNear(pos, 16).GetValueOrDefault("game:log-placed-oak-ud"));
        Assert.Null(mill.BladeKit);
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

        // A trunk offered mid-cycle and let go of stays in the hand.
        var trunk = Trunk("oak", 40);
        Assert.False(SawDepth.AtTop(mill.Depth));
        Assert.NotNull(Click(player, pos, trunk.Clone()));
        Assert.True(_handled);
        W.BlockAccessor.GetBlock(pos).OnBlockInteractStop(0.1f, W, player, new BlockSelection { Position = pos });
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
        // Not over ChopperOutput's rooms at x 60, z -60 to -24: the logs, cut after the scenario
        // too, fall to the ground.
        var pos = Sky(90, -30);
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

    // ---- The trunk as shown, and its boxes ----

    [AtlasScenario]
    public void A_trunk_is_shown_as_Logging_Expandeds_lg_or_xxl_model()
    {
        foreach (var (size, shown) in new[] { ("xs", "lg"), ("sm", "lg"), ("md", "lg"), ("lg", "lg"), ("xl", "xxl"), ("xxl", "xxl") })
        {
            var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-birch-{size}-yes-north"));
            Assert.Equal($"loggingmod:treetrunk-birch-{shown}-no-north", Trunks.ShownBlock(W, stack)!.Code.ToString());
            Assert.Equal(size, stack.Block.Variant["size"]);
        }
        Assert.Null(Trunks.ShownBlock(W, null));
    }

    /// <summary>Every box of the mill's cells, in world coordinates, as the game asks for them.</summary>
    private List<Cuboidd> MillBoxes(BEBuckingMill mill, bool selection)
    {
        var cells = mill.GhostCells().Select(c => c.Pos).Prepend(mill.Pos);
        return cells.SelectMany(c =>
        {
            var block = W.BlockAccessor.GetBlock(c);
            var boxes = selection ? block.GetSelectionBoxes(W.BlockAccessor, c) : block.GetCollisionBoxes(W.BlockAccessor, c);
            return (boxes ?? []).Select(b => new Cuboidd(b.X1 + c.X, b.Y1 + c.Y, b.Z1 + c.Z, b.X2 + c.X, b.Y2 + c.Y, b.Z2 + c.Z));
        }).ToList();
    }

    private static string Key(Cuboidd b) => $"{b.X1:F4},{b.Y1:F4},{b.Z1:F4},{b.X2:F4},{b.Y2:F4},{b.Z2:F4}";

    /// <summary>The boxes in <paramref name="with"/> that are not in <paramref name="without"/>.</summary>
    private static List<Cuboidd> Added(List<Cuboidd> with, List<Cuboidd> without)
    {
        var left = without.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var added = new List<Cuboidd>();
        foreach (var b in with)
            if (left.TryGetValue(Key(b), out int n) && n > 0)
                left[Key(b)] = n - 1;
            else
                added.Add(b);
        return added;
    }

    /// <summary>The trunk's box in the world, from the rig's bed turned to the mill's facing.</summary>
    private (Vec3d Min, Vec3d Max) TrunkWorldBox(BEBuckingMill mill, TrunkClass trunk)
    {
        var (lo, hi) = TrunkBox.Bounds(Rig.TrunkBed!, trunk);
        var a = Footprint.ToWorld(lo, mill.Side);
        var b = Footprint.ToWorld(hi, mill.Side);
        return (new Vec3d(mill.Pos.X + Math.Min(a.X, b.X), mill.Pos.Y + lo.Y, mill.Pos.Z + Math.Min(a.Z, b.Z)),
                new Vec3d(mill.Pos.X + Math.Max(a.X, b.X), mill.Pos.Y + hi.Y, mill.Pos.Z + Math.Max(a.Z, b.Z)));
    }

    private static double Volume(IEnumerable<Cuboidd> boxes) => boxes.Sum(b => (b.X2 - b.X1) * (b.Y2 - b.Y1) * (b.Z2 - b.Z1));

    /// <summary>The cell whose boxes hold <paramref name="p"/>, with the point relative to it.</summary>
    private (BlockPos Cell, Vec3d Hit) CellHolding(BEBuckingMill mill, Vec3d p)
    {
        foreach (var c in mill.GhostCells().Select(c => c.Pos).Prepend(mill.Pos))
            foreach (var b in W.BlockAccessor.GetBlock(c).GetSelectionBoxes(W.BlockAccessor, c) ?? [])
                if (p.X >= c.X + b.X1 - 1e-4 && p.X <= c.X + b.X2 + 1e-4 && p.Y >= c.Y + b.Y1 - 1e-4 && p.Y <= c.Y + b.Y2 + 1e-4
                    && p.Z >= c.Z + b.Z1 - 1e-4 && p.Z <= c.Z + b.Z2 + 1e-4)
                    return (c, new Vec3d(p.X - c.X, p.Y - c.Y, p.Z - c.Z));
        throw new Xunit.Sdk.XunitException($"no cell's box holds {p}");
    }

    /// <summary>A loaded trunk is solid and selectable where the model shows it: one box of the
    /// shown model's size on the bed (1×1×4 thin, 2×2×5 thick), clipped into the cells, on every
    /// facing. A click on it is the mill's (Ctrl takes it back), even with a block in hand, which
    /// would otherwise be placed inside it; taken out, the boxes are the frame's again.</summary>
    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(Facings))]
    public async Task A_loaded_trunk_is_solid_and_selectable(string side, int index)
    {
        var pos = Sky(-60 + 30 * index, 90);
        var player = await Player("trunkbox" + index);
        var mill = await PlaceMill(pos, side);
        Assemble(mill, player);
        var frameOnly = MillBoxes(mill, selection: false);
        Assert.Equal(frameOnly.Select(Key), MillBoxes(mill, selection: true).Select(Key));
        var soil = new ItemStack(BlockOf("game:soil-medium-normal"));

        foreach (var (size, trunk, uncovered) in new[] { ("sm", TrunkClass.Thin, 0.0), ("xl", TrunkClass.Thick, 0.734375) })
        {
            TurnToTop(mill);
            Assert.Null(Click(player, pos, Trunk("oak", 6, size: size)));
            Assert.NotNull(mill.Trunk);
            var (min, max) = TrunkWorldBox(mill, trunk);
            var added = Added(MillBoxes(mill, selection: false), frameOnly);
            Assert.Equal(Key(new Cuboidd(min.X, min.Y, min.Z, max.X, max.Y, max.Z)),
                Key(new Cuboidd(added.Min(b => b.X1), added.Min(b => b.Y1), added.Min(b => b.Z1),
                                added.Max(b => b.X2), added.Max(b => b.Y2), added.Max(b => b.Z2))));
            var (l, w, h) = TrunkBox.Size(trunk);
            Assert.Equal(l * w * h - uncovered, Volume(added), 3);
            Assert.Equal(added.Select(Key).Order(), Added(MillBoxes(mill, selection: true), frameOnly).Select(Key).Order());

            // On the trunk's top, over the middle of the bed: the click is the mill's, with a block
            // in hand too, and nothing is taken from the hand.
            var top = new Vec3d((min.X + max.X) / 2, max.Y, (min.Z + max.Z) / 2);
            var (cell, hit) = CellHolding(mill, top);
            Assert.True(mill.HitsTrunk(cell, hit));
            Assert.Equal(1, Click(player, cell, soil.Clone(), hit: hit)?.StackSize);
            Assert.True(_handled);
            // Off the trunk (below the bed's top, in the controller's cell), the block is the hand's business.
            var below = new Vec3d(0.5, 0.2, 0.5);
            Assert.False(mill.HitsTrunk(pos, below));
            Click(player, pos, soil.Clone(), hit: below);
            Assert.False(_handled);

            // Ctrl on the trunk takes it back, and the boxes go with it.
            Click(player, cell, null, ctrl: true, hit: hit);
            Assert.Null(mill.Trunk);
            Assert.Equal(frameOnly.Select(Key), MillBoxes(mill, selection: false).Select(Key));
        }
    }

    // ---- Blades ----

    /// <summary>The blade kit's speed comes from the game's saw of its metal: 1 + 0.35 per tool tier
    /// above copper's. It shortens the cut, and only the cut.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_better_blade_kit_cuts_faster()
    {
        var expected = new Dictionary<string, float>
        {
            ["copper"] = 1, ["gold"] = 1, ["silver"] = 1,
            ["tinbronze"] = 1.35f, ["bismuthbronze"] = 1.35f, ["blackbronze"] = 1.35f,
            ["iron"] = 1.7f, ["meteoriciron"] = 1.7f, ["steel"] = 2.05f,
        };
        foreach (var (metal, speed) in expected)
        {
            Assert.Equal(speed, Mod.BladeSpeed(ItemOf($"{Iw}:sawmillblade-{metal}")), 3);
            // The fallback for a metal with no saw: its metal tier, one below the saws'.
            Assert.Equal(W.GetItem(new AssetLocation("game", "saw-" + metal))!.ToolTier, Mod.MetalPropertyTier(metal) + 1);
        }

        var pos = Sky(30, 120);
        var player = await Player("steelsaw");
        var mill = await PlaceMill(pos, "west");
        Assemble(mill, player, "steel");
        Assert.Equal(2.05f, mill.BladeSpeed, 3);
        Assert.Contains("Cuts 2.05× as fast as copper blades", Info(mill, player));
        var tree = new TreeAttribute();
        mill.ToTreeAttributes(tree);
        Assert.Equal(2.05f, tree.GetFloat("bladeSpeed"), 3);

        // Unpowered, turned by hand. Four logs take 4 × 0.5 turns with copper, 2.05 times fewer with steel.
        Assert.Null(Click(player, pos, Trunk("oak", 4)));
        float cut = 4 * Mod.Config.RevolutionsPerStoredLog / 2.05f;
        Turn(mill, cut / 2);
        Assert.Equal(0.5f, mill.Progress, 2);
        KillItemsNear(pos);
        Turn(mill, cut / 2 + 0.01f);
        Assert.Null(mill.Trunk);
        Assert.Equal(8, ItemsNear(pos).GetValueOrDefault("game:log-placed-oak-ud"));
        // The windlass is not the blade's: the saws rise from the bed at the same rate.
        Assert.True(mill.Rising);
        Assert.Equal(1f, mill.Depth);
        Turn(mill, 0.5f * Mod.Config.RaiseRevolutions);
        Assert.Equal(0.5f, mill.Depth, 3);
    }

    /// <summary><c>DurableSawmillBlades</c>: every blade kit has three times Immersive Woodworking's
    /// durability.</summary>
    [AtlasScenario]
    public void Blade_kits_last_three_times_as_long()
    {
        Assert.True(SeraphHorizonsSystem.ConfigFor(World.Api).DurableSawmillBlades);
        var kits = W.Items.Where(i => i?.Code is { Domain: Iw } c && c.Path.StartsWith(Parts.BladePrefix)).ToList();
        Assert.Equal(SawmillBladeDurability.Shipped.Keys.Order(), kits.Select(k => k.Variant["metal"]).Order());
        foreach (var kit in kits)
            Assert.Equal(SawmillBladeDurability.Factor * SawmillBladeDurability.Shipped[kit.Variant["metal"]],
                kit.GetMaxDurability(new ItemStack(kit)));
    }

    // ---- Creative ----

    /// <summary>The woodworking stations' creative shortcut (<see cref="CreativeUpgrades"/>): in
    /// creative, Ctrl + right click (not Shift) fits the next part, whatever is held, with nothing
    /// taken; on the assembled mill it takes the kit back, as in survival, and the next click fits
    /// a new one.</summary>
    [AtlasScenario]
    public async Task In_creative_a_Ctrl_click_fits_the_next_part()
    {
        var pos = Sky(60, 120);
        var player = await Player("creativemill");
        var mill = await PlaceMill(pos, "north");
        var ghost = mill.GhostCells().First().Pos;
        Assert.True(mill.CreativeShortcut);

        // Not in survival, and not with Shift.
        Click(player, pos, null, ctrl: true);
        Click(player, pos, null, ctrl: true, shift: true, creative: true);
        Assert.Equal(0, mill.SashCount);

        // The help line, for a player in creative only.
        bool Help()
        {
            var help = W.BlockAccessor.GetBlock(pos).GetPlacedBlockInteractionHelp(W, new BlockSelection { Position = pos }, player);
            return help.Any(wi => wi.ActionLangCode == CreativeUpgrades.HelpKey);
        }
        Assert.False(Help());
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        Assert.True(Help());
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;

        // Each click the next part, through a ghost as well, whatever is held; the hand keeps it.
        var steps = new (Func<bool> Done, string What)[]
        {
            (() => mill.SashCount == 1, "a sash"),
            (() => mill.SashCount == 2, "the second sash"),
            (() => mill.HasCrankshaft, "the crankshaft"),
            (() => mill.HasLevers, "the levers"),
            (() => mill.BladeMetal == AssembledMachines.DefaultMetal, "a steel blade kit"),
        };
        foreach (var (done, what) in steps)
        {
            var left = Click(player, ghost, ItemOf("game:stick", 5), ctrl: true, creative: true);
            Assert.True(_handled);
            Assert.True(done(), $"no {what}");
            Assert.Equal(("game:stick", 5), (left?.Collectible.Code.ToString(), left?.StackSize ?? 0));
        }
        Assert.True(mill.Complete);
        Assert.Empty(ItemsNear(pos));

        // Assembled: Ctrl takes the kit back, and the next Ctrl fits one again.
        Assert.Equal("sawmillblade-steel", Click(player, pos, null, ctrl: true, creative: true)?.Collectible.Code.Path);
        Assert.False(mill.Complete);
        Click(player, pos, null, ctrl: true, creative: true);
        Assert.True(mill.Complete);
    }

    // ---- Feeding: the load window, holds, the rack's place and state ----

    /// <summary>
    /// The user's report: at a real shaft speed a trunk offered by hand after the first cut never
    /// went on. Measured at the creative rotor's top speed on the default travel (RaiseRevolutions 6):
    /// the share of ticks the saws are in the load window, which is the chance that a single click
    /// lands in it. A click is refused outside it; a held click (the mill keeps the request while the
    /// button is down) goes on at the next top, cut after cut.
    /// </summary>
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task At_full_speed_a_held_trunk_goes_on_at_the_next_top_every_cycle()
    {
        var pos = Sky(-30, 150);
        var player = await Player("fastfeeder");
        var mill = await PlaceMill(pos, "south");
        Assemble(mill, player, "steel");
        float raise = Mod.Config.RaiseRevolutions, perLog = Mod.Config.RevolutionsPerStoredLog;
        Mod.Config.RaiseRevolutions = MillConfig.Defaults.RaiseRevolutions;
        try
        {
            await Power(mill, full: true);
            // An empty cycle, sampled every tick.
            await World.Until(() => mill.Rising, 20000);
            await World.Until(() => !mill.Rising, 20000);
            await World.Until(() => mill.Rising, 20000);
            int ticks = 0, open = 0;
            bool wasRising = true;
            while (ticks < 2000)
            {
                await World.Ticks(1);
                ticks++;
                if (SawDepth.AtTop(mill.Depth))
                    open++;
                if (wasRising && !mill.Rising)
                    wasRising = false;
                else if (!wasRising && mill.Rising)
                    break;   // one whole cycle: up, over the top, down to the bed
            }
            output.WriteLine($"full speed: shaft speed {mill.ShaftSpeed:0.00}, one empty cycle of {ticks} ticks, "
                             + $"{open} of them in the load window ({100.0 * open / ticks:0.0}%)");
            Assert.True(open < ticks / 4, "the load window is not narrow at this speed");

            // A click outside the window is refused, and with no hold it stays refused.
            await World.Until(() => mill.Depth > 0.5f && !mill.Rising, 20000);
            var trunk = Trunk("oak", 2);
            Assert.NotNull(Click(player, pos, trunk.Clone()));
            Assert.True(_handled);
            // The click's hold stays while the button is down: the trunk goes on at the next top.
            for (int cut = 1; cut <= 3; cut++)
            {
                await World.Until(() => mill.Trunk != null, 20000);
                Assert.Null(player.InventoryManager.ActiveHotbarSlot.Itemstack);
                W.BlockAccessor.GetBlock(pos).OnBlockInteractStop(1, W, player, new BlockSelection { Position = pos });
                await World.Until(() => mill.Trunk == null, 30000);
                output.WriteLine($"cut {cut} done");
                // pressed again mid-cycle, and held
                await World.Until(() => mill.Depth > 0.3f, 20000);
                Click(player, pos, trunk.Clone());
            }
            await World.Until(() => mill.Trunk != null, 20000);
            W.BlockAccessor.GetBlock(pos).OnBlockInteractStop(1, W, player, new BlockSelection { Position = pos });

            // Let go before the top, the request is gone: the trunk stays in the hand.
            await World.Until(() => mill.Trunk == null, 30000);
            await World.Until(() => mill.Depth > 0.3f, 20000);
            Click(player, pos, trunk.Clone());
            W.BlockAccessor.GetBlock(pos).OnBlockInteractCancel(0.1f, W, player, new BlockSelection { Position = pos }, EnumItemUseCancelReason.ReleasedMouse);
            await World.Until(() => mill.Rising, 20000);
            await World.Until(() => !mill.Rising, 20000);
            await World.Ticks(5);
            Assert.Null(mill.Trunk);
            Assert.NotNull(player.InventoryManager.ActiveHotbarSlot.Itemstack);
        }
        finally
        {
            Mod.Config.RaiseRevolutions = raise;
            Mod.Config.RevolutionsPerStoredLog = perLog;
        }
    }

    /// <summary>A rack placed as a player places it (Logging Expanded's own placement, from a
    /// player looking each of the four ways) against each of the three ground cells beyond the
    /// infeed end is found, whichever way it turned out; the mill says what the rack offers.</summary>
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_rack_placed_by_a_player_against_the_infeed_end_is_found_every_way()
    {
        var pos = Sky(30, 150);
        var player = await Player("rackplacer");
        var mill = await PlaceMill(pos, "east");
        Assemble(mill, player);
        var infeed = BlockFacing.FromCode(Footprint.ToWorld(Rig.InfeedSide, mill.Side).Code());
        var across = infeed.GetCW();
        var touching = Rig.InfeedNeighbours().Select(mill.CellPos).ToList();
        Assert.Equal(3, touching.Count);
        var rackBlock = BlockOf("loggingmod:trunkstorage-oak-empty-north");
        // a floor under the racks
        foreach (var t in touching)
            for (int out_ = 0; out_ <= 2; out_++)
                for (int side = -2; side <= 2; side++)
                    World.SetBlock("game:rock-granite", t.AddCopy(infeed, out_).AddCopy(across, side).DownCopy());
        void ClearRacks()
        {
            foreach (var t in touching)
                for (int out_ = 0; out_ <= 2; out_++)
                    for (int side = -2; side <= 2; side++)
                        W.BlockAccessor.SetBlock(0, t.AddCopy(infeed, out_).AddCopy(across, side));
        }
        Assert.Equal(RackState.None, mill.CheckRack(out _));

        var results = new List<string>();
        foreach (var look in BlockFacing.HORIZONTALS)
            foreach (var cell in touching)
            {
                ClearRacks();
                // the player a few blocks back, looking along `look` at the cell
                var at = new Vec3d(cell.X + 0.5 - 3 * look.Normali.X, cell.Y, cell.Z + 0.5 - 3 * look.Normali.Z);
                player.Entity.Pos.SetPos(at);
                player.Entity.Pos.Yaw = (float)Math.Atan2(-look.Normali.X, -look.Normali.Z);
                var sel = new BlockSelection { Position = cell.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.0, 0.5) };
                string failure = "";
                bool placed = rackBlock.TryPlaceBlock(W, player, new ItemStack(rackBlock), sel, ref failure);
                string where = "against the end";
                if (!placed)
                {
                    // Logging Expanded refuses it where its second block would go into the mill: a
                    // player then puts it one block further out, and that second block reaches the end.
                    sel.Position = cell.AddCopy(infeed);
                    at.Add(infeed.Normali.X, 0, infeed.Normali.Z);
                    player.Entity.Pos.SetPos(at);
                    failure = "";
                    placed = rackBlock.TryPlaceBlock(W, player, new ItemStack(rackBlock), sel, ref failure);
                    where = "one block out";
                }
                await World.Ticks(2);
                var state = mill.CheckRack(out _);
                string turned = W.BlockAccessor.GetBlock(sel.Position).Code?.ToString() ?? "air";
                results.Add($"look {look.Code}, cell {cell}: placed {placed} {where} ({failure}) as {turned}: {state}");
                Assert.True(placed, results[^1]);
                Assert.Equal(RackState.Empty, state);
            }
        foreach (var line in results)
            output.WriteLine(line);

        // What the rack offers, said in the info: empty, a branched trunk, a trunk that goes on.
        ClearRacks();
        World.SetBlock($"loggingmod:trunkstorage-oak-empty-{infeed.Code}", touching[1]);
        await World.Ticks(2);
        var rack = Mod.Logging!.IsRack(W.BlockAccessor.GetBlockEntity(touching[1])) ? W.BlockAccessor.GetBlockEntity(touching[1])! : null;
        Assert.NotNull(rack);
        await World.Until(() => mill.RackState == RackState.Empty, 5000);
        Assert.Contains("Rack: empty", Info(mill, player));
        rack!.GetType().GetMethod("PushTrunk")!.Invoke(rack, [Trunk("oak", 4, branched: true)]);
        await World.Until(() => mill.RackState == RackState.Branched, 5000);
        Assert.Contains("still has branches", Info(mill, player));
        var tree = new TreeAttribute();
        mill.ToTreeAttributes(tree);
        Assert.Equal((int)RackState.Branched, tree.GetInt("rackState"));
        Assert.NotNull(Mod.Logging!.PopTrunk(rack));
        rack.GetType().GetMethod("PushTrunk")!.Invoke(rack, [Trunk("oak", 4)]);
        await World.Until(() => mill.RackState == RackState.Ready, 5000);
        Assert.Contains("goes on when the saws come to the top", Info(mill, player));
        // Unpowered, nothing is pulled; with an empty hand and no trunk, the player is told.
        Assert.Null(mill.Trunk);
        Assert.Null(Click(player, pos, null));
        Assert.Null(mill.Trunk);
        await Power(mill);
        await World.Until(() => mill.Trunk != null, 8000);
    }

    /// <summary>A stopped mill's saws are wound up by hand: hold right-click with an empty hand.
    /// Letting go stops them where they are. A trunk cut part way stays on, with its progress; when
    /// the mill turns again the saws come down onto it at its cut and finish it, with the right
    /// logs and wear.</summary>
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_stopped_mills_saws_are_wound_up_by_hand_and_a_cut_carries_on()
    {
        var pos = Sky(60, 150);
        var player = await Player("winder2");
        var mill = await PlaceMill(pos, "west");
        Assemble(mill, player);
        var sel = new BlockSelection { Position = pos };
        var block = W.BlockAccessor.GetBlock(pos);

        // Empty: stopped low in its sinking.
        await Power(mill);
        await World.Until(() => !mill.Rising && mill.Depth > 0.5f, 10000);
        await Unpower(mill);
        float low = mill.Depth;
        Assert.False(SawDepth.AtTop(low));
        Assert.Contains("hold right-click with an empty hand to wind them up", Info(mill, player));
        Assert.Contains(block.GetPlacedBlockInteractionHelp(W, sel, player), wi => wi.ActionLangCode == "seraphhorizons:blockhelp-buckingmill-windup");
        // a trunk in hand is refused, and says how
        Assert.NotNull(Click(player, pos, Trunk("oak", 4)));
        Assert.Equal(low, mill.Depth);
        // held: the saws rise; let go: they stop
        Assert.Null(Click(player, pos, null));
        Assert.True(block.OnBlockInteractStep(0.1f, W, player, sel));
        await World.Ticks(6);
        block.OnBlockInteractStop(0.3f, W, player, sel);
        float part = mill.Depth;
        Assert.True(part < low, $"no winding: {low} to {part}");
        await World.Ticks(10);
        Assert.Equal(part, mill.Depth);
        // held again, to the top, in about HandWindSeconds for the whole travel
        Click(player, pos, null);
        await World.Until(() => mill.Depth <= 0, (int)(Feeding.HandWindSeconds * 1000) + 3000);
        Assert.False(mill.Rising);
        Assert.False(block.OnBlockInteractStep(1f, W, player, sel));
        Assert.Contains("Saws at the top", Info(mill, player));

        // Mid-cut: load at the top, cut half by hand, wind up, and power it again.
        var kit = mill.BladeKit!;
        int durability = kit.Collectible.GetRemainingDurability(kit);
        Assert.Null(Click(player, pos, Trunk("oak", 4)));
        Turn(mill, 0.5f * 4 * Mod.Config.RevolutionsPerStoredLog);
        Assert.Equal(0.5f, mill.Progress, 2);
        Assert.False(Cutting.Recoverable(mill.Progress));
        Click(player, pos, null);
        await World.Until(() => mill.Depth <= 0, (int)(Feeding.HandWindSeconds * 1000) + 3000);
        block.OnBlockInteractStop(2f, W, player, sel);
        Assert.NotNull(mill.Trunk);
        Assert.Equal(0.5f, mill.Progress, 2);
        // still too far cut to take out: the rule is the progress, not where the saws are
        Click(player, pos, null, ctrl: true);
        Assert.NotNull(mill.Trunk);

        KillItemsNear(pos);
        await Power(mill);
        // the saws come down onto the trunk where the cut is, and carry on
        await World.Until(() => mill.Phase == MillPhase.Cutting, 6000);
        float touch = SawDepth.Touch(Rig.Saw, Rig.TrunkBed, "sm");
        Assert.True(mill.Depth >= SawDepth.Cutting(touch, 0.5f) - 0.01f, $"depth {mill.Depth}");
        Assert.True(mill.Progress >= 0.5f);
        await World.Until(() => mill.Trunk == null, 10000);
        Assert.Equal(8, ItemsNear(pos).GetValueOrDefault("game:log-placed-oak-ud"));
        Assert.Equal(durability - 4, kit.Collectible.GetRemainingDurability(kit));
    }
}
