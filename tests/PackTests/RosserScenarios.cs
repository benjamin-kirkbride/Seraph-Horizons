using System.Reflection;
using Atlas.XUnit;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser;
using SeraphHorizons.Mod.Rosser.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Rosser: the rosser against the pinned Immersive Woodworking, Logging
/// Expanded and Pipes and Power Expanded, driven by a real mechanical power network (a vanilla
/// creative rotor against its side power face). RosserSettings in ModConfig/seraphhorizons.json is
/// seeded from fixtures/buckingsawmill (the [AtlasDataFiles] on <see cref="WoodworkingScenarios"/>)
/// with RevolutionsPerStoredLog at 0.5 and RevolutionsPerBranch at 0.08, so a trip takes a few
/// turns; every other setting is the default. Each scenario builds its rosser on a floor of its own
/// high in the sky (sticks and bark land on it), at z 230 from spawn, clear of the other
/// woodworking scenarios; the scenarios further down use rows of their own (z 270 to 900, see
/// "Rows" there).
/// </summary>
public partial class WoodworkingScenarios
{
    private RosserSystem RosserMod => RosserSystem.Of(World.Api);
    private RosserRig RosserRig => RosserMod.Rig ?? throw new Xunit.Sdk.XunitException("the rosser's rig did not load");

    /// <summary>A spot 50 above spawn, its chunk columns loaded, cleared well beyond the rosser's
    /// footprint in every facing, on a granite floor one below it.</summary>
    private async Task<BlockPos> RosserSky(int dx, int dz, int reach = 18)
    {
        var origin = World.Spawn.AddCopy(dx, 50, dz);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        // one block in every chunk column the area touches (37 or more wide, it can span three)
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        if (World.Api is ICoreServerAPI sapi)
            foreach (var c in columns)
                sapi.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 30000);
        int floor = BlockOf("game:rock-granite").Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 6; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        return origin;
    }

    private async Task<BERosser> PlaceRosser(BlockPos pos, string side)
    {
        World.SetBlock($"seraphhorizons:rosser-frame-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BERosser
               ?? throw new Xunit.Sdk.XunitException($"no rosser block entity at {pos}");
    }

    private static string Info(BERosser rosser, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        rosser.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private WorldInteraction[] RosserHelp(BlockPos pos, IPlayer player) =>
        W.BlockAccessor.GetBlock(pos).GetPlacedBlockInteractionHelp(W, new BlockSelection { Position = pos }, player);

    /// <summary>Every stage by right-clicks with real stacks, through the frame and a ghost.</summary>
    private void AssembleRosser(BERosser rosser, IPlayer player, string heads = "steel")
    {
        var ghost = rosser.GhostCells().First().Pos;
        foreach (var (code, count) in new[] { (RosserParts.ShaftCode, 1), (RosserParts.RingCode, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4),
                                              ("game:metalplate-iron", 2), (RosserParts.LeversCode, 1), ($"{Iw}:barkspudhead-{heads}", 4) })
            Assert.True(Click(player, code.Contains("rod") ? ghost : rosser.Pos, ItemOf(code, count)) == null, $"{code} ×{count} not all fitted");
        Assert.True(rosser.Complete);
    }

    /// <summary>A creative rotor at full speed against the power face; waits until the shaft turns fast.</summary>
    private async Task PowerRosser(BERosser rosser)
    {
        var ghost = rosser.CellPos(RosserRig.PowerCell);
        var face = Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        var rotorPos = ghost.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => rosser.ShaftSpeed >= 0.5f, 15000);
    }

    /// <summary>A trunk of <paramref name="wood"/> holding <paramref name="logs"/> logs, with
    /// <paramref name="branches"/> branches counted (and the <c>yes</c> state) when above 0.</summary>
    private ItemStack RosserTrunk(string wood, int logs, int branches, string size = "sm")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-{(branches > 0 ? "yes" : "no")}-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        if (branches > 0)
            stack.Attributes.SetInt(Trunks.BranchCountKey, branches);
        return stack;
    }

    /// <summary>Takes the first stack matching <paramref name="match"/> out of the player's hotbar
    /// or backpack (a trunk given back need not land in the hand).</summary>
    private static ItemStack? TakeFromPlayer(IPlayer player, System.Func<ItemStack, bool> match)
    {
        foreach (var inv in new[] { Vintagestory.API.Config.GlobalConstants.hotBarInvClassName, Vintagestory.API.Config.GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
                if (slot.Itemstack is { } stack && match(stack))
                {
                    slot.Itemstack = null;
                    slot.MarkDirty();
                    return stack;
                }
        return null;
    }

    // ---- Loading ----

    [AtlasScenario, ReadsBootLog]
    public void The_rosser_loads_cleanly_with_its_blocks_and_recipe()
    {
        Assert.True(RosserSystem.Applies(World.Api));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("rosser", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.NotNull(RosserMod.Rig);
        Assert.NotNull(RosserMod.Logging);
        foreach (var side in Sides.All)
        {
            Assert.IsType<BlockRosser>(BlockOf($"seraphhorizons:rosser-frame-{side.Code()}"));
            Assert.IsType<BlockRosserGhostPower>(BlockOf($"seraphhorizons:rosser-ghostpower-{side.Code()}"));
            Assert.IsType<BlockRosserGhostWater>(BlockOf($"seraphhorizons:rosser-ghostwater-{side.Code()}"));
        }
        Assert.IsType<BlockRosserGhost>(BlockOf("seraphhorizons:rosser-ghost"));
        // The parts it is built from exist in the pack, as do the bark spud's (for the heads' wear).
        foreach (var code in new[] { RosserParts.ShaftCode, RosserParts.RingCode, RosserParts.LeversCode, "game:hoop-iron", "game:rod-iron",
                                     "game:metalplate-iron", $"{Iw}:barkspudhead-copper", $"{Iw}:barkspudhead-steel", "game:chutesection-copper" })
            Assert.NotNull(ItemOf(code));
        Assert.Equal(4 * 2250, RosserMod.HeadCapacity("steel"));
        Assert.Equal(5, RosserMod.HeadTier("steel"));
        Assert.Equal(2, RosserMod.HeadTier("copper"));
        // the pack's Pipes and Power Expanded binds (the water is optional, so nothing else says so)
        Assert.True(PpexWater.Bound(World.Api), "Pipes and Power Expanded's pipe network did not bind");

        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:rosser-frame-north");
        Assert.NotNull(recipe.Output!.ResolvedItemStack);
        var ingredients = (recipe.ResolvedIngredients ?? []).OfType<CraftingRecipeIngredient>().ToList();
        // By quantity: with MachineSchematics on, the schematic takes a slot and two frames share one.
        Assert.Equal(4, ingredients.Where(i => i.Code!.ToString() == $"{Iw}:sawmill-frame-north").Sum(i => i.Quantity));
        Assert.Equal(32, ingredients.Where(i => i.Code!.ToString() == "game:supportbeam-*").Sum(i => i.Quantity));
        Assert.Equal(2, ingredients.Where(i => i.Code!.ToString() == "game:chutesection-copper").Sum(i => i.Quantity));
        Assert.Equal(32, MachineParts.Count(recipe, MachineParts.Nails));
        Assert.Contains(ingredients, i => i.IsTool && i.Code!.Path.StartsWith("hammer"));
    }

    // ---- Placing and breaking ----

    /// <summary>A player standing back from the target, looking along <paramref name="look"/>, places
    /// the frame item: the rosser extends away from them, every cell gets its ghost (the power and
    /// water ghosts' faces are side faces, and the cells beyond them are free and are not where a
    /// rack stands), and breaking it, here through a ghost, drops the frame and the fitted parts
    /// and clears every cell.</summary>
    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(Facings))]
    public async Task A_placed_rosser_stamps_its_ghosts_and_breaking_removes_them(string look, int index)
    {
        var pos = await RosserSky(40 * index, 230);
        var player = await Player("rosserplacer" + index);
        var d = BlockFacing.FromCode(look).Normali;
        player.Entity.Pos.SetPos(new Vec3d(pos.X + 0.5 - 3 * d.X, pos.Y, pos.Z + 0.5 - 3 * d.Z));
        var item = BlockOf("seraphhorizons:rosser-frame-north");
        var sel = new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        string failure = "";
        Assert.True(item.TryPlaceBlock(W, player, new ItemStack(item), sel, ref failure), $"not placed: {failure}");
        await World.Ticks(5);

        var rosser = Assert.IsType<BERosser>(W.BlockAccessor.GetBlockEntity(pos));
        Assert.True(Sides.TryParse(look, out var lookSide));
        Assert.Equal(Footprint.PlacedFacing(lookSide), rosser.Side);
        var rig = RosserRig;
        var cells = rosser.GhostCells().ToList();
        Assert.Equal(rig.Cells.Count - 1, cells.Count);
        string side = rosser.Side.Code();
        foreach (var (cell, kind) in cells)
        {
            Assert.Equal(kind switch
            {
                BERosser.CellKind.Power => $"seraphhorizons:rosser-ghostpower-{side}",
                BERosser.CellKind.Water => $"seraphhorizons:rosser-ghostwater-{side}",
                _ => "seraphhorizons:rosser-ghost",
            }, W.BlockAccessor.GetBlock(cell).Code.ToString());
            Assert.Equal(pos, Assert.IsType<BERosserGhost>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
        }
        // the infeed end is the far one: its rack cells are beyond every cell
        int Along(BlockPos p) => (p.X - pos.X) * d.X + (p.Z - pos.Z) * d.Z;
        Assert.Equal(0, cells.Min(c => Along(c.Pos)));
        int far = cells.Max(c => Along(c.Pos));
        Assert.All(rig.InfeedNeighbours(), n => Assert.Equal(far + 1, Along(rosser.CellPos(n))));
        Assert.All(rig.OutfeedNeighbours(), n => Assert.Equal(-1, Along(rosser.CellPos(n))));
        // power and water on side faces, opposite each other, the cells beyond them free
        var power = rosser.CellPos(rig.PowerCell);
        var water = rosser.CellPos(rig.WaterCell);
        var powerFace = Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(power)).PowerFace;
        var waterFace = Assert.IsType<BlockRosserGhostWater>(W.BlockAccessor.GetBlock(water)).WaterFace;
        Assert.Equal(Footprint.ToWorld(rig.PowerFace, rosser.Side).Code(), powerFace.Code);
        Assert.Equal(Footprint.ToWorld(rig.WaterFace, rosser.Side).Code(), waterFace.Code);
        Assert.Equal(0, powerFace.Normali.X * d.X + powerFace.Normali.Z * d.Z);
        Assert.Equal(powerFace.Opposite, waterFace);
        var racks = rig.InfeedNeighbours().Concat(rig.OutfeedNeighbours()).Select(rosser.CellPos).ToList();
        foreach (var beyond in new[] { power.AddCopy(powerFace), water.AddCopy(waterFace) })
        {
            Assert.Equal(0, W.BlockAccessor.GetBlock(beyond).Id);
            Assert.DoesNotContain(beyond, racks);
        }
        // a pipe on the water face is held there; no other face of a ghost holds anything
        var anyBlock = BlockOf("game:rock-granite");
        Assert.True(W.BlockAccessor.GetBlock(water).CanAttachBlockAt(W.BlockAccessor, anyBlock, water, waterFace));
        Assert.False(W.BlockAccessor.GetBlock(water).CanAttachBlockAt(W.BlockAccessor, anyBlock, water, waterFace.Opposite));
        Assert.False(W.BlockAccessor.GetBlock(power).CanAttachBlockAt(W.BlockAccessor, anyBlock, power, waterFace));
        // hollow cells are empty without a trunk; the others have boxes
        foreach (var cell in rig.Cells)
        {
            var boxes = rosser.CellBoxes(rosser.CellPos(cell.Pos));
            Assert.NotNull(boxes);
            if (cell.Hollow)
                Assert.Empty(boxes);
            else
                Assert.NotEmpty(boxes);
        }

        // some parts in, then broken through a ghost: everything comes back, the cells are clear
        Assert.Null(Click(player, pos, ItemOf(RosserParts.ShaftCode)));
        Assert.Null(Click(player, pos, ItemOf(RosserParts.RingCode, 4)));
        Assert.Null(Click(player, pos, ItemOf("game:rod-steel", 3)));
        KillItemsNear(pos, 20);
        var broken = cells[cells.Count / 2].Pos;
        W.BlockAccessor.GetBlock(broken).OnBlockBroken(W, broken, player);
        await World.Ticks(2);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
        var drops = ItemsNear(pos, 20);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:rosser-frame-north"));
        Assert.Equal(1, drops.GetValueOrDefault(RosserParts.ShaftCode));
        Assert.Equal(4, drops.GetValueOrDefault(RosserParts.RingCode));
        Assert.Equal(3, drops.GetValueOrDefault("game:rod-steel"));
        KillItemsNear(pos, 20);
    }

    // ---- Assembly ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_rosser_is_assembled_by_parts_as_the_rules_say()
    {
        var pos = await RosserSky(160, 230);
        var player = await Player("rosserbuilder");
        var rosser = await PlaceRosser(pos, "north");
        var ghost = rosser.GhostCells().Last().Pos;

        string info = Info(rosser, player);
        Assert.Contains("Unassembled", info);
        Assert.Contains("4 × Large gear sections", info);
        Assert.Contains(RosserHelp(pos, player), wi => wi.ActionLangCode == "seraphhorizons:blockhelp-rosser-fitpart");
        Assert.DoesNotContain(RosserHelp(pos, player), wi => wi.ActionLangCode == "seraphhorizons:blockhelp-rosser-fitheads");

        // the tyres and heads go on the ring: refused, kept in hand
        Assert.Equal(2, Click(player, pos, ItemOf("game:hoop-iron", 2))?.StackSize);
        Assert.Equal(4, Click(player, ghost, ItemOf($"{Iw}:barkspudhead-iron", 4))?.StackSize);
        // the ring takes four sections from a bigger stack
        Assert.Equal(2, Click(player, ghost, ItemOf(RosserParts.RingCode, 6))?.StackSize);
        Assert.True(rosser.Parts.Has(RosserStage.Ring));
        Assert.Contains(RosserHelp(pos, player), wi => wi.ActionLangCode == "seraphhorizons:blockhelp-rosser-fitheads");
        // hoops, rods and plates are iron work (IronWoodworkingMachines is on)
        Assert.Equal(2, Click(player, pos, ItemOf("game:hoop-tinbronze", 2))?.StackSize);
        Assert.False(rosser.Parts.Has(RosserStage.Tyres));
        Assert.Null(Click(player, pos, ItemOf("game:hoop-iron", 2)));
        // one click of rods fills both roll sets, infeed first, and leaves the rest in hand
        Assert.Null(Click(player, pos, ItemOf("game:rod-meteoriciron", 3)));
        Assert.True(rosser.Parts.Has(RosserStage.RollsIn));
        Assert.False(rosser.Parts.Has(RosserStage.RollsOut));
        Assert.Equal(2, Click(player, pos, ItemOf("game:rod-iron", 3))?.StackSize);
        Assert.True(rosser.Parts.Has(RosserStage.RollsOut));
        Assert.Equal(1, Click(player, pos, ItemOf("game:rod-iron", 1))?.StackSize);
        // the heads go on as four of one metal: three are refused
        Assert.Equal(3, Click(player, pos, ItemOf($"{Iw}:barkspudhead-copper", 3))?.StackSize);
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:barkspudhead-copper", 4)));
        Assert.Equal("copper", rosser.HeadMetal);
        Assert.Equal((1000, 1000), (rosser.Parts.HeadsLeft, rosser.Parts.HeadsCapacity));
        Assert.Null(Click(player, pos, ItemOf(RosserParts.ShaftCode)));
        Assert.Null(Click(player, pos, ItemOf("game:metalplate-steel", 2)));
        // something that is not a part: the click is not the rosser's
        Assert.NotNull(Click(player, ghost, ItemOf($"{Iw}:sawmillsash")));
        Assert.False(_handled);
        Assert.False(rosser.Complete);
        Assert.Contains("Sawmill feed levers", Info(rosser, player));
        Assert.Null(Click(player, ghost, ItemOf(RosserParts.LeversCode)));
        Assert.True(rosser.Complete);
        Assert.Equal("Rosser", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        info = Info(rosser, player);
        Assert.DoesNotContain("Unassembled", info);
        Assert.Contains("1000 / 1000", info);
        Assert.Contains("Empty", info);
        Assert.Contains(RosserHelp(pos, player), wi => wi.ActionLangCode == "seraphhorizons:blockhelp-rosser-loadtrunk");
        Assert.Equal(RosserMod.Config.Resistance, rosser.Power!.GetResistance());

        // Ctrl takes unworn heads back (into the empty hand): no longer complete
        var heads = Click(player, pos, null, ctrl: true);
        Assert.Equal(($"{Iw}:barkspudhead-copper", 4), (heads?.Collectible.Code.ToString(), heads?.StackSize ?? 0));
        Assert.False(rosser.Complete);
        Assert.Contains("No scraper heads", Info(rosser, player));
        // a trunk is refused without heads
        Assert.NotNull(Click(player, pos, RosserTrunk("oak", 4, 0)));
        Assert.Null(rosser.Trunk);

        // breaking returns exactly what went in, metals and all
        KillItemsNear(pos, 20);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(2);
        var drops = ItemsNear(pos, 20);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:rosser-frame-north"));
        Assert.Equal(2, drops.GetValueOrDefault("game:hoop-iron"));
        Assert.Equal(3, drops.GetValueOrDefault("game:rod-meteoriciron"));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-iron"));
        Assert.Equal(2, drops.GetValueOrDefault("game:metalplate-steel"));
        Assert.Equal(4, drops.GetValueOrDefault(RosserParts.RingCode));
        Assert.Equal(0, drops.GetValueOrDefault($"{Iw}:barkspudhead-copper"));
        KillItemsNear(pos, 20);
    }

    // ---- A trip ----

    /// <summary>A branched thin trunk goes on by hand, the shaft turns, and the trunk travels the
    /// whole trip: its boxes follow it, the limb breaker's sticks and the ring's bark land at the
    /// chute, the heads wear by its stored logs, and it waits on the outfeed bed debarked until
    /// Ctrl + right click takes it.</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_powered_rosser_debarks_a_thin_trunk_with_sticks_and_bark_at_the_chute()
    {
        var pos = await RosserSky(200, 230);
        var player = await Player("rosserhand");
        var rosser = await PlaceRosser(pos, "south");
        AssembleRosser(rosser, player);
        int capacity = rosser.Parts.HeadsCapacity;
        KillItemsNear(pos, 20);

        // loads without power, onto the infeed bed; its boxes are there and on the trunk a click is the rosser's
        Assert.Null(Click(player, pos, RosserTrunk("oak", 6, branches: 6)));
        Assert.Equal(RosserState.Waiting, rosser.State);
        Assert.Equal(TrunkClass.Thin, rosser.TrunkClass);
        Assert.True(rosser.Busy);
        Assert.Null(rosser.PeekFinished());
        var path = RosserRig.Path;
        var atStart = TrunkBox.CellBoxes(RosserRig.Cells, TrunkBox.BoundsOnPath(path, TrunkClass.Thin, 0));
        var atEnd = TrunkBox.CellBoxes(RosserRig.Cells, TrunkBox.BoundsOnPath(path, TrunkClass.Thin, (float)path.End(1)));
        var startOnly = atStart.Keys.First(c => !atEnd.ContainsKey(c));
        var endOnly = atEnd.Keys.First(c => !atStart.ContainsKey(c));
        int Boxes(Int3 cell) => rosser.CellBoxes(rosser.CellPos(cell))!.Length;
        var frameOf = RosserRig.Cells.ToDictionary(c => c.Pos, c => c.Hollow ? 0 : Math.Max(1, c.Boxes.Count));
        Assert.Equal(frameOf[startOnly] + 1, Boxes(startOnly));
        Assert.Equal(frameOf[endOnly], Boxes(endOnly));
        // a second trunk is refused while one is in
        Assert.NotNull(Click(player, pos, RosserTrunk("oak", 4, 0)));
        Assert.Contains("6 logs, 6 branches", Info(rosser, player));

        await PowerRosser(rosser);
        await World.Until(() => rosser.State == RosserState.Delivered, 90_000);

        var trunk = rosser.PeekFinished();
        Assert.NotNull(trunk);
        Assert.True(Trunks.IsDebarked(trunk));
        Assert.Equal("loggingmod:treetrunk-oak-sm-debarked-north", trunk!.Collectible.Code.ToString());
        Assert.False(trunk.Attributes.HasAttribute(Trunks.BranchCountKey));
        Assert.Equal(6, Trunks.StoredLogs(trunk, W));
        Assert.Equal(frameOf[startOnly], Boxes(startOnly));
        Assert.Equal(frameOf[endOnly] + 1, Boxes(endOnly));
        // one point per stored log
        Assert.Equal(capacity - 6, rosser.Parts.HeadsLeft);
        Assert.Contains("Debarked", Info(rosser, player));

        // three sticks (one per two branches) and oak's tan bark, three pieces a log, at the chute
        await World.Ticks(40);
        var items = ItemsNear(pos, 20);
        Assert.Equal(3, items.GetValueOrDefault("game:stick"));
        Assert.Equal(18, items.GetValueOrDefault($"{Iw}:bark-tan-green"));
        var chute = Footprint.ToWorld(RosserRig.Chute.Pos, rosser.Side);
        var landed = World.EntitiesIn(new Cuboidi(pos.X - 20, pos.Y - 2, pos.Z - 20, pos.X + 20, pos.Y + 4, pos.Z + 20))
            .OfType<EntityItem>().Where(e => e.Alive).ToList();
        Assert.All(landed, e => Assert.True(Math.Abs(e.Pos.X - (pos.X + chute.X)) < 2.5 && e.Pos.Z - pos.Z > 2,
            $"{e.Itemstack.Collectible.Code} landed at {e.Pos.XYZ}, not by the chute at {pos.X + chute.X}, {pos.Z + chute.Z}"));

        // Ctrl takes the debarked trunk; the outfeed is empty again and so are its boxes
        Click(player, pos, null, ctrl: true);
        await World.Ticks(2);
        // in the inventory, or dropped by the controller when there is no room for it
        var taken = TakeFromPlayer(player, s => Trunks.IsTrunk(s))
                    ?? World.EntitiesIn(new Cuboidi(pos.X - 3, pos.Y - 2, pos.Z - 3, pos.X + 3, pos.Y + 3, pos.Z + 3))
                        .OfType<EntityItem>().FirstOrDefault(e => e.Alive && Trunks.IsTrunk(e.Itemstack))?.Itemstack;
        Assert.Equal("loggingmod:treetrunk-oak-sm-debarked-north", taken?.Collectible.Code.ToString());
        Assert.Equal(RosserState.Empty, rosser.State);
        Assert.Equal(frameOf[endOnly], Boxes(endOnly));
        // a debarked trunk is refused at the infeed
        Assert.NotNull(Click(player, pos, taken));
        Assert.Null(rosser.Trunk);
        KillItemsNear(pos, 20);
        W.BlockAccessor.SetBlock(0, rosser.CellPos(RosserRig.PowerCell).AddCopy(
            Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(rosser.CellPos(RosserRig.PowerCell))).PowerFace));
    }

    // ---- Rows ----
    // Each scenario below has a floor of its own, at x 0 to 200 (by facing: 45 × index) on one of
    // these rows, z from spawn: 270 power, 315 racks, 360 breaking mid-trip, 405 boxes, 450 the mill
    // in line (wider floors, x 55 × index), 500 trips, water, wear and the mill's precedence, 620
    // unloading (a chunk column of its own), 700 to 900 felled trees. The rows above are 230 (the first scenarios here).

    // A second player who only stands by a machine: the server unloads chunk columns no player is
    // near, and a scenario longer than a minute would lose its machine. Not the shared player, who
    // stays where the other woodworking scenarios expect (their floors near spawn load no chunks).
    private static IPlayer? _keeper;
    private static object? _keeperWorld;

    /// <summary>Keeps the chunk columns around <paramref name="pos"/> loaded (the keeper player
    /// stands above it) until the next scenario moves the keeper.</summary>
    private async Task StandBy(IPlayer _, BlockPos pos)
    {
        if (_keeper == null || !ReferenceEquals(_keeperWorld, World.Api))
        {
            _keeper = (await World.JoinPlayer("rosserkeeper")).Player;
            _keeperWorld = World.Api;
        }
        _keeper.WorldData.CurrentGameMode = EnumGameMode.Creative;
        _keeper.Entity.TeleportTo(pos.ToVec3d().Add(0.5, 5, 0.5));
    }

    private BlockFacing RosserPowerFace(BERosser rosser) =>
        Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(rosser.CellPos(RosserRig.PowerCell))).PowerFace;

    private BlockPos RosserRotorPos(BERosser rosser) => rosser.CellPos(RosserRig.PowerCell).AddCopy(RosserPowerFace(rosser));

    /// <summary>Takes the rotor away; waits until the shaft has run down below the rosser's speed.</summary>
    private async Task UnpowerRosser(BERosser rosser)
    {
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        await World.Until(() => rosser.ShaftSpeed < rosser.MinSpeed, 20000);
    }

    private ItemStack RosserReady(BERosser rosser, IPlayer player, string heads = "steel")
    {
        AssembleRosser(rosser, player, heads);
        return ItemOf($"{Iw}:barkspudhead-{heads}");
    }

    /// <summary>Feeds the trunk on to travel <paramref name="travel"/> (server side) in steps of at
    /// most a quarter block, as the shaft gives them; what the steps gave, summed.</summary>
    private TripStep FeedTo(BERosser rosser, double travel)
    {
        // the end of the trip is reached, not left a hair short of it
        if (RosserMod.Pace is { } pace && rosser.Trip.HasTrunk && travel >= rosser.Trip.End(pace) - 1e-6)
            travel = rosser.Trip.End(pace) + 1;
        int sticks = 0, bark = 0;
        bool delivered = false;
        for (int guard = 0; guard < 10_000 && rosser.Trip.Travel < travel - 1e-6 && rosser.State is RosserState.Waiting or RosserState.Feeding; guard++)
        {
            double step = Math.Min(0.25, travel - rosser.Trip.Travel);
            var s = rosser.Feed((float)(step / rosser.Trip.Rate));
            sticks += s.Sticks;
            bark += s.BarkLogs;
            delivered |= s.Delivered;
        }
        return new TripStep(sticks, bark, delivered);
    }

    /// <summary>Travel at which the trunk's nose is at <paramref name="station"/> plus <paramref name="past"/>.</summary>
    private double TravelWithNoseAt(double station, double past = 0) => station + past - RosserRig.Path.Nose0;

    /// <summary>Every box of the rosser's cells, in world coordinates, as the game asks for them.</summary>
    private List<Cuboidd> RosserWorldBoxes(BERosser rosser, bool selection) =>
        rosser.GhostCells().Select(c => c.Pos).Prepend(rosser.Pos).SelectMany(c =>
        {
            var block = W.BlockAccessor.GetBlock(c);
            var boxes = selection ? block.GetSelectionBoxes(W.BlockAccessor, c) : block.GetCollisionBoxes(W.BlockAccessor, c);
            return (boxes ?? []).Select(b => new Cuboidd(b.X1 + c.X, b.Y1 + c.Y, b.Z1 + c.Z, b.X2 + c.X, b.Y2 + c.Y, b.Z2 + c.Z));
        }).ToList();

    /// <summary>The trunk's box in the world at <paramref name="travel"/> (quantised as the boxes are), from the rig's path turned to the facing.</summary>
    private (Vec3d Min, Vec3d Max) RosserTrunkWorldBox(BERosser rosser, TrunkClass k, double travel)
    {
        var (lo, hi) = TrunkBox.BoundsOnPath(RosserRig.Path, k, (float)RosserTrip.BoxTravel(travel));
        var a = Footprint.ToWorld(lo, rosser.Side);
        var b = Footprint.ToWorld(hi, rosser.Side);
        return (new Vec3d(rosser.Pos.X + Math.Min(a.X, b.X), rosser.Pos.Y + lo.Y, rosser.Pos.Z + Math.Min(a.Z, b.Z)),
                new Vec3d(rosser.Pos.X + Math.Max(a.X, b.X), rosser.Pos.Y + hi.Y, rosser.Pos.Z + Math.Max(a.Z, b.Z)));
    }

    /// <summary>The rosser's cell whose selection boxes hold <paramref name="p"/>, with the point relative to it.</summary>
    private (BlockPos Cell, Vec3d Hit) RosserCellHolding(BERosser rosser, Vec3d p)
    {
        foreach (var c in rosser.GhostCells().Select(c => c.Pos).Prepend(rosser.Pos))
            foreach (var b in W.BlockAccessor.GetBlock(c).GetSelectionBoxes(W.BlockAccessor, c) ?? [])
                if (p.X >= c.X + b.X1 - 1e-4 && p.X <= c.X + b.X2 + 1e-4 && p.Y >= c.Y + b.Y1 - 1e-4 && p.Y <= c.Y + b.Y2 + 1e-4
                    && p.Z >= c.Z + b.Z1 - 1e-4 && p.Z <= c.Z + b.Z2 + 1e-4)
                    return (c, new Vec3d(p.X - c.X, p.Y - c.Y, p.Z - c.Z));
        throw new Xunit.Sdk.XunitException($"no cell's box holds {p}");
    }

    /// <summary>The trunk stacks lying around <paramref name="around"/>, alive.</summary>
    private List<ItemStack> TrunksNear(BlockPos around, int radius = 20)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 40, around.Z - radius, around.X + radius, around.Y + 8, around.Z + radius);
        return World.EntitiesIn(box).OfType<EntityItem>().Where(e => e.Alive && Trunks.IsTrunk(e.Itemstack)).Select(e => e.Itemstack).ToList();
    }

    private BlockEntity? RackAt(BlockPos pos) => RosserMod.Logging!.FindRack(W.BlockAccessor, pos);

    private void PushOnRack(BlockPos pos, ItemStack trunk)
    {
        var rack = RackAt(pos) ?? throw new Xunit.Sdk.XunitException($"no rack at {pos}");
        Assert.True(RosserMod.Logging!.TrunkCount(rack) < 4, "the rack is full");
        RosserMod.Logging.PushTrunk(rack, trunk);
        rack.MarkDirty(true);
    }

    private int RackCount(BlockPos pos) => RackAt(pos) is { } rack ? RosserMod.Logging!.TrunkCount(rack) : -1;

    /// <summary>A rack set at <paramref name="pos"/> with its second block one further along <paramref name="outward"/>.</summary>
    private void SetRack(BlockPos pos, BlockFacing outward)
    {
        World.SetBlock($"loggingmod:trunkstorage-oak-empty-{outward.Code}", pos);
        Assert.IsType<BlockMultiblock>(W.BlockAccessor.GetBlock(pos.AddCopy(outward)));
    }

    // ---- Power ----

    /// <summary>On every facing: the power ghost has its one connector on the power face, and no
    /// other cell of the rosser is a power block; a turning axle against the outer faces of the
    /// other ghosts turns nothing; a vanilla axle on the power face does, and the shaft the renderer
    /// draws turns exactly as that axle is drawn (θ from <see cref="MillMotion.NativeShaftAngle"/>
    /// about native z against the axle's AngleRad × AxisSign about its world axis). The trunk feeds
    /// forward whichever way the network turns (the entry is rectified).</summary>
    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(Facings))]
    public async Task The_power_face_takes_an_axle_on_every_facing_and_the_shaft_turns_with_it(string side, int index)
    {
        var pos = await RosserSky(45 * index, 270);
        var player = await Player("rosserpower" + index);
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, side);
        RosserReady(rosser, player);
        var rig = RosserRig;
        var power = rosser.CellPos(rig.PowerCell);
        var ghost = Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(power));
        var face = ghost.PowerFace;
        foreach (var f in BlockFacing.ALLFACES)
            Assert.Equal(f == face, ghost.HasMechPowerConnectorAt(W, power, f, null!));
        foreach (var (cell, kind) in rosser.GhostCells())
            if (kind != BERosser.CellKind.Power)
                Assert.False(W.BlockAccessor.GetBlock(cell) is IMechanicalPowerBlock, $"{kind} cell {cell} is a power block");
        Assert.False(W.BlockAccessor.GetBlock(pos) is IMechanicalPowerBlock);

        string axis = face.Axis == EnumAxis.X ? "we" : "ns";
        // A turning axle against the outer side faces of other cells: the water ghost's (the far
        // side) and the plain ghost beside the power cell (the near side). Nothing turns.
        var water = rosser.CellPos(rig.WaterCell);
        var waterFace = BlockRosserGhostWater.WaterFaceFor(rig, side);
        var beside = rosser.CellPos(rig.PowerCell + new Int3(1, 0, 0));
        Assert.IsType<BlockRosserGhost>(W.BlockAccessor.GetBlock(beside));
        foreach (var (cell, f) in new[] { (water, waterFace), (beside, face) })
        {
            var axle = cell.AddCopy(f);
            Assert.Equal(0, W.BlockAccessor.GetBlock(axle).Id);
            World.SetBlock($"game:woodenaxle-{axis}", axle);
            World.SetBlock($"game:creativerotor-{f.Code}", axle.AddCopy(f));
        }
        await World.Ticks(40);
        Assert.True(rosser.ShaftSpeed < 0.001f, $"the shaft turns at {rosser.ShaftSpeed}");
        foreach (var (cell, f) in new[] { (water, waterFace), (beside, face) })
        {
            W.BlockAccessor.SetBlock(0, cell.AddCopy(f).AddCopy(f));
            W.BlockAccessor.SetBlock(0, cell.AddCopy(f));
        }

        // The power face: an axle, then the rotor; the trunk goes in and feeds.
        var axlePos = power.AddCopy(face);
        var rotorPos = axlePos.AddCopy(face);
        World.SetBlock($"game:woodenaxle-{axis}", axlePos);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => rosser.ShaftSpeed >= 0.5f, 15000);

        var shaft = rosser.Power!;
        var axleMp = W.BlockAccessor.GetBlockEntity(axlePos)!.GetBehavior<BEBehaviorMPBase>()!;
        Assert.Same(axleMp.Network, shaft.Network);
        int i = face.Axis == EnumAxis.X ? 0 : 2;
        Assert.Equal(-1, axleMp.AxisSign[i]);
        var u = Footprint.ToWorld(new Int3(0, 0, 1), rosser.Side);
        Assert.True(face.Axis == EnumAxis.X ? (u.X != 0) : (u.Z != 0));
        int along = u.X + u.Z;
        float lastAngle = shaft.AngleRad;
        double turned = 0;
        for (int t = 0; t < 20; t++)
        {
            await World.Ticks(1);
            float a = shaft.AngleRad, b = axleMp.AngleRad;
            Assert.Equal(0, MillMotion.WrappedDelta(b, a), 3);
            double shaftDrawn = MillMotion.NativeShaftAngle(rosser.Side, a, Axis.Z) * along;
            double axleDrawn = b * axleMp.AxisSign[i];
            Assert.Equal(0, MillMotion.WrappedDelta(axleDrawn, shaftDrawn), 3);
            turned += MillMotion.WrappedDelta(lastAngle, a);
            lastAngle = a;
        }
        output.WriteLine($"{side}: power face {face.Code}, axle {axis} AxisSign {string.Join(",", axleMp.AxisSign)}, AngleRad moved {turned:0.00} rad in 20 ticks, network speed {shaft.Network!.Speed:0.00}");
        Assert.True(Math.Abs(turned) > 0.1, "the shaft did not turn");

        // the trunk feeds forward with the network turning this way...
        Assert.Null(Click(player, pos, RosserTrunk("oak", 4, 0)));
        await World.Until(() => rosser.Trip.Travel > 0.5, 20000);
        double before = rosser.Trip.Travel;
        await World.Ticks(10);
        Assert.True(rosser.Trip.Travel > before);

        // ...and the other way: the same network turned backwards (as a source discovered from the
        // other end turns it), AngleRad now runs down, and T still goes up.
        var network = shaft.Network!;
        network.Speed = -Math.Abs(network.Speed);
        network.DirectionHasReversed = false;
        // hold it reversed for a second: the rotor's torque would bring it back round
        double reversed = 0, travelBefore = rosser.Trip.Travel;
        lastAngle = shaft.AngleRad;
        for (int t = 0; t < 10; t++)
        {
            network.Speed = -Math.Abs(network.Speed) - 0.5f;
            await World.Ticks(1);
            reversed += MillMotion.WrappedDelta(lastAngle, shaft.AngleRad);
            lastAngle = shaft.AngleRad;
        }
        output.WriteLine($"{side}: reversed, AngleRad moved {reversed:0.00} rad in 10 ticks; T {travelBefore:0.000} -> {rosser.Trip.Travel:0.000}");
        Assert.True(Math.Sign(reversed) == -Math.Sign(turned), $"the network did not turn the other way ({reversed:0.00} after {turned:0.00})");
        Assert.True(rosser.Trip.Travel > travelBefore, "the trunk did not feed with the shaft turning backwards");
        await World.Until(() => rosser.State == RosserState.Delivered, 60000);
        W.BlockAccessor.SetBlock(0, rotorPos);
        W.BlockAccessor.SetBlock(0, axlePos);
        KillItemsNear(pos, 20);
    }

    // ---- Water ----

    private (MethodInfo Across, object Manager) PpexNetworks()
    {
        var type = HarmonyLib.AccessTools.TypeByName("ExpandedLib.Networks.BlockNetworkModSystem")
                   ?? HarmonyLib.AccessTools.TypeByName("ExpandedLib.Blocks.Networks.BlockNetworkModSystem")
                   ?? throw new Xunit.Sdk.XunitException("no ppex network manager");
        var across = HarmonyLib.AccessTools.Method(type, "GetConnectedNetworkAcross", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
        return (across, World.Api.ModLoader.GetModSystem(type.FullName!)!);
    }

    private object? PipeNetworkAcross(BlockPos cell, BlockFacing face)
    {
        var (across, manager) = PpexNetworks();
        return across.Invoke(manager, [W.BlockAccessor, cell, face]);
    }

    private static object? PipeState(object network)
    {
        for (var t = network.GetType(); t != null; t = t.BaseType)
            if (t.GetProperty("State", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } p)
                return p.GetValue(network);
        return null;
    }

    private static T StateValue<T>(object state, string name) => (T)state.GetType().GetProperty(name)!.GetValue(state)!;

    /// <summary>A Pipes and Power Expanded pipe placed by a player against the water ghost's water
    /// face stays there (the ghost holds it, and its connector into the ghost is no leak); filled
    /// with water through its network, it fills the rosser's reservoir up to its size at the intake
    /// rate; the rosser is wet (synced) and spends a log's water per log: acacia's bark, whose tan
    /// chance (0.8) the wet multiplier lifts past 1, is all tan, and each log gives 4 or 5 pieces
    /// (3 × 1.5, the half rolled) where a dry rosser gives exactly 3. A pipe against any other
    /// ghost face is not held.</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_pipe_on_the_water_face_waters_the_drip_and_wet_bark_comes_more_and_better()
    {
        Assert.True(PpexWater.Bound(World.Api));
        var pos = await RosserSky(0, 500);
        var player = await Player("rosserwater");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "east");
        RosserReady(rosser, player);
        var rig = RosserRig;
        var water = rosser.CellPos(rig.WaterCell);
        var waterFace = Assert.IsType<BlockRosserGhostWater>(W.BlockAccessor.GetBlock(water)).WaterFace;
        string axis = waterFace.Axis == EnumAxis.X ? "we" : "ns";
        var pipe = BlockOf($"ppex:pipe-straight-{axis}-iron");

        // A pipe against a plain ghost's outer face (the cell above the water ghost's neighbour on
        // the top row) is not held: a neighbour change makes it break itself.
        var plain = rosser.CellPos(rig.PowerCell + new Int3(1, 0, 0));
        var plainFace = RosserPowerFace(rosser);
        var loose = plain.AddCopy(plainFace);
        string failure = "";
        player.Entity.Pos.SetPos(loose.AddCopy(plainFace, 3).ToVec3d().Add(0.5, 0, 0.5));
        Assert.True(pipe.TryPlaceBlock(W, player, new ItemStack(pipe), new BlockSelection { Position = loose.Copy(), Face = plainFace, HitPosition = new Vec3d(0.5, 0.5, 0.5) }, ref failure), failure);
        World.SetBlock("game:rock-granite", loose.UpCopy());
        W.BlockAccessor.SetBlock(0, loose.UpCopy());
        W.BlockAccessor.TriggerNeighbourBlockUpdate(loose.UpCopy());
        await World.Ticks(5);
        output.WriteLine($"pipe against a plain ghost's face: now {W.BlockAccessor.GetBlock(loose).Code}");
        Assert.Equal(0, W.BlockAccessor.GetBlock(loose).Id);
        KillItemsNear(pos, 20);

        // On the water face, as a player clicks it: held, and its connector points into the ghost.
        var at = water.AddCopy(waterFace);
        player.Entity.Pos.SetPos(at.AddCopy(waterFace, 3).ToVec3d().Add(0.5, 0, 0.5));
        failure = "";
        Assert.True(pipe.TryPlaceBlock(W, player, new ItemStack(pipe), new BlockSelection { Position = at.Copy(), Face = waterFace, HitPosition = new Vec3d(0.5, 0.5, 0.5) }, ref failure), failure);
        await World.Ticks(5);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(at);
        await World.Ticks(5);
        Assert.StartsWith("pipe-straight", W.BlockAccessor.GetBlock(at).Code.Path);
        // the far end capped, so the only connector left open is the one into the ghost
        World.SetBlock("game:rock-granite", at.AddCopy(waterFace));
        await World.Ticks(5);
        Assert.StartsWith("pipe-straight", W.BlockAccessor.GetBlock(at).Code.Path);
        var network = PipeNetworkAcross(water, waterFace) ?? throw new Xunit.Sdk.XunitException("no pipe network across the water face");
        var produce = network.GetType().GetMethods().Single(m => m.Name == "TryProduceLiquid" && m.GetParameters().Length == 4);
        Assert.True((bool)produce.Invoke(network, [30f, 20f, 1f, W.BlockAccessor])!, "the pipe took no water");
        var state = PipeState(network)!;
        output.WriteLine($"pipe network: {StateValue<string>(state, "MediumType")} {StateValue<float>(state, "Volume"):0.0} / {StateValue<float>(state, "MaxVolume"):0.0} L, leaking {StateValue<bool>(state, "IsLeaking")}");
        Assert.Equal("Water", StateValue<string>(state, "MediumType"));
        Assert.False(StateValue<bool>(state, "IsLeaking"));

        // the reservoir fills at the intake rate up to its size
        var config = RosserMod.Config;
        float volume = StateValue<float>(state, "Volume");
        await World.Until(() => rosser.Water.Litres >= config.ReservoirLitres - 1e-6, 10000);
        Assert.Equal(config.ReservoirLitres, rosser.Water.Litres, 3);
        Assert.True(rosser.Wet);
        var tree = new TreeAttribute();
        rosser.ToTreeAttributes(tree);
        Assert.True(tree.GetBool("wet"));
        Assert.Contains($"Drip: wet ({config.ReservoirLitres:0} / {config.ReservoirLitres:0} L)", Info(rosser, player));
        // the pipe gave what the reservoir took
        Assert.Equal(volume - config.ReservoirLitres, StateValue<float>(PipeState(PipeNetworkAcross(water, waterFace)!)!, "Volume"), 1);

        // Six wet logs: all tan, 4 or 5 pieces each. The water is spent per log (the pipe tops it up).
        KillItemsNear(pos, 20);
        Assert.Null(Click(player, pos, RosserTrunk("acacia", 6, 0)));
        var steps = FeedTo(rosser, 100);
        Assert.True(steps.Delivered);
        Assert.Equal(6, steps.BarkLogs);
        Assert.Equal(config.ReservoirLitres - 6 * config.WaterPerLog, rosser.Water.Litres, 3);
        await World.Ticks(40);
        var wet = ItemsNear(pos, 20);
        output.WriteLine("wet acacia, 6 logs: " + string.Join(", ", wet.Select(kv => $"{kv.Key} ×{kv.Value}")));
        Assert.Equal(0, wet.GetValueOrDefault($"{Iw}:bark-generic"));
        Assert.InRange(wet.GetValueOrDefault($"{Iw}:bark-tan-green"), 24, 30);
        Click(player, pos, null, ctrl: true);
        TakeFromPlayer(player, s => Trunks.IsTrunk(s));
        // the pipe topped up again, the reservoir fills again
        Assert.True((bool)produce.Invoke(PipeNetworkAcross(water, waterFace)!, [20f, 20f, 1f, W.BlockAccessor])!);
        await World.Until(() => rosser.Water.Litres >= config.ReservoirLitres - 1e-6, 5000);

        // The same on a dry rosser beside it: 3 pieces a log exactly; tan or generic.
        var dryPos = pos.AddCopy(8, 0, 0);
        var dry = await PlaceRosser(dryPos, "east");
        RosserReady(dry, player);
        Assert.False(dry.Wet);
        Assert.Contains("Drip: dry", Info(dry, player));
        KillItemsNear(pos, 20);
        Assert.Null(Click(player, dryPos, RosserTrunk("acacia", 10, 0)));
        Assert.Equal(10, FeedTo(dry, 100).BarkLogs);
        await World.Ticks(40);
        var dried = ItemsNear(pos, 20);
        output.WriteLine("dry acacia, 10 logs: " + string.Join(", ", dried.Select(kv => $"{kv.Key} ×{kv.Value}")));
        Assert.Equal(30, dried.GetValueOrDefault($"{Iw}:bark-tan-green") + dried.GetValueOrDefault($"{Iw}:bark-generic"));
        Assert.Equal(0d, dry.Water.Litres);
        KillItemsNear(pos, 20);

        // Taken off, the pipe leaves the reservoir as it is and the next logs spend it.
        W.BlockAccessor.SetBlock(0, at.AddCopy(waterFace));
        W.BlockAccessor.SetBlock(0, at);
        await World.Ticks(25);
        Assert.Equal(config.ReservoirLitres, rosser.Water.Litres, 3);
        KillItemsNear(pos, 20);
    }

    // ---- Full trips ----

    /// <summary>Thin and thick trunks, branchy and clean, go through a rosser on a creative rotor
    /// one after the other: each gives floor(branches × StickFraction) sticks and oak's three pieces
    /// of tan bark per stored log at the chute, comes out as the <c>-debarked-</c> trunk of its size
    /// with its logs and other contents and the debarked mark on its logs, at its class's rate, and
    /// wears the heads one point per stored log. The block info and help follow each state on the
    /// way: empty, waiting (stopped and turning), feeding, stalled, delivered.</summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Thin_and_thick_trunks_branchy_and_clean_go_through_with_their_sticks_bark_and_wear()
    {
        var pos = await RosserSky(45, 500);
        var player = await Player("rossertrips");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "west");
        RosserReady(rosser, player);
        var pace = RosserMod.Pace!;
        var config = RosserMod.Config;
        int tier = RosserMod.HeadTier("steel")!.Value;
        bool HelpHas(string action) => RosserHelp(pos, player).Any(wi => wi.ActionLangCode == "seraphhorizons:blockhelp-rosser-" + action);

        // empty and unpowered
        string info = Info(rosser, player);
        Assert.Contains("Empty: a trunk can go on", info);
        Assert.Contains("Scraper heads: Steel, 9000 / 9000", info);
        Assert.Contains("Feeds 2.05× as fast as with copper heads", info);
        Assert.Contains("Not turning fast enough", info);
        Assert.Contains("Drip: dry", info);
        Assert.True(HelpHas("loadtrunk"));
        Assert.True(HelpHas("removeheads"));
        Assert.False(HelpHas("taketrunk"));

        int left = rosser.Parts.HeadsLeft;
        var cases = new (string Size, int Logs, int Branches, TrunkClass Class)[]
        {
            ("sm", 6, 7, TrunkClass.Thin), ("md", 12, 0, TrunkClass.Thin), ("xl", 30, 13, TrunkClass.Thick), ("xxl", 40, 0, TrunkClass.Thick),
        };
        bool first = true;
        foreach (var (size, logs, branches, k) in cases)
        {
            KillItemsNear(pos, 20);
            var trunk = RosserTrunk("oak", logs, branches, size);
            trunk.Attributes.SetInt("resinLogCount", 2);
            Assert.Null(Click(player, pos, trunk));
            Assert.Equal(k, rosser.TrunkClass);
            Assert.Equal(pace.Rate((int)k, tier), rosser.Trip.Rate, 9);
            if (first)
            {
                // waiting, stopped: it can be taken back; then the shaft turns
                Assert.Equal(RosserState.Waiting, rosser.State);
                Assert.Contains("goes in when the shaft turns", Info(rosser, player));
                Assert.Contains($"{logs} logs, {branches} branches", Info(rosser, player));
                Assert.True(HelpHas("taketrunk"));
                Assert.False(HelpHas("loadtrunk"));
                await PowerRosser(rosser);
                await World.Until(() => rosser.State == RosserState.Feeding && rosser.Trip.Travel > 1, 30000);
                Assert.Contains("Debarking: ", Info(rosser, player));
                Assert.Contains("Drive speed: ", Info(rosser, player));
                Assert.False(HelpHas("taketrunk"));
                // the rotor taken away: stalled, T holds, the trunk stays in the rolls
                await UnpowerRosser(rosser);
                double held = rosser.Trip.Travel;
                await World.Ticks(20);
                Assert.Equal(held, rosser.Trip.Travel);
                Assert.Equal(RosserState.Feeding, rosser.State);
                Assert.Contains("Stopped with the trunk in the rolls", Info(rosser, player));
                Assert.Null(Click(player, pos, null, ctrl: true));
                Assert.NotNull(rosser.Trunk);
                await PowerRosser(rosser);
                first = false;
            }
            var started = DateTime.UtcNow;
            while (rosser.State != RosserState.Delivered)
            {
                output.WriteLine($"{DateTime.UtcNow - started:mm\\:ss} {size}: {rosser.State} T {rosser.Trip.Travel:0.000} / {RosserMod.Pace!.TripLength((int)k):0.000}, rate {rosser.Trip.Rate:0.0000}, speed {rosser.ShaftSpeed:0.00}");
                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(90), "not delivered");
                await World.Ticks(20);
            }

            var done = rosser.PeekFinished()!;
            Assert.Equal($"loggingmod:treetrunk-oak-{size}-debarked-north", done.Collectible.Code.ToString());
            Assert.Equal(logs, Trunks.StoredLogs(done, W));
            Assert.True(DebarkedTrunks.IsMarked(Trunks.StoredLogStack(done, W)));
            Assert.Equal(2, done.Attributes.GetInt("resinLogCount"));
            Assert.False(done.Attributes.HasAttribute(Trunks.BranchCountKey));
            left -= logs;
            Assert.Equal(left, rosser.Parts.HeadsLeft);
            info = Info(rosser, player);
            Assert.Contains("Debarked: the trunk waits on the outfeed bed", info);
            Assert.Contains("Take it with Ctrl + right click", info);
            Assert.Contains($"Scraper heads: Steel, {left} / 9000", info);
            Assert.True(HelpHas("taketrunk"));
            await World.Ticks(40);
            var items = ItemsNear(pos, 20);
            output.WriteLine($"oak {size}, {logs} logs, {branches} branches: " + string.Join(", ", items.Select(kv => $"{kv.Key} ×{kv.Value}")));
            Assert.Equal((int)Math.Floor(branches * config.StickFraction), items.GetValueOrDefault("game:stick"));
            Assert.Equal(3 * logs, items.GetValueOrDefault($"{Iw}:bark-tan-green"));
            Assert.All(items.Keys, key => Assert.Contains(key, new[] { "game:stick", $"{Iw}:bark-tan-green" }));
            Click(player, pos, null, ctrl: true);
            Assert.NotNull(TakeFromPlayer(player, s => Trunks.IsTrunk(s)));
            Assert.Equal(RosserState.Empty, rosser.State);
        }
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        KillItemsNear(pos, 20);
    }

    /// <summary>Heads close to worn out: the trunk that spends them is still delivered and stays
    /// on the bed; the heads are gone (not returned on breaking), the rosser is incomplete, refuses
    /// the next trunk, offers the heads in its help, and works again with four new ones.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Heads_spent_mid_use_stop_the_rosser_until_new_ones_go_on()
    {
        var pos = await RosserSky(90, 500);
        var player = await Player("rosserworn");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "south");
        RosserReady(rosser, player, "copper");
        // as saved with five points left
        var tree = new TreeAttribute();
        rosser.ToTreeAttributes(tree);
        tree.SetInt("headsLeft", 5);
        rosser.FromTreeAttributes(tree, W);
        Assert.Equal((5, 1000), (rosser.Parts.HeadsLeft, rosser.Parts.HeadsCapacity));
        Assert.True(rosser.Complete);

        Assert.Null(Click(player, pos, RosserTrunk("oak", 6, 0)));
        // copper heads feed at copper's pace (steel's is checked on the trips)
        Assert.Equal(RosserMod.Pace!.Rate(1, RosserMod.HeadTier("copper")), rosser.Trip.Rate, 9);
        Assert.True(FeedTo(rosser, 100).Delivered);
        Assert.Equal(RosserState.Delivered, rosser.State);
        Assert.NotNull(rosser.PeekFinished());
        Assert.True(Trunks.IsDebarked(rosser.Trunk));
        Assert.False(rosser.Complete);
        Assert.Null(rosser.HeadMetal);
        Assert.False(rosser.Fitted("heads"));
        Assert.Contains("No scraper heads", Info(rosser, player));
        Assert.Contains(RosserHelp(pos, player), wi => wi.ActionLangCode == "seraphhorizons:blockhelp-rosser-fitheads");
        // the trunk still comes off; the next one is refused
        Click(player, pos, null, ctrl: true);
        Assert.NotNull(TakeFromPlayer(player, s => Trunks.IsDebarked(s)));
        Assert.NotNull(Click(player, pos, RosserTrunk("oak", 4, 0)));
        Assert.Null(rosser.Trunk);
        // no heads come back on breaking now; with four new ones it works again
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:barkspudhead-iron", 4)));
        Assert.True(rosser.Complete);
        Assert.Equal("iron", rosser.HeadMetal);
        Assert.Null(Click(player, pos, RosserTrunk("oak", 4, 0)));
        Assert.Equal(RosserState.Waiting, rosser.State);
        Click(player, pos, null, ctrl: true);
        TakeFromPlayer(player, s => Trunks.IsTrunk(s));
        KillItemsNear(pos, 20);
    }

    // ---- Racks ----

    /// <summary>On every facing: a rack placed by a player (Logging Expanded's own placement,
    /// looking each of the four ways) against each of the three ground cells beyond the infeed end
    /// is found; the rosser says what its top trunk offers (a debarked one or one with no logs waits
    /// there), and once running takes the next trunk from it. A delivered trunk goes onto a rack with
    /// room at the outfeed end; with that rack full (4) it waits on the bed, nothing lost, and goes
    /// on as soon as there is room.</summary>
    [AtlasTheory(TimeoutMs = 300_000), MemberData(nameof(Facings))]
    public async Task Racks_feed_the_infeed_end_and_take_from_the_outfeed_end(string side, int index)
    {
        var pos = await RosserSky(45 * index, 315, reach: 21);
        var player = await Player("rosserracks" + index);
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, side);
        RosserReady(rosser, player);
        var rig = RosserRig;
        var infeed = BlockFacing.FromCode(Footprint.ToWorld(rig.InfeedSide, rosser.Side).Code());
        var outfeed = infeed.Opposite;
        var touching = rig.InfeedNeighbours().Select(rosser.CellPos).ToList();
        Assert.Equal(3, touching.Count);
        var across = infeed.GetCW();
        var rackBlock = BlockOf("loggingmod:trunkstorage-oak-empty-north");
        void ClearAt(IEnumerable<BlockPos> cells, BlockFacing away)
        {
            foreach (var t in cells)
                for (int o = 0; o <= 2; o++)
                    for (int s = -2; s <= 2; s++)
                        W.BlockAccessor.SetBlock(0, t.AddCopy(away, o).AddCopy(across, s));
        }
        Assert.Equal(RosserRackState.None, rosser.CheckRack(out _));

        var results = new List<string>();
        foreach (var look in BlockFacing.HORIZONTALS)
            foreach (var cell in touching)
            {
                ClearAt(touching, infeed);
                var at = new Vec3d(cell.X + 0.5 - 3 * look.Normali.X, cell.Y, cell.Z + 0.5 - 3 * look.Normali.Z);
                player.Entity.Pos.SetPos(at);
                player.Entity.Pos.Yaw = (float)Math.Atan2(-look.Normali.X, -look.Normali.Z);
                var sel = new BlockSelection { Position = cell.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.0, 0.5) };
                string failure = "";
                bool placed = rackBlock.TryPlaceBlock(W, player, new ItemStack(rackBlock), sel, ref failure);
                string where = "against the end";
                if (!placed)
                {
                    sel.Position = cell.AddCopy(infeed);
                    at.Add(infeed.Normali.X, 0, infeed.Normali.Z);
                    player.Entity.Pos.SetPos(at);
                    failure = "";
                    placed = rackBlock.TryPlaceBlock(W, player, new ItemStack(rackBlock), sel, ref failure);
                    where = "one block out";
                }
                await World.Ticks(2);
                var state = rosser.CheckRack(out _);
                results.Add($"look {look.Code}, cell {cell}: placed {placed} {where} ({failure}) as {W.BlockAccessor.GetBlock(sel.Position).Code}: {state}");
                Assert.True(placed, results[^1]);
                Assert.Equal(RosserRackState.Empty, state);
            }
        foreach (var line in results)
            output.WriteLine(line);
        ClearAt(touching, infeed);

        // the racks for the run: at the infeed (a cell by facing) and the outfeed (another)
        var inRack = touching[index % 3];
        SetRack(inRack, infeed);
        var outRack = rig.OutfeedNeighbours().Select(rosser.CellPos).ToList()[(index + 1) % 3];
        SetRack(outRack, outfeed);
        await World.Ticks(2);
        Assert.Equal(RosserOutfeedState.Rack, rosser.CheckOutfeed(out _));

        // what the infeed rack offers, unpowered (nothing is pulled)
        PushOnRack(inRack, RosserTrunk("oak", 6, 4));
        await World.Until(() => rosser.RackState == RosserRackState.Ready, 5000);
        Assert.Contains("Rack: its top trunk goes in", Info(rosser, player));
        PushOnRack(inRack, DebarkedTrunk("oak", 4));
        await World.Until(() => rosser.RackState == RosserRackState.Debarked, 5000);
        Assert.Contains("Rack: its top trunk is already debarked; take it off", Info(rosser, player));
        Assert.True(Trunks.IsDebarked(RosserMod.Logging!.PopTrunk(RackAt(inRack)!)));
        RackAt(inRack)!.MarkDirty(true);
        PushOnRack(inRack, RosserTrunk("oak", 0, 0));
        await World.Until(() => rosser.RackState == RosserRackState.NoLogs, 5000);
        Assert.Contains("Rack: its top trunk holds no logs", Info(rosser, player));
        await PowerRosser(rosser);
        // a trunk with no logs waits there, running or not
        await World.Ticks(30);
        Assert.Equal(RosserState.Empty, rosser.State);
        Assert.Equal(2, RackCount(inRack));
        Assert.Equal(0, Trunks.StoredLogs(RosserMod.Logging.PopTrunk(RackAt(inRack)!)!, W));
        RackAt(inRack)!.MarkDirty(true);

        // pulled, debarked, pushed into the outfeed rack
        await World.Until(() => rosser.State != RosserState.Empty, 5000);
        Assert.Equal(0, RackCount(inRack));
        Assert.Equal("empty", W.BlockAccessor.GetBlock(inRack).Variant["fill"]);
        await World.Until(() => RackCount(outRack) == 1, 60000);
        Assert.Equal(RosserState.Empty, rosser.State);
        var pushed = RosserMod.Logging.PeekTrunk(RackAt(outRack)!)!;
        Assert.Equal("loggingmod:treetrunk-oak-sm-debarked-north", pushed.Collectible.Code.ToString());
        Assert.Equal(6, Trunks.StoredLogs(pushed, W));

        // the outfeed rack full: the next trunk waits on the bed
        for (int n = 0; n < 3; n++)
            PushOnRack(outRack, DebarkedTrunk("oak", 4));
        Assert.Equal(4, RackCount(outRack));
        PushOnRack(inRack, RosserTrunk("birch", 5, 0));
        await World.Until(() => rosser.State == RosserState.Delivered, 60000);
        await World.Ticks(50);
        Assert.Equal(RosserState.Delivered, rosser.State);
        Assert.Equal(RosserOutfeedState.RackFull, rosser.OutfeedState);
        Assert.Contains("The rack at the near end is full", Info(rosser, player));
        Assert.Equal(4, RackCount(outRack));
        Assert.Equal("loggingmod:treetrunk-birch-sm-debarked-north", rosser.PeekFinished()?.Collectible.Code.ToString());
        // room again: it goes on, and nothing was lost on the way
        var taken = RosserMod.Logging.PopTrunk(RackAt(outRack)!);
        RackAt(outRack)!.MarkDirty(true);
        Assert.NotNull(taken);
        await World.Until(() => rosser.State == RosserState.Empty, 5000);
        Assert.Equal(4, RackCount(outRack));
        Assert.Equal("loggingmod:treetrunk-birch-sm-debarked-north", RosserMod.Logging.PeekTrunk(RackAt(outRack)!)?.Collectible.Code.ToString());
        await World.Until(() => rosser.RackState == RosserRackState.Empty, 3000);
        Assert.Contains("Rack: empty", Info(rosser, player));
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        KillItemsNear(pos, 20);
    }

    /// <summary>With <c>AutoPullFromRack</c> off the rosser takes nothing from the infeed rack and
    /// says so; with <c>AutoPushToRack</c> off a delivered trunk stays on the bed beside a rack with
    /// room, and the info says it waits for a hand (not that the rack takes it). Switched back on,
    /// both go ahead.</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task With_the_rack_switches_off_the_rosser_leaves_the_racks_alone_and_says_so()
    {
        var pos = await RosserSky(180, 500, reach: 21);
        var player = await Player("rosserswitches");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "east");
        RosserReady(rosser, player);
        var rig = RosserRig;
        var infeed = BlockFacing.FromCode(Footprint.ToWorld(rig.InfeedSide, rosser.Side).Code());
        var inRack = rosser.CellPos(rig.InfeedNeighbours().First());
        var outRack = rosser.CellPos(rig.OutfeedNeighbours().Last());
        SetRack(inRack, infeed);
        SetRack(outRack, infeed.Opposite);
        var config = RosserMod.Config;
        try
        {
            config.AutoPullFromRack = false;
            config.AutoPushToRack = false;
            PushOnRack(inRack, RosserTrunk("oak", 4, 0));
            await PowerRosser(rosser);
            await World.Until(() => rosser.RackState == RosserRackState.Off, 3000);
            Assert.Contains("Rack: not taking trunks from a rack (AutoPullFromRack is off)", Info(rosser, player));
            await World.Ticks(40);
            Assert.Equal(RosserState.Empty, rosser.State);
            Assert.Equal(1, RackCount(inRack));

            // by hand instead: delivered, it stays with room on the rack
            Assert.Null(Click(player, pos, RosserTrunk("birch", 4, 0)));
            await World.Until(() => rosser.State == RosserState.Delivered, 60000);
            await World.Ticks(40);
            Assert.Equal(RosserState.Delivered, rosser.State);
            Assert.Equal(0, RackCount(outRack));
            Assert.Equal(RosserOutfeedState.Off, rosser.OutfeedState);
            string info = Info(rosser, player);
            Assert.Contains("It waits for a hand: not pushing trunks onto a rack (AutoPushToRack is off)", info);
            Assert.DoesNotContain("It goes onto the rack", info);

            config.AutoPushToRack = true;
            await World.Until(() => RackCount(outRack) == 1, 5000);
            Assert.Equal(RosserState.Empty, rosser.State);
            await World.Ticks(40);
            Assert.Equal(1, RackCount(inRack));
            config.AutoPullFromRack = true;
            await World.Until(() => RackCount(inRack) == 0, 5000);
            Assert.NotEqual(RosserState.Empty, rosser.State);
            await World.Until(() => RackCount(outRack) == 2, 60000);
        }
        finally
        {
            config.AutoPullFromRack = true;
            config.AutoPushToRack = true;
            W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
            KillItemsNear(pos, 21);
        }
    }

    /// <summary>In creative mode, Ctrl + right click fits the next stage, in <see cref="RosserStage"/>
    /// order, with nothing taken from the player; the load on the shaft is a token until the rosser
    /// is complete. Complete, the same click takes the unworn heads back, as in survival.</summary>
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task In_creative_a_Ctrl_click_fits_the_rossers_next_stage()
    {
        var pos = await RosserSky(225, 500);
        var player = await Player("rossercreative");
        var rosser = await PlaceRosser(pos, "north");
        Assert.True(rosser.CreativeShortcut);
        Assert.Equal(BEBehaviorMillMP.IncompleteResistance, rosser.Power!.GetResistance());
        var stages = Enum.GetValues<RosserStage>();
        for (int i = 0; i < stages.Length; i++)
        {
            Assert.False(rosser.Complete);
            Assert.Null(Click(player, pos, null, ctrl: true, creative: true));
            Assert.True(_handled);
            for (int j = 0; j < stages.Length; j++)
                Assert.True(rosser.Parts.Has(stages[j]) == j <= i, $"after {i + 1} clicks, {stages[j]} fitted: {rosser.Parts.Has(stages[j])}");
        }
        Assert.True(rosser.Complete);
        Assert.Equal(RosserMod.Config.Resistance, rosser.Power!.GetResistance());
        Assert.Equal(RosserMod.HeadCapacity(rosser.HeadMetal!), rosser.Parts.HeadsCapacity);
        var heads = Click(player, pos, null, ctrl: true, creative: true);
        Assert.Equal($"{Iw}:barkspudhead-steel", heads?.Collectible.Code.ToString());
        Assert.Equal(4, heads?.StackSize);
        Assert.False(rosser.Complete);
        KillItemsNear(pos, 20);
    }

    /// <summary>Sets the heads' points left, as a save would hold them.</summary>
    private void SetHeadsLeft(BERosser rosser, int left)
    {
        var tree = new TreeAttribute();
        rosser.ToTreeAttributes(tree);
        tree.SetInt("headsLeft", left);
        rosser.FromTreeAttributes(tree, W);
        Assert.Equal(left, rosser.Parts.HeadsLeft);
    }

    /// <summary>Only a new trip needs heads: the trunk whose trip spends them still leaves, onto a
    /// rack with room at the outfeed end while the shaft turns, or onto a bucking mill in line;
    /// the next trunk on the infeed rack waits for new heads.</summary>
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_trunk_that_spends_the_heads_still_leaves_by_rack_or_mill()
    {
        var origin = await RosserSky(0, 555, reach: 25);
        var player = await Player("rosserspentout");
        await StandBy(player, origin);

        // onto a rack: a rosser facing south (it runs along x, 5 wide in z), 8 north of the middle
        var pos = origin.AddCopy(5, 0, -8);
        var rosser = await PlaceRosser(pos, "south");
        RosserReady(rosser, player, "copper");
        var rig = RosserRig;
        var infeed = BlockFacing.FromCode(Footprint.ToWorld(rig.InfeedSide, rosser.Side).Code());
        var inRack = rosser.CellPos(rig.InfeedNeighbours().First());
        var outRack = rosser.CellPos(rig.OutfeedNeighbours().First());
        SetRack(inRack, infeed);
        SetRack(outRack, infeed.Opposite);
        await PowerRosser(rosser);
        SetHeadsLeft(rosser, 5);
        Assert.Null(Click(player, pos, RosserTrunk("oak", 6, 0)));
        // the next trunk waits on the infeed rack
        PushOnRack(inRack, RosserTrunk("oak", 4, 0));
        await World.Until(() => RackCount(outRack) == 1, 60000);
        Assert.False(rosser.Complete);
        Assert.Null(rosser.HeadMetal);
        Assert.Equal(RosserState.Empty, rosser.State);
        Assert.Equal("loggingmod:treetrunk-oak-sm-debarked-north", RosserMod.Logging!.PeekTrunk(RackAt(outRack)!)?.Collectible.Code.ToString());
        await World.Ticks(40);
        Assert.Equal(1, RackCount(inRack));
        Assert.Equal(RosserState.Empty, rosser.State);
        Assert.Contains("No scraper heads", Info(rosser, player));
        // new heads: the waiting trunk goes in
        Assert.Null(Click(player, pos, ItemOf($"{Iw}:barkspudhead-copper", 4)));
        await World.Until(() => RackCount(inRack) == 0, 5000);
        await World.Until(() => RackCount(outRack) == 2, 60000);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));

        // onto a mill in line: a second rosser 8 south of the middle, a mill at its near end
        var linePos = origin.AddCopy(-3, 0, 8);
        var mill = await PlaceMill(linePos.AddCopy(-Footprint.ToWorld(new Int3(-6, 0, 0), Side.South).X, 0, -Footprint.ToWorld(new Int3(-6, 0, 0), Side.South).Z), "south");
        var lined = await PlaceRosser(mill.CellPos(new Int3(-6, 0, 0)), "south");
        Assert.True(lined.MillInLine());
        Assemble(mill, player);
        RosserReady(lined, player, "copper");
        await Power(mill, full: true);
        await PowerRosser(lined);
        SetHeadsLeft(lined, 5);
        Assert.Null(Click(player, lined.Pos, RosserTrunk("oak", 6, 0)));
        await World.Until(() => mill.Trunk != null, 60000);
        Assert.False(lined.Complete);
        Assert.Equal(RosserState.Empty, lined.State);
        Assert.True(Trunks.IsDebarked(mill.Trunk));
        await World.Until(() => mill.Trunk == null, 30000);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(lined));
        W.BlockAccessor.SetBlock(0, RotorPos(mill));
        KillItemsNear(origin, 25);
    }

    // ---- Breaking mid-trip ----

    public static TheoryData<string, int> BreakStages() => new() { { "AsLoaded", 0 }, { "Debranched", 1 }, { "Debarked", 2 }, { "Delivered", 3 } };

    /// <summary>A branchy thick trunk fed to a point of its trip (nose short of the limb breaker;
    /// between the breaker and the ring; past the ring; delivered), then the rosser broken through
    /// a ghost: the sticks and bark that dropped are what the trip counted, and the drops are the
    /// frame, every part (the heads only while unworn) and the trunk as Core's
    /// <see cref="RosserTrip.Broken"/> says: as loaded, debranched or debarked. Run through again,
    /// it gives nothing a second time (a debranched trunk no sticks; a debarked one is refused).</summary>
    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(BreakStages))]
    public async Task Breaking_mid_trip_gives_the_trunk_as_far_as_it_got_and_nothing_twice(string stage, int index)
    {
        var pos = await RosserSky(45 * index, 360);
        var player = await Player("rosserbreak" + index);
        await StandBy(player, pos);
        string side = Sides.All[index].Code();
        var rosser = await PlaceRosser(pos, side);
        RosserReady(rosser, player);
        var pace = RosserMod.Pace!;
        const int logs = 30, branches = 14;
        int sticks = (int)Math.Floor(branches * RosserMod.Config.StickFraction);
        Assert.Null(Click(player, pos, RosserTrunk("oak", logs, branches, "xl")));
        KillItemsNear(pos, 20);
        double travel = stage switch
        {
            "AsLoaded" => TravelWithNoseAt(pace.Breaker, -0.25),
            "Debranched" => TravelWithNoseAt(pace.Ring, -0.05),
            "Debarked" => TravelWithNoseAt(pace.Ring, 2),
            _ => 100,
        };
        var steps = FeedTo(rosser, travel);
        var trip = rosser.Trip;
        var expected = stage == "Delivered" ? RosserBrokenTrunk.Debarked : Enum.Parse<RosserBrokenTrunk>(stage);
        Assert.Equal(expected, trip.Broken(pace));
        Assert.Equal(stage == "Delivered", trip.State(pace) == RosserState.Delivered);
        Assert.Equal((steps.Sticks, steps.BarkLogs), (trip.SticksDone, trip.BarkDone));
        if (stage == "Debranched")
            Assert.InRange(trip.SticksDone, 1, sticks - 1);
        await World.Ticks(30);
        var dropped = ItemsNear(pos, 20);
        output.WriteLine($"{stage} at T {trip.Travel:0.000}: {trip.SticksDone} sticks, {trip.BarkDone} logs' bark; " + string.Join(", ", dropped.Select(kv => $"{kv.Key} ×{kv.Value}")));
        Assert.Equal(trip.SticksDone, dropped.GetValueOrDefault("game:stick"));
        Assert.Equal(3 * trip.BarkDone, dropped.GetValueOrDefault($"{Iw}:bark-tan-green"));
        KillItemsNear(pos, 20);

        var cells = rosser.GhostCells().ToList();
        var broken = cells.First(c => c.Kind == BERosser.CellKind.Plain).Pos;
        W.BlockAccessor.GetBlock(broken).OnBlockBroken(W, broken, player);
        await World.Ticks(2);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
        var drops = ItemsNear(pos, 20);
        output.WriteLine($"{stage}, broken: " + string.Join(", ", drops.Select(kv => $"{kv.Key} ×{kv.Value}")));
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:rosser-frame-north"));
        Assert.Equal(1, drops.GetValueOrDefault(RosserParts.ShaftCode));
        Assert.Equal(4, drops.GetValueOrDefault(RosserParts.RingCode));
        Assert.Equal(2, drops.GetValueOrDefault("game:hoop-iron"));
        Assert.Equal(4, drops.GetValueOrDefault("game:rod-iron"));
        Assert.Equal(2, drops.GetValueOrDefault("game:metalplate-iron"));
        Assert.Equal(1, drops.GetValueOrDefault(RosserParts.LeversCode));
        Assert.Equal(stage == "Delivered" ? 0 : 4, drops.GetValueOrDefault($"{Iw}:barkspudhead-steel"));
        Assert.Equal(0, drops.GetValueOrDefault("game:stick"));
        Assert.Equal(0, drops.GetValueOrDefault($"{Iw}:bark-tan-green"));
        var trunk = Assert.Single(TrunksNear(pos));
        Assert.Equal(logs, Trunks.StoredLogs(trunk, W));
        Assert.Equal(expected switch
        {
            RosserBrokenTrunk.AsLoaded => "loggingmod:treetrunk-oak-xl-yes-north",
            RosserBrokenTrunk.Debranched => "loggingmod:treetrunk-oak-xl-no-north",
            _ => "loggingmod:treetrunk-oak-xl-debarked-north",
        }, trunk.Collectible.Code.ToString());
        Assert.Equal(expected == RosserBrokenTrunk.AsLoaded ? branches : 0, trunk.Attributes.GetInt(Trunks.BranchCountKey));
        Assert.Equal(expected == RosserBrokenTrunk.Debarked, DebarkedTrunks.IsMarked(Trunks.StoredLogStack(trunk, W)));
        trunk = trunk.Clone();
        KillItemsNear(pos, 20);

        // through a rosser again
        var again = await PlaceRosser(pos, side);
        RosserReady(again, player);
        if (expected == RosserBrokenTrunk.Debarked)
        {
            Assert.NotNull(Click(player, pos, trunk));
            Assert.Null(again.Trunk);
        }
        else
        {
            Assert.Null(Click(player, pos, trunk));
            var rest = FeedTo(again, 100);
            Assert.Equal(expected == RosserBrokenTrunk.AsLoaded ? sticks : 0, rest.Sticks);
            Assert.Equal(logs, rest.BarkLogs);
        }
        KillItemsNear(pos, 20);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, null);
        KillItemsNear(pos, 20);
    }

    // ---- Boxes ----

    /// <summary>On every facing, a thin and a thick trunk at several points of the trip: the boxes
    /// the rosser's cells add are the trunk's box at that travel (quantised to 1/16 block) and no
    /// more, they fill it, selection and collision agree, hollow cells hold only the trunk's part,
    /// and a click on the trunk, a block in hand, is the rosser's (nothing is placed); Ctrl on the
    /// delivered trunk takes it and the boxes are the frame's again.</summary>
    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(Facings))]
    public async Task The_trunks_boxes_follow_it_on_every_facing_and_a_click_on_it_is_the_rossers(string side, int index)
    {
        var pos = await RosserSky(45 * index, 405);
        var player = await Player("rosserboxes" + index);
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, side);
        RosserReady(rosser, player);
        var frameOnly = RosserWorldBoxes(rosser, selection: false);
        // Empty, the collision boxes are the selection boxes and the lids.
        var lids = Added(frameOnly, RosserWorldBoxes(rosser, selection: true));
        Assert.Equal(RosserRig.Cells.Count(c => c.Lid != null), lids.Count);
        Assert.All(lids, b => Assert.Equal(RigCell.LidThickness, b.Y2 - b.Y1, 4));
        var hollow = RosserRig.Cells.Where(c => c.Hollow).Select(c => rosser.CellPos(c.Pos)).ToList();
        Assert.NotEmpty(hollow);
        Assert.All(hollow, c => Assert.Empty(W.BlockAccessor.GetBlock(c).GetSelectionBoxes(W.BlockAccessor, c)));
        Assert.All(RosserRig.Cells.Where(c => c.Hollow), c =>
            Assert.Equal(c.Lid != null ? 1 : 0, W.BlockAccessor.GetBlock(rosser.CellPos(c.Pos)).GetCollisionBoxes(W.BlockAccessor, rosser.CellPos(c.Pos)).Length));
        var soil = new ItemStack(BlockOf("game:soil-medium-normal"));

        // off the trunk, a block in hand is the hand's business
        Click(player, pos, soil.Clone(), hit: new Vec3d(0.5, 0.2, 0.5));
        Assert.False(_handled);

        foreach (var (size, k) in new[] { ("sm", TrunkClass.Thin), ("xl", TrunkClass.Thick) })
        {
            Assert.Null(Click(player, pos, RosserTrunk("oak", 6, 0, size)));
            double end = RosserMod.Pace!.TripLength((int)k);
            foreach (double travel in new[] { 0, end * 0.31, end * 0.62, end })
            {
                FeedTo(rosser, travel);
                Assert.Equal(travel, rosser.Trip.Travel, 3);
                var (min, max) = RosserTrunkWorldBox(rosser, k, rosser.Trip.Travel);
                var added = Added(RosserWorldBoxes(rosser, selection: false), frameOnly);
                string where = $"{side} {size} T {travel:0.00}";
                Assert.True(added.Count > 0, where);
                Assert.Equal(Key(new Cuboidd(min.X, min.Y, min.Z, max.X, max.Y, max.Z)),
                    Key(new Cuboidd(added.Min(b => b.X1), added.Min(b => b.Y1), added.Min(b => b.Z1),
                                    added.Max(b => b.X2), added.Max(b => b.Y2), added.Max(b => b.Z2))));
                Assert.Equal(added.Select(Key).Order(), Added(RosserWorldBoxes(rosser, selection: true), frameOnly).Select(Key).Order());
                // every point of the trunk is in a box
                for (int a = 0; a <= 8; a++)
                for (int b = 0; b <= 4; b++)
                for (int c = 0; c <= 4; c++)
                {
                    var p = new Vec3d(min.X + 0.01 + (max.X - min.X - 0.02) * a / 8, min.Y + 0.01 + (max.Y - min.Y - 0.02) * b / 4, min.Z + 0.01 + (max.Z - min.Z - 0.02) * c / 4);
                    Assert.True(added.Any(x => p.X >= x.X1 && p.X <= x.X2 && p.Y >= x.Y1 && p.Y <= x.Y2 && p.Z >= x.Z1 && p.Z <= x.Z2), $"{where}: {p} is in no box");
                }
                // a hollow cell holds the trunk's part or nothing (its lid, if it has one, is collision only)
                foreach (var h in hollow)
                    foreach (var box in W.BlockAccessor.GetBlock(h).GetSelectionBoxes(W.BlockAccessor, h) ?? [])
                    {
                        var wbox = new Cuboidd(box.X1 + h.X, box.Y1 + h.Y, box.Z1 + h.Z, box.X2 + h.X, box.Y2 + h.Y, box.Z2 + h.Z);
                        Assert.True(wbox.X1 >= min.X - 1e-4 && wbox.X2 <= max.X + 1e-4 && wbox.Y1 >= min.Y - 1e-4 && wbox.Y2 <= max.Y + 1e-4
                                    && wbox.Z1 >= min.Z - 1e-4 && wbox.Z2 <= max.Z + 1e-4, $"{where}: hollow cell {h} has {Key(wbox)}");
                    }
                // on the trunk's top: the rosser's, with a block in hand, and nothing is taken
                var top = new Vec3d((min.X + max.X) / 2, max.Y, (min.Z + max.Z) / 2);
                var (cell, hit) = RosserCellHolding(rosser, top);
                Assert.True(rosser.HitsTrunk(cell, hit), where);
                Assert.Equal(1, Click(player, cell, soil.Clone(), hit: hit)?.StackSize);
                Assert.True(_handled, where);
                Assert.NotNull(rosser.Trunk);
            }
            // delivered: Ctrl on the trunk takes it, and the boxes go with it
            var (dmin, dmax) = RosserTrunkWorldBox(rosser, k, rosser.Trip.Travel);
            var (dcell, dhit) = RosserCellHolding(rosser, new Vec3d((dmin.X + dmax.X) / 2, dmax.Y, (dmin.Z + dmax.Z) / 2));
            Click(player, dcell, null, ctrl: true, hit: dhit);
            Assert.Null(rosser.Trunk);
            Assert.NotNull(TakeFromPlayer(player, s => Trunks.IsTrunk(s)));
            Assert.Equal(frameOnly.Select(Key).Order(), RosserWorldBoxes(rosser, selection: false).Select(Key).Order());
        }
        KillItemsNear(pos, 20);
    }

    /// <summary>The rosser's top is a deck, on every facing: no column of the footprint can be fallen
    /// into from above, the station's (its hollow cells, the gaps about the ring and the rolls) at
    /// its top and the beds' over their hollow cells, empty and with a thick trunk part way through.
    /// The lids are collision only: the trunk's boxes and clicks on it are the scenario above's.</summary>
    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(Facings))]
    public async Task The_rossers_top_is_solid_to_walk_on(string side, int index)
    {
        var pos = await RosserSky(45 * index, 600);
        var player = await Player("rosserdeck" + index);
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, side);
        RosserReady(rosser, player);
        AssertTopIsADeck(RosserRig.Cells, rosser.CellPos, side);
        Assert.Null(Click(player, pos, RosserTrunk("oak", 6, 0, "xl")));
        FeedTo(rosser, RosserMod.Pace!.TripLength((int)TrunkClass.Thick) * 0.5);
        AssertTopIsADeck(RosserRig.Cells, rosser.CellPos, side + ", trunk half way");
    }

    // ---- The bucking mill in line ----

    private BEBuckingMill PlaceMillAsPlayer(IPlayer player, BlockPos at, Vec3i d)
    {
        player.Entity.Pos.SetPos(new Vec3d(at.X + 0.5 - 3 * d.X, at.Y, at.Z + 0.5 - 3 * d.Z));
        var item = BlockOf("seraphhorizons:buckingmill-frame-north");
        string failure = "";
        Assert.True(item.TryPlaceBlock(W, player, new ItemStack(item), new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) }, ref failure),
            $"mill not placed: {failure}");
        return W.BlockAccessor.GetBlockEntity(at) as BEBuckingMill ?? throw new Xunit.Sdk.XunitException($"no mill at {at}");
    }

    private BERosser PlaceRosserAsPlayer(IPlayer player, BlockPos at, Vec3i d)
    {
        player.Entity.Pos.SetPos(new Vec3d(at.X + 0.5 - 3 * d.X, at.Y, at.Z + 0.5 - 3 * d.Z));
        var item = BlockOf("seraphhorizons:rosser-frame-north");
        string failure = "";
        Assert.True(item.TryPlaceBlock(W, player, new ItemStack(item), new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) }, ref failure),
            $"rosser not placed: {failure}");
        return W.BlockAccessor.GetBlockEntity(at) as BERosser ?? throw new Xunit.Sdk.XunitException($"no rosser at {at}");
    }

    /// <summary>A player looking each way places a bucking mill, then a rosser on the cell just
    /// beyond the mill's infeed end, looking the same way: they face the same way, the mill finds
    /// the rosser through its three infeed cells (the side ones are the rosser's ghosts and lead to
    /// it, only the controller's cell on the trunk's line counts), and they agree that the mill
    /// takes the trunk. Running, the rosser's debarked trunk goes onto the mill at the top of its
    /// saws' cycle and comes out as debarked logs. A trunk the mill has not taken yet stays the
    /// rosser's: breaking the rosser drops exactly that trunk once, and the mill gets nothing.</summary>
    [AtlasTheory(TimeoutMs = 300_000), MemberData(nameof(Facings))]
    public async Task A_rosser_placed_in_line_feeds_the_mill_and_breaking_it_mid_hand_off_loses_nothing(string look, int index)
    {
        var d = BlockFacing.FromCode(look).Normali;
        var origin = await RosserSky(55 * index, 450, reach: 25);
        var player = await Player("rosserline" + index);
        await StandBy(player, origin);
        var millPos = origin.AddCopy(-9 * d.X, 0, -9 * d.Z);
        var mill = PlaceMillAsPlayer(player, millPos, d);
        await World.Ticks(5);
        var at = mill.CellPos(new Int3(-6, 0, 0));
        var rosser = PlaceRosserAsPlayer(player, at, d);
        await World.Ticks(5);
        Assert.Equal(mill.Side, rosser.Side);
        int Along(BlockPos p) => (p.X - millPos.X) * d.X + (p.Z - millPos.Z) * d.Z;
        Assert.Equal(6, Along(rosser.Pos));
        Assert.Equal(21, rosser.GhostCells().Max(c => Along(c.Pos)));

        foreach (var n in Rig.InfeedNeighbours())
        {
            var cell = mill.CellPos(n);
            Assert.Same(rosser, TrunkFeeders.Find(W.BlockAccessor, cell));
            Assert.Equal(n.Z == 0, rosser.HasOutfeedCell(cell));
        }
        Assert.Equal(RackState.FeederEmpty, mill.CheckRack(out _));
        Assert.True(rosser.MillInLine());
        Assert.Equal(RosserOutfeedState.Mill, rosser.CheckOutfeed(out _));

        Assemble(mill, player);
        RosserReady(rosser, player);
        await Power(mill, full: true);
        await PowerRosser(rosser);
        KillItemsNear(millPos, 12);
        Assert.Null(Click(player, at, RosserTrunk("oak", 6, 6)));
        await World.Until(() => mill.RackState == RackState.FeederBusy, 3000);
        Assert.Contains("Rosser: a trunk is on its way through", Info(mill, player));
        float depthAtTake = -1;
        await World.Until(() =>
        {
            if (mill.Trunk != null && depthAtTake < 0)
                depthAtTake = mill.Depth;
            return mill.Trunk != null;
        }, 90_000);
        Assert.Equal(RosserState.Empty, rosser.State);
        Assert.Null(rosser.Trunk);
        Assert.True(depthAtTake <= 0.2f, $"taken with the saws at {depthAtTake}");
        Assert.True(Trunks.IsDebarked(mill.Trunk));
        Assert.Equal(6, Trunks.StoredLogs(mill.Trunk!, W));
        await World.Until(() => mill.Trunk == null, 30000);
        await World.Ticks(40);
        Assert.Equal(Cutting.LogYield(6, Mod.Config.LogsPerStoredLog), ItemsNear(millPos, 10).GetValueOrDefault("game:debarkedlog-oak-ud"));
        Assert.Empty(TrunksNear(at, 25));

        // Mid hand-off: the mill stopped, a finished trunk on the rosser's outfeed bed, the rosser broken.
        await Unpower(mill);
        Assert.Null(Click(player, at, RosserTrunk("oak", 4, 0)));
        await World.Until(() => rosser.State == RosserState.Delivered, 60000);
        await World.Until(() => mill.RackState == RackState.FeederReady, 3000);
        Assert.Contains("Rosser: its debarked trunk goes on when the saws come to the top", Info(mill, player));
        await World.Until(() => rosser.OutfeedState == RosserOutfeedState.Mill, 3000);
        Assert.Contains("The bucking mill in line takes it when its saws come to the top", Info(rosser, player));
        KillItemsNear(at, 25);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        var ghost = rosser.GhostCells().First(c => c.Kind == BERosser.CellKind.Plain).Pos;
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(2);
        Assert.Equal(RackState.None, mill.CheckRack(out _));
        await Power(mill, full: true);
        var until = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < until)
            await World.Ticks(5);
        Assert.Null(mill.Trunk);
        var left = Assert.Single(TrunksNear(at, 25));
        Assert.Equal("loggingmod:treetrunk-oak-sm-debarked-north", left.Collectible.Code.ToString());
        Assert.Equal(4, Trunks.StoredLogs(left, W));
        W.BlockAccessor.SetBlock(0, RotorPos(mill));
        KillItemsNear(at, 25);
        KillItemsNear(millPos, 12);
    }

    /// <summary>A mill in line, offset one block sideways (it still takes from the rosser's
    /// controller cell), and a rack with room at the outfeed cell the mill leaves free: the trunk
    /// is the mill's, never the rack's. With the mills' AutoPullFromRack off, or the mill gone, the
    /// next trunk goes onto the rack.</summary>
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_mill_in_line_takes_precedence_over_a_rack_at_the_outfeed_end()
    {
        var pos = await RosserSky(135, 500, reach: 22);
        var player = await Player("rosserprecedence");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "south");
        var o = Footprint.ToWorld(new Int3(-6, 0, -1), rosser.Side);
        var millAt = pos.AddCopy(-o.X, -o.Y, -o.Z);
        var mill = await PlaceMill(millAt, "south");
        Assert.Equal(pos, mill.CellPos(new Int3(-6, 0, -1)));
        var outfeed = BlockFacing.FromCode(Footprint.ToWorld(RosserRig.OutputSide, rosser.Side).Code());
        var rackPos = rosser.CellPos(new Int3(1, 0, -1));
        Assert.Contains(new Int3(1, 0, -1), RosserRig.OutfeedNeighbours());
        Assert.Equal(0, W.BlockAccessor.GetBlock(rackPos).Id);
        SetRack(rackPos, outfeed);
        await World.Ticks(2);
        Assert.True(rosser.MillInLine());
        Assert.Equal(RosserOutfeedState.Mill, rosser.CheckOutfeed(out _));
        Assert.Equal(RackState.FeederEmpty, mill.CheckRack(out _));

        Assemble(mill, player);
        RosserReady(rosser, player);
        await Power(mill, full: true);
        await PowerRosser(rosser);
        Assert.Null(Click(player, pos, RosserTrunk("oak", 5, 0)));
        await World.Until(() => mill.Trunk != null, 90_000);
        Assert.Equal(0, RackCount(rackPos));
        Assert.Equal(RosserState.Empty, rosser.State);
        await World.Until(() => mill.Trunk == null, 30000);

        // with the mills not pulling (their AutoPullFromRack off) the mill does not count as a
        // taker, and the rack gets the trunk
        Mod.Config.AutoPullFromRack = false;
        try
        {
            Assert.False(rosser.MillInLine());
            Assert.Equal(RosserOutfeedState.Rack, rosser.CheckOutfeed(out _));
            Assert.Null(Click(player, pos, RosserTrunk("oak", 5, 0)));
            await World.Until(() => RackCount(rackPos) == 1, 60000);
            Assert.Null(mill.Trunk);
            Assert.Equal(RosserState.Empty, rosser.State);
            Assert.NotNull(RosserMod.Logging!.PopTrunk(RackAt(rackPos)!));
            RackAt(rackPos)!.MarkDirty(true);
        }
        finally
        {
            Mod.Config.AutoPullFromRack = true;
        }

        // the mill broken: the rack takes the next one
        var millRotor = RotorPos(mill);
        W.BlockAccessor.GetBlock(millAt).OnBlockBroken(W, millAt, player);
        W.BlockAccessor.SetBlock(0, millRotor);
        await World.Ticks(2);
        Assert.False(rosser.MillInLine());
        Assert.Equal(RosserOutfeedState.Rack, rosser.CheckOutfeed(out _));
        Assert.Null(Click(player, pos, RosserTrunk("oak", 5, 0)));
        await World.Until(() => RackCount(rackPos) == 1, 60000);
        Assert.Equal(RosserState.Empty, rosser.State);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        KillItemsNear(pos, 22);
    }

    // ---- Saving and unloading ----

    private static int Mod32(int v) => ((v % 32) + 32) % 32;

    /// <summary>Unloads the chunk column holding <paramref name="pos"/> and loads it again; returns
    /// the block entity there afterwards (a new one, read back from the save).</summary>
    private async Task<T> UnloadAndReload<T>(BlockPos pos, BlockEntity old, Func<Task>? whileUnloaded = null) where T : BlockEntity
    {
        var sapi = (ICoreServerAPI)World.Api;
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        int cx = pos.X / size, cz = pos.Z / size;
        // saved first, as the server does before it unloads a column nobody is near
        var saved = await World.ExecuteCommand("/autosavenow");
        output.WriteLine($"/autosavenow: {saved.Ok}");
        await World.Ticks(5);
        sapi.WorldManager.UnloadChunkColumn(cx, cz);
        // waits counted in ticks: a stuck load must fail here, not run into the watchdog
        for (int i = 0; i < 200 && W.BlockAccessor.GetChunkAtBlockPos(pos) != null && ReferenceEquals(W.BlockAccessor.GetBlockEntity(pos), old); i++)
            await World.Ticks(1);
        bool gone = W.BlockAccessor.GetChunkAtBlockPos(pos) == null;
        output.WriteLine($"chunk column {cx},{cz} unloaded: {gone}");
        if (gone && whileUnloaded != null)
            await whileUnloaded();
        for (int i = 0; i < 60 && !(W.BlockAccessor.GetBlockEntity(pos) is T now && !ReferenceEquals(now, old)); i++)
        {
            sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
            await World.Ticks(10);
        }
        output.WriteLine($"loaded again: chunk {W.BlockAccessor.GetChunkAtBlockPos(pos) != null}, block {W.BlockAccessor.GetBlock(pos).Code}, entity {W.BlockAccessor.GetBlockEntity(pos)?.GetType().Name}, floor {W.BlockAccessor.GetBlock(pos.DownCopy()).Code}");
        Assert.True(W.BlockAccessor.GetBlockEntity(pos) is T again && !ReferenceEquals(again, old), "the chunk column did not load again");
        return (T)W.BlockAccessor.GetBlockEntity(pos)!;
    }

    /// <summary>A rosser in a chunk column of its own (the mill in line in the next one) unloaded
    /// and loaded again mid-trip keeps T, the rate and what has dropped, drops nothing on the way,
    /// and finishes the trunk with exactly its sticks and bark. Unloaded again with a finished
    /// trunk the stopped mill has not taken: the mill finds no feeder meanwhile, and once it runs
    /// takes the trunk exactly once.</summary>
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Unloaded_mid_trip_and_mid_hand_off_the_rosser_keeps_its_trunk_and_counters()
    {
        // looking east: the mill at x - 6 .. x - 1, the rosser at x .. x + 15, x at a chunk's start
        int dx = 45 - Mod32(World.Spawn.X + 45), dz = 620 - Mod32(World.Spawn.Z + 620) + 16;
        var sky = await RosserSky(dx + 8, dz, reach: 20);
        var pos = sky.AddCopy(-8, 0, 0);
        Assert.Equal(0, Mod32(pos.X));
        var player = await Player("rosserunload");
        var rosser = await PlaceRosser(pos, "north");
        Assert.All(rosser.GhostCells(), c => Assert.Equal(pos.X / 32, c.Pos.X / 32));
        var millPos = pos.AddCopy(-6, 0, 0);
        var mill = await PlaceMill(millPos, "north");
        Assert.Equal(pos, mill.CellPos(new Int3(-6, 0, 0)));
        Assert.All(mill.GhostCells(), c => Assert.Equal(pos.X / 32 - 1, c.Pos.X / 32));
        Assemble(mill, player);
        RosserReady(rosser, player);
        var pace = RosserMod.Pace!;
        // far from the player, so nothing keeps the column loaded
        _keeper?.Entity.TeleportTo(World.Spawn.ToVec3d());

        // mid-trip: some sticks and bark down
        KillItemsNear(pos, 20);
        Assert.Null(Click(player, pos, RosserTrunk("oak", 12, 10, "md")));
        FeedTo(rosser, TravelWithNoseAt(pace.Breaker, 2));
        var trip = rosser.Trip;
        Assert.InRange(trip.SticksDone, 1, 4);
        Assert.InRange(trip.BarkDone, 1, 11);
        await World.Ticks(30);
        var before = ItemsNear(pos, 20);
        Assert.Equal(trip.SticksDone, before.GetValueOrDefault("game:stick"));
        var again = await UnloadAndReload<BERosser>(pos, rosser);
        await World.Ticks(30);
        Assert.Equal(trip.Travel, again.Trip.Travel, 9);
        Assert.Equal(trip.Rate, again.Trip.Rate, 12);
        Assert.Equal((trip.SticksDone, trip.BarkDone, trip.Logs, trip.Branches), (again.Trip.SticksDone, again.Trip.BarkDone, again.Trip.Logs, again.Trip.Branches));
        Assert.Equal(RosserState.Feeding, again.State);
        Assert.True(again.Complete);
        Assert.Equal(TrunkClass.Thin, again.TrunkClass);
        Assert.Equal(before, ItemsNear(pos, 20));
        // its boxes are back where the trunk is
        var (min, max) = RosserTrunkWorldBox(again, TrunkClass.Thin, again.Trip.Travel);
        var (cell, hit) = RosserCellHolding(again, new Vec3d((min.X + max.X) / 2, max.Y, (min.Z + max.Z) / 2));
        Assert.True(again.HitsTrunk(cell, hit));
        var rest = FeedTo(again, 100);
        Assert.True(rest.Delivered);
        Assert.Equal(5, trip.SticksDone + rest.Sticks);
        Assert.Equal(12, trip.BarkDone + rest.BarkLogs);
        await World.Ticks(30);
        var all = ItemsNear(pos, 20);
        Assert.Equal(5, all.GetValueOrDefault("game:stick"));
        Assert.Equal(36, all.GetValueOrDefault($"{Iw}:bark-tan-green"));
        KillItemsNear(pos, 20);

        // mid hand-off: delivered, the mill stopped and peeking; the rosser's column unloads
        Assert.Equal(RosserState.Delivered, again.State);
        Assert.Equal(RackState.FeederReady, mill.CheckRack(out _));
        var third = await UnloadAndReload<BERosser>(pos, again, async () =>
        {
            Assert.Equal(RackState.None, mill.CheckRack(out _));
            Assert.False(mill.PullFromRack(passedTop: true));
            Assert.Null(mill.Trunk);
            await World.Ticks(5);
        });
        Assert.Equal(RosserState.Delivered, third.State);
        Assert.Equal(12, Trunks.StoredLogs(third.Trunk!, W));
        Assert.Equal(RackState.FeederReady, mill.CheckRack(out _));
        await StandBy(player, pos);
        await Power(mill, full: true);
        await World.Until(() => mill.Trunk != null, 30000);
        Assert.Equal(RosserState.Empty, third.State);
        Assert.Equal(12, Trunks.StoredLogs(mill.Trunk!, W));
        Assert.Empty(TrunksNear(pos, 25));
        await World.Until(() => mill.Trunk == null, 30000);
        W.BlockAccessor.SetBlock(0, RotorPos(mill));
        KillItemsNear(pos, 25);
    }

    // ---- Branch counts of real felled trees ----

    /// <summary>Not a check of the rosser: measures the branches Logging Expanded counts on trees
    /// the game grows (its tree generators, a few species and sizes), felled through its own
    /// felling listener with an axe in hand, for tuning <c>TypicalThinBranches</c> and
    /// <c>TypicalThickBranches</c> (set from this: about 2.45 branches per log). Prints a table.</summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Felled_trees_branch_counts_are_measured()
    {
        var sapi = (ICoreServerAPI)World.Api;
        var gens = sapi.World.TreeGenerators;
        foreach (var chunk in gens.Keys.Chunk(12))
            output.WriteLine("tree generators: " + string.Join(", ", chunk));
        var core = sapi.ModLoader.GetModSystem("LoggingMod.Core") ?? throw new Xunit.Sdk.XunitException("no LoggingMod.Core");
        var listener = HarmonyLib.AccessTools.Field(core.GetType(), "_fellingListener").GetValue(core)!;
        var onBreak = HarmonyLib.AccessTools.Method(listener.GetType(), "OnBreakBlock");
        var axe = W.Items.First(i => i is ItemAxe && i.Code.Path.StartsWith("axe-felling"));
        var player = await Player("rosserfeller");
        var splayer = (IServerPlayer)player;
        var rows = new List<string> { "species | size | logs | branchCount | trunks (size: logs/branches) | branchy leaves | branches per log" };
        int n = 0, measured = 0, totalLogs = 0, totalBranches = 0;
        foreach (var species in new[] { "oak", "birch", "pine", "maple", "larch", "acacia", "spruce", "redwood" })
        {
            var key = gens.Keys.FirstOrDefault(k => k.Path == species) ?? gens.Keys.FirstOrDefault(k => k.Path.Contains(species) && !k.Path.Contains("fruit"));
            if (key == null)
            {
                rows.Add($"{species} | - | no tree generator");
                continue;
            }
            foreach (float size in new[] { 0.7f, 1.0f, 1.3f })
            {
                var pos = await RosserSky(50 * (n % 5), 700 + 50 * (n / 5), reach: 14);
                n++;
                World.SetBlock("game:soil-medium-normal", pos.DownCopy());
                var bulk = sapi.World.GetBlockAccessorBulkUpdate(true, true);
                gens[key].GrowTree(bulk, pos, new TreeGenParams { size = size, skipForestFloor = true, hemisphere = EnumHemisphere.North }, new LCGRandom(4321 + n));
                bulk.Commit();
                await World.Ticks(2);
                BlockPos? log = null;
                for (int y = -1; y <= 3 && log == null; y++)
                    if (W.BlockAccessor.GetBlock(pos.UpCopy(y)).Code?.Path.StartsWith("log-") == true)
                        log = pos.UpCopy(y);
                if (log == null)
                {
                    rows.Add($"{species} | {size} | no log at the base ({W.BlockAccessor.GetBlock(pos).Code})");
                    continue;
                }
                int branchy = 0;
                for (int x = -14; x <= 14; x++)
                for (int y = 0; y <= 60; y++)
                for (int z = -14; z <= 14; z++)
                    if (W.BlockAccessor.GetBlock(pos.AddCopy(x, y, z)).Code?.Path.StartsWith("leavesbranchy-") == true)
                        branchy++;
                KillItemsNear(pos, 20);
                var slot = player.InventoryManager.ActiveHotbarSlot;
                slot.Itemstack = new ItemStack(axe);
                slot.MarkDirty();
                object[] args = [splayer, new BlockSelection { Position = log.Copy(), Face = BlockFacing.NORTH, HitPosition = new Vec3d(0.5, 0.5, 0) }, 1f, EnumHandling.PassThrough];
                onBreak.Invoke(listener, args);
                await World.Ticks(5);
                var trunks = TrunksNear(pos, 20);
                int logs = trunks.Sum(t => Trunks.StoredLogs(t, W)), branches = trunks.Sum(t => t.Attributes.GetInt(Trunks.BranchCountKey));
                rows.Add($"{species} | {size} | {logs} | {branches} | {string.Join(" ", trunks.Select(t => $"{t.Block.Variant["size"]}:{Trunks.StoredLogs(t, W)}/{t.Attributes.GetInt(Trunks.BranchCountKey)}"))} | {branchy} | {(logs > 0 ? (double)branches / logs : 0):0.00}");
                if (logs > 0)
                {
                    measured++;
                    totalLogs += logs;
                    totalBranches += branches;
                }
                slot.Itemstack = null;
                slot.MarkDirty();
                KillItemsNear(pos, 20);
                // clear the tree
                for (int x = -14; x <= 14; x++)
                for (int y = 0; y <= 60; y++)
                for (int z = -14; z <= 14; z++)
                    W.BlockAccessor.SetBlock(0, pos.AddCopy(x, y, z));
            }
        }
        foreach (var row in rows)
            output.WriteLine(row);
        output.WriteLine($"all measured trees: {totalLogs} logs, {totalBranches} branches, {(totalLogs > 0 ? (double)totalBranches / totalLogs : 0):0.00} branches per log");
        Assert.True(measured > 0, "no tree was felled into trunks:\n" + string.Join("\n", rows));
    }
}
