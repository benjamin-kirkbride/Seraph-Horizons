using Atlas.XUnit;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/CrucibleFurnace (<c>StainlessSteel</c>, #796): the crucible furnace with the
/// default <c>CrucibleFurnaceSettings</c>. Each scenario places the iron frame on a granite floor of its
/// own, builds it stage by stage and stands a clay brick chimney on its outlet; a player works it by
/// right-clicks on its cells through the blocks' <c>OnBlockInteractStart</c>, as the game calls it,
/// with the face and hit point a click would have. The clock is the world's, which a scenario must not
/// move, so a fire is aged by moving its last tick back (<see cref="BECrucibleFurnace.Age"/>) and run by
/// <see cref="BECrucibleFurnace.Update"/>.
/// </summary>
[AtlasWorld]
public class CrucibleFurnaceScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private const string Brick = "game:claybricks-good-fire";
    private CrucibleFurnaceSystem Mod => CrucibleFurnaceSystem.Of(World.Api);
    private CrucibleFurnaceRig Rig => Mod.Rig ?? throw new Xunit.Sdk.XunitException("the crucible furnace's rig did not load");
    private ITestOutputHelper Output { get; } = output;

    private sealed class Site(CrucibleFurnaceScenarios s, BlockPos origin, IPlayer player)
    {
        public BlockPos Origin { get; } = origin;
        public IPlayer Player { get; } = player;
        private IWorldAccessor W => s.W;
        public ItemSlot Hand => Player.InventoryManager.ActiveHotbarSlot;
        public ItemSlot OffHand => Player.Entity.LeftHandItemSlot;
        public bool Handled { get; private set; }
        public BlockPos? LastCell { get; private set; }

        public ItemStack Stack(string code, int size = 1) =>
            W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
            : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

        /// <summary>Right-clicks the furnace at <paramref name="native"/> (a point in its native frame,
        /// blocks) on face <paramref name="face"/> (native), holding <paramref name="held"/>; returns
        /// what is left in the hand.</summary>
        public ItemStack? Click(BECrucibleFurnace f, Float3 native, Side? face, ItemStack? held, bool shift = false, bool ctrl = false, bool creative = false)
        {
            Hand.Itemstack = held;
            Hand.MarkDirty();
            Player.Entity.Controls.ShiftKey = shift;
            Player.Entity.Controls.CtrlKey = ctrl;
            if (creative)
                Player.WorldData.CurrentGameMode = EnumGameMode.Creative;
            try
            {
                // the cell the point is on: a hair inside the face clicked (the top, without one)
                var n = face is { } inward ? inward.Normal() : new Int3(0, 1, 0);
                var world = f.WorldPoint(native);
                var inside = f.WorldPoint(new Float3(native.X - n.X * 0.001f, native.Y - n.Y * 0.001f, native.Z - n.Z * 0.001f));
                var cell = new BlockPos((int)Math.Floor(inside.X), (int)Math.Floor(inside.Y), (int)Math.Floor(inside.Z), f.Pos.dimension);
                LastCell = cell;
                var sel = new BlockSelection
                {
                    Position = cell,
                    Face = face is { } side ? BlockFacing.FromCode(Footprint.ToWorld(side, f.Side).Code()) : BlockFacing.UP,
                    HitPosition = new Vec3d(world.X - cell.X, world.Y - cell.Y, world.Z - cell.Z),
                };
                Handled = W.BlockAccessor.GetBlock(cell).OnBlockInteractStart(W, Player, sel);
            }
            finally
            {
                Player.Entity.Controls.ShiftKey = Player.Entity.Controls.CtrlKey = false;
                Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
            }
            return Hand.Itemstack;
        }

        public string Info(BECrucibleFurnace f)
        {
            var sb = new System.Text.StringBuilder();
            f.GetBlockInfo(Player, sb);
            return sb.ToString();
        }

        private IEnumerable<ItemSlot> Slots() =>
            Player.InventoryManager.Inventories.Values.Where(i => i.ClassName is "hotbar" or "backpack").SelectMany(i => i);

        public int Count(string code) =>
            Slots().Where(slot => slot.Itemstack?.Collectible?.Code?.ToString() == code).Sum(slot => slot.StackSize);

        public void EmptyInventory()
        {
            foreach (var slot in Slots().Append(OffHand))
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        public void Tongs(bool on)
        {
            OffHand.Itemstack = on ? Stack("game:tongs") : null;
            OffHand.MarkDirty();
        }
    }

    // ---- Where a click lands ----

    /// <summary>The top of the hole over pot <paramref name="i"/>'s mouth.</summary>
    private Float3 PotPoint(int i) => Rig.PotPoints[i] with { Y = Rig.HoleCell.Y + 1 };

    /// <summary>The top of the hole's middle.</summary>
    private Float3 HolePoint => new(Rig.HoleCell.X + 0.5f, Rig.HoleCell.Y + 1, Rig.HoleCell.Z + 0.3f);

    private Float3 OutletPoint => new(Rig.OutletCell.X + 0.5f, Rig.OutletCell.Y + 1, Rig.OutletCell.Z + 0.5f);

    /// <summary>The middle of the ash mouth, on the front face.</summary>
    private Float3 AshPoint => Rig.Ash;

    /// <summary>A spot 20 above spawn, <paramref name="dx"/> along, its chunks loaded, cleared over a
    /// granite floor wide enough for a row of <paramref name="furnaces"/> and a tall chimney.</summary>
    private async Task<Site> Build(string player, int dx, int furnaces = 1)
    {
        var p = await World.JoinPlayer(player);
        var origin = World.Spawn.AddCopy(dx, 20, 80);
        await p.TeleportTo(origin.AddCopy(0, 3, -3));
        int size = GlobalConstants.ChunkSize;
        if (World.Api is ICoreServerAPI sapi)
            for (int cx = (origin.X - 3) / size; cx <= (origin.X + 3 * furnaces + 3) / size; cx++)
                for (int cz = (origin.Z - 3) / size; cz <= (origin.Z + 6) / size; cz++)
                    sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null
                                && W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(3 * furnaces + 2, 0, 5)) != null, 30000);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -3; x <= 3 * furnaces + 2; x++)
        for (int z = -3; z <= 5; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y < 16; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        KillItems(origin, 3 * furnaces + 6);
        await World.Ticks(2);
        var site = new Site(this, origin, p.Player);
        p.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;   // a joined player starts in creative
        site.EmptyInventory();
        return site;
    }

    /// <summary>The frame placed at <paramref name="pos"/> facing <paramref name="side"/> (the furnace
    /// extending that way from it), with its ghosts.</summary>
    private async Task<BECrucibleFurnace> Place(BlockPos pos, string side = "south")
    {
        World.SetBlock($"seraphhorizons:cruciblefurnace-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BECrucibleFurnace ?? throw new Xunit.Sdk.XunitException($"no crucible furnace at {pos}");
        be.PlaceGhosts();
        return be;
    }

    /// <summary>Builds every stage with real items by right-clicks, in <paramref name="brick"/> brick.</summary>
    private void BuildUp(Site site, BECrucibleFurnace f, string brick = "red")
    {
        var left = site.Click(f, HolePoint, null, site.Stack("game:burnedbrick-" + brick, 25));
        Assert.True(left == null, $"handled {site.Handled}, left {left}: {site.Info(f)}");
        left = site.Click(f, AshPoint, Side.North, site.Stack("game:refractorybrick-fired-tier3", 9));
        Assert.True(left == null, $"handled {site.Handled}, left {left}, clicked {site.LastCell} ({W.BlockAccessor.GetBlock(site.LastCell!).Code}), furnace at {f.Pos}: {site.Info(f)}");
        Assert.Null(site.Click(f, OutletPoint, null, site.Stack("game:clay-fire", 4)));
        Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:refractorybrick-fired-tier3", 4)));
        Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:metalplate-iron", 2)));
        Assert.True(f.Complete, site.Info(f));
    }

    private const string TopBlock = "game:claybrickchimney-four-red-ns";

    /// <summary>A chimney on <paramref name="f"/>'s outlet: a shaft of <paramref name="height"/> blocks
    /// of <paramref name="shaft"/> (fire brick blocks unless given), topped with a clay brick chimney unless <paramref name="top"/> is false.</summary>
    private void Chimney(BECrucibleFurnace f, int height, bool top = true, string shaft = Brick)
    {
        var b = f.CellPos(Rig.ChimneyBase);
        for (int y = 0; y < 14; y++)
            World.SetBlock(y < height ? shaft : y == height && top ? TopBlock : "game:air", b.AddCopy(0, y, 0));
    }

    private Dictionary<string, int> ItemsNear(BlockPos around, int radius = 8)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 3, around.Z - radius, around.X + radius, around.Y + 6, around.Z + radius);
        return World.EntitiesIn(box).OfType<EntityItem>().Where(e => e.Alive)
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
    }

    private void KillItems(BlockPos around, int radius = 8)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 3, around.Z - radius, around.X + radius, around.Y + 6, around.Z + radius);
        foreach (var e in World.EntitiesIn(box).OfType<EntityItem>())
            e.Die(EnumDespawnReason.Removed);
    }

    /// <summary>Puts a fired pot with <paramref name="heats"/> in place <paramref name="pot"/> of an open
    /// furnace and charges it.</summary>
    private void Charge(Site site, BECrucibleFurnace f, int pot, int heats, params (string Code, int Count)[] items)
    {
        Assert.False(f.LidClosed);
        var stack = site.Stack(CrucibleFurnaceSystem.FiredPot);
        PotStack.SetHeats(stack, heats);
        Assert.Null(site.Click(f, PotPoint(pot), null, stack));
        Assert.NotNull(f.Pot(pot));
        foreach (var (code, count) in items)
            Assert.True(site.Click(f, PotPoint(pot), null, site.Stack(code, count), shift: code == "game:coke") == null, $"{code} did not all go in: {site.Info(f)}");
    }

    /// <summary>Shuts (or opens) the lid with Shift and an empty hand, if it is not so already.</summary>
    private void Lid(Site site, BECrucibleFurnace f, bool shut)
    {
        if (f.LidClosed != shut)
            site.Click(f, HolePoint, null, null, shift: true);
        Assert.Equal(shut, f.LidClosed);
    }

    /// <summary>Runs the fire <paramref name="hours"/> on.</summary>
    private static void Run(BECrucibleFurnace f, double hours)
    {
        f.Age(hours);
        f.Update();
    }

    // ---- Loading ----

    [AtlasScenario]
    public void The_furnace_its_frame_pots_ferroalloys_recipes_and_guide_exist()
    {
        Assert.True(CrucibleFurnaceSystem.Applies(World.Api));
        Assert.NotNull(Mod.Rig);
        Assert.NotNull(Mod.Lid);
        foreach (var side in Sides.All)
            Assert.IsType<BlockCrucibleFurnace>(W.GetBlock(new AssetLocation($"seraphhorizons:cruciblefurnace-frame-{side.Code()}")));
        Assert.IsType<BlockCrucibleFurnaceGhost>(W.GetBlock(new AssetLocation("seraphhorizons:cruciblefurnace-ghost")));
        foreach (var code in new[] { "seraphhorizons:meltingpot-raw", CrucibleFurnaceSystem.FiredPot, CrucibleFurnaceSystem.SmeltedPot })
            Assert.True(W.GetBlock(new AssetLocation(code)) is { Id: > 0 }, $"no {code}");
        // the old melting holes are gone, with their recipe
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("meltinghole", StringComparison.Ordinal));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path?.StartsWith("meltinghole", StringComparison.Ordinal) == true);
        Assert.IsType<BlockMeltingPotSmelted>(W.GetBlock(new AssetLocation(CrucibleFurnaceSystem.SmeltedPot)));
        Assert.NotNull(W.GetItem(new AssetLocation(Stainless.Ferrochrome)));
        Assert.NotNull(W.GetItem(new AssetLocation(Stainless.Ferrosilicon)));
        // the frame: 2 iron plates, 4 iron rods, 10 iron nails and strips, a hammer (the rig's build.frame)
        var frame = Assert.Single(W.GridRecipes, r => r.Output.Code?.ToString() == CrucibleFurnaceSystem.FrameCode && r.Enabled);
        var used = frame.ResolvedIngredients!.Where(i => i != null && !i.IsTool).GroupBy(i => i!.Code.ToString()).ToDictionary(g => g.Key, g => g.Sum(i => i!.Quantity));
        foreach (var m in Rig.FrameIngredients)
            Assert.Equal(m.Quantity, used.GetValueOrDefault(m.Code));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // every brick type and every stage's material is in the game
        foreach (var t in Rig.Bricks.Types.Where(t => t.Id != "clinker"))
            Assert.True(W.GetItem(new AssetLocation(Rig.Bricks.ItemOf(t.Id))) != null, $"no {t.Id} brick");
        foreach (var m in Rig.Stages.SelectMany(s => s.Materials).Where(m => !m.Code.Contains('*')).Append(Rig.OutletCap))
            Assert.NotNull(CrucibleFurnaceSystem.Resolve(W, m.Code));
        Assert.Contains(World.Api.GetClayformingRecipes(), r => r.Output?.Code?.ToString() == "seraphhorizons:meltingpot-raw");
        Assert.Equal(PotRecipes.Default.Recipes.Select(r => r.Code), Mod.Recipes.Recipes.Select(r => r.Code));
        Assert.Empty(Mod.Config.Sanitise());
        Assert.Equal(Mod.Config.PotCapacityUnits, Rig.Figures.PotUnits);
        Assert.Equal(Mod.Config.CokeCapacity, Rig.Figures.CokeLayers * Rig.Figures.CokePerLayer);
        Assert.Equal("Making stainless steel", Lang.GetL("en", CrucibleFurnaceSystem.GuideTitleKey));
        Assert.NotNull(World.Api.Assets.TryGet(new AssetLocation("seraphhorizons:config/handbook/cruciblefurnace.json")));
        Assert.DoesNotContain("meltinghole", Lang.GetL("en", "seraphhorizons:cruciblefurnace-text"));
        Assert.Equal("Crucible furnace frame", new ItemStack(W.GetBlock(BlockCrucibleFurnace.ItemCode)).GetName());
        // no multiblock structure for the recipe browser: the furnace is a model now
        Assert.Null(W.GetBlock(BlockCrucibleFurnace.ItemCode).Attributes?["multiblockStructure"]?.Exists == true ? "" : null);
    }

    // ---- Placing ----

    // On all four facings: room for every cell, a ghost stamped in each of the 23, the furnace running
    // away along the facing from the controller, the outlet in the back row's middle; broken through a
    // ghost, every cell is cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_frame_places_on_all_four_facings_with_ghosts()
    {
        var site = await Build("framer", 0, furnaces: 6);
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = site.Origin.AddCopy(5 * i++, 0, 0);
            var f = await Place(pos, side.Code());
            Assert.Equal(side, f.Side);
            var cells = f.GhostCells().ToList();
            Assert.Equal(23, cells.Count);
            foreach (var cell in cells)
                Assert.Equal(pos, Assert.IsType<BECrucibleFurnaceGhost>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
            var n = side.Normal();
            Assert.Equal(pos.AddCopy(n.X * 2, 1, n.Z * 2), f.CellPos(Rig.OutletCell));
            // before the brickwork, the top cells can be clicked but not walked into; the hollow ones have no box
            var top = f.CellPos(new Int3(1, 1, 1));
            Assert.Single(W.BlockAccessor.GetBlock(top).GetSelectionBoxes(W.BlockAccessor, top));
            Assert.Empty(W.BlockAccessor.GetBlock(top).GetCollisionBoxes(W.BlockAccessor, top));
            var over = f.CellPos(new Int3(0, 2, 1));
            Assert.Empty(W.BlockAccessor.GetBlock(over).GetCollisionBoxes(W.BlockAccessor, over));
            Assert.Equal("Crucible furnace frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
            Assert.Equal(side, CrucibleFurnaceRig.PlacedSide(side));

            W.BlockAccessor.GetBlock(cells[5]).OnBlockBroken(W, cells[5], site.Player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
            Assert.Equal(1, ItemsNear(pos).GetValueOrDefault(CrucibleFurnaceSystem.FrameCode));
            KillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_stages_go_in_a_stack_a_click_in_order_of_one_brick()
    {
        var site = await Build("bricklayer", 40);
        var f = await Place(site.Origin, "east");
        Assert.Contains("Next, the brickwork: 25 fired bricks of one colour", site.Info(f));
        // a later stage's material is refused and stays in hand
        Assert.Equal(9, site.Click(f, HolePoint, null, site.Stack("game:refractorybrick-fired-tier3", 9))?.StackSize);
        Assert.True(site.Handled);
        // no pot before the furnace is built
        Assert.Equal(1, site.Click(f, PotPoint(0), null, site.Stack(CrucibleFurnaceSystem.FiredPot))?.StackSize);
        // a stack a click, as many as the stage still needs
        Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:burnedbrick-black", 16)));
        Assert.Equal("black", f.BrickType);
        Assert.Contains("Next, the brickwork: 9 ", site.Info(f));
        Assert.Contains("Brick: black brick", site.Info(f));
        // a second type is refused
        Assert.Equal(16, site.Click(f, AshPoint, Side.North, site.Stack("game:burnedbrick-red", 16))?.StackSize);
        Assert.Equal(55, site.Click(f, AshPoint, Side.North, site.Stack("game:burnedbrick-black", 64))?.StackSize);
        Assert.True(f.Build!.Has(FurnaceRequires.Brickwork));
        // collision now the brickwork's, with the decks on the top cells
        var top = f.CellPos(new Int3(1, 1, 1));
        Assert.True(W.BlockAccessor.GetBlock(top).GetCollisionBoxes(W.BlockAccessor, top).Length >= 2);
        Assert.Contains("Next, the lining: 9 ", site.Info(f));
        Assert.Null(site.Click(f, OutletPoint, null, site.Stack("game:clay-fire", 4)));
        Assert.Equal(11, site.Click(f, OutletPoint, null, site.Stack("game:refractorybrick-fired-tier3", 20))?.StackSize);
        Assert.True(f.LiningIn);
        Assert.False(f.LidFitted);
        Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:metalplate-iron", 2)));
        Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:refractorybrick-fired-tier3", 4)));
        Assert.True(f.Complete);
        Assert.Equal("Crucible furnace", W.BlockAccessor.GetBlock(site.Origin).GetPlacedBlockName(W, site.Origin));
        // the lid and the damper go in open
        Assert.False(f.LidClosed);
        Assert.Equal(Damper.Open, f.Damper);
        Assert.Contains("Lid open", site.Info(f));
        Assert.Contains("Ash-pit damper: open", site.Info(f));
        Assert.Contains("West pot: none", site.Info(f));
        Assert.Contains("No draught: build a chimney of brick blocks at least 6 high", site.Info(f));
        // open, the lid has a box over the middle row
        var lidCell = f.CellPos(new Int3(0, 2, 1));
        await World.Ticks(40);   // past the swing
        Assert.NotEmpty(W.BlockAccessor.GetBlock(lidCell).GetCollisionBoxes(W.BlockAccessor, lidCell));

        // a save keeps the build
        var tree = new TreeAttribute();
        f.ToTreeAttributes(tree);
        f.FromTreeAttributes(tree, W);
        Assert.True(f.Complete);
        Assert.Equal("black", f.BrickType);

        // broken, the frame and everything fitted come back, with the cap
        Assert.Null(site.Click(f, OutletPoint, null, site.Stack("game:metalplate-iron")));
        Assert.True(f.Capped);
        KillItems(site.Origin);
        var ghost = f.CellPos(new Int3(-1, 0, 2));
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, site.Player);
        await World.Ticks(3);
        var drops = ItemsNear(site.Origin);
        Assert.Equal(1, drops.GetValueOrDefault(CrucibleFurnaceSystem.FrameCode));
        Assert.Equal(25, drops.GetValueOrDefault("game:burnedbrick-black"));
        Assert.Equal(13, drops.GetValueOrDefault("game:refractorybrick-fired-tier3"));
        Assert.Equal(4, drops.GetValueOrDefault("game:clay-fire"));
        Assert.Equal(3, drops.GetValueOrDefault("game:metalplate-iron"));
        KillItems(site.Origin);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task The_creative_shortcut_fits_the_next_stage_free()
    {
        var site = await Build("creator", 52);
        var f = await Place(site.Origin);
        int stages = 0;
        while (!f.Complete && stages < 5)
        {
            Assert.Null(site.Click(f, HolePoint, null, null, ctrl: true, creative: true));
            stages++;
        }
        Assert.Equal(3, stages);
        Assert.Equal("red", f.BrickType);
        Assert.Equal(new[] { 25, 9, 4, 4, 2 }, f.Build!.Returns().Select(d => d.Count));
    }

    // ---- The chimney, the damper and the lid ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_taller_chimney_heats_faster_and_the_damper_throttles_it()
    {
        var site = await Build("stoker", 64, furnaces: 3);
        var low = await Place(site.Origin);
        var high = await Place(site.Origin.AddCopy(6, 0, 0));
        foreach (var f in new[] { low, high })
        {
            BuildUp(site, f);
            Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:coke", 12)));
            Assert.Equal(12, f.Coke, 6);
            Assert.True(f.TryLight(site.Player));
            Lid(site, f, shut: true);
        }
        Chimney(low, 6);
        Chimney(high, 11, shaft: "game:brickcourse-four-running-red");
        low.Update();
        high.Update();
        Assert.Equal(200, low.Draught!.Value.HeatingPerHour, 6);
        Assert.Equal(400, high.Draught!.Value.HeatingPerHour, 6);
        Assert.Contains("Chimney: 6 blocks, heating 200 °C an hour, up to 1600 °C", site.Info(low));
        Assert.Contains("Chimney: 11 blocks, heating 400 °C an hour, up to 1650 °C", site.Info(high));
        double l0 = low.Temperature, h0 = high.Temperature;
        Run(low, 1);
        Run(high, 1);
        Assert.Equal(l0 + 200, low.Temperature, 0);
        Assert.Equal(h0 + 400, high.Temperature, 0);
        Assert.Equal(11, low.Coke, 3);

        // the damper on the front: half, then shut, then open again
        Assert.Null(site.Click(high, AshPoint, Side.North, null));
        Assert.Equal(Damper.Half, high.Damper);
        Assert.Contains("heating 200 °C an hour", site.Info(high));
        double t = high.Temperature;
        Run(high, 1);
        Assert.Equal(t + 200, high.Temperature, 0);
        Assert.Equal(11 - 0.6, high.Coke, 3);
        site.Click(high, AshPoint, Side.North, null);
        Assert.Equal(Damper.Shut, high.Damper);
        t = high.Temperature;
        Run(high, 2);
        Assert.Equal(t - 2 * 75, high.Temperature, 0);   // banked: a quarter of the closed rate
        Assert.Equal(11 - 0.6 - 0.5, high.Coke, 3);
        Assert.True(high.Lit);
        site.Click(high, AshPoint, Side.North, null);
        Assert.Equal(Damper.Open, high.Damper);

        // a chimney of five draws nothing
        Chimney(low, 5);
        low.Update();
        Assert.Null(low.Draught);
        Assert.Contains("No draught: the chimney is 5 blocks high, it needs 6", site.Info(low));
        // a shaft without its chimney top draws nothing; nor one with a top partway up
        Chimney(low, 6, top: false);
        low.Update();
        Assert.Null(low.Draught);
        Assert.Contains("No draught: the chimney needs a chimney top, one clay brick chimney block on its 6 brick blocks", site.Info(low));
        Chimney(low, 7);
        World.SetBlock(TopBlock, low.CellPos(Rig.ChimneyBase).AddCopy(0, 2, 0));
        low.Update();
        Assert.Null(low.Draught);
        Assert.Equal(RowProblem.ChimneyTopBuried, low.Row.Problem);
        Assert.Contains("No draught: more chimney stands on the clay brick chimney block", site.Info(low));

        // hot, the lid needs tongs; with them it opens, and the open lid cools the fire
        Assert.True(high.Temperature > GlobalConstants.TooHotToTouchTemperature);
        site.Click(high, HolePoint, null, null);
        Assert.True(high.LidClosed);
        site.Tongs(true);
        site.Click(high, HolePoint, null, null);
        Assert.False(high.LidClosed);
        Assert.True(high.LidSwinging);
        t = high.Temperature;
        Run(high, 0.5);
        Assert.Equal(t - 300, high.Temperature, 0);
        site.Tongs(false);
    }

    // ---- The row ----

    // Three furnaces side by side share the middle one's chimney, the other two capped; an uncapped
    // outlet leaves the whole row without draught, and the block info names it.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_row_of_three_shares_one_chimney_and_an_open_outlet_leaks()
    {
        var site = await Build("rowbuilder", 84, furnaces: 3);
        var row = new List<BECrucibleFurnace>();
        for (int i = 0; i < 3; i++)
            row.Add(await Place(site.Origin.AddCopy(3 * i, 0, 0), "south"));
        foreach (var f in row)
            for (int s = 0; s < 3; s++)
                site.Click(f, HolePoint, null, null, ctrl: true, creative: true);
        Assert.All(row, f => Assert.True(f.Complete));
        Chimney(row[1], 7);
        Assert.Null(site.Click(row[0], OutletPoint, null, site.Stack("game:metalplate-iron")));
        Assert.True(row[0].Capped);
        foreach (var f in row)
            f.Update();
        // the third is open: nobody draws, and each says which outlet is open
        Assert.All(row, f => Assert.Equal(RowProblem.OutletOpen, f.Row.Problem));
        Assert.All(row, f => Assert.Null(f.Draught));
        // seen from the front of a south-facing furnace (the viewer looking south), east is on the left
        Assert.True(site.Info(row[1]).Contains("No draught: the outlet of the furnace 1 to the left is open"), site.Info(row[1]));
        Assert.Contains("No draught: the outlet of the furnace 2 to the left is open", site.Info(row[0]));
        Assert.Equal(3, row[0].Row.Count);
        // capped, the row draws at the chimney's full draught, each furnace
        Assert.Null(site.Click(row[2], OutletPoint, null, site.Stack("game:metalplate-iron")));
        foreach (var f in row)
            f.Update();
        Assert.All(row, f => Assert.True(f.Row.Works, f.Row.Problem.ToString()));
        Assert.All(row, f => Assert.Equal(240, f.Draught!.Value.HeatingPerHour, 6));
        Assert.Contains("Row: 3 furnaces, the chimney on this furnace", site.Info(row[1]));
        // the cap comes back to an empty hand
        Assert.Equal("game:metalplate-iron", site.Click(row[2], OutletPoint, null, null)?.Collectible.Code.ToString());
        Assert.False(row[2].Capped);
        Assert.Equal(1, site.Count("game:metalplate-iron"));
        row[0].Update();
        Assert.Equal(RowProblem.OutletOpen, row[0].Row.Problem);
        // capped again, the row draws again
        site.Click(row[2], OutletPoint, null, site.Stack("game:metalplate-iron"));
        foreach (var f in row)
            f.Update();
        Assert.True(row[0].Row.Works);
        site.EmptyInventory();
    }

    // ---- Two pots ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Two_pots_melt_at_their_own_temperatures_and_pull_with_tongs_when_hot()
    {
        var site = await Build("potter", 100);
        var f = await Place(site.Origin);
        BuildUp(site, f, "brown");
        Chimney(f, 6);
        // west: a full pot of stainless, ferrochrome first; east: three iron ingots
        Charge(site, f, 0, 0, (Stainless.Ferrochrome, 12), ("game:ingot-iron", 2), ("game:metalbit-iron", 8));
        Charge(site, f, 1, 1, ("game:ingot-iron", 3));
        Assert.Contains("West pot: 12x Ferrochrome", site.Info(f));
        Assert.Contains("Makes stainless steel, melts at 1530 °C", site.Info(f));
        Assert.Contains("Makes iron, melts at 1538 °C", site.Info(f));
        // a second pot in a taken place is refused
        Assert.Equal(1, site.Click(f, PotPoint(1), null, site.Stack(CrucibleFurnaceSystem.FiredPot))?.StackSize);
        site.Hand.Itemstack = null;
        Lid(site, f, shut: true);
        f.SetFire(1535, 6, true);
        Run(f, 0.01);
        Assert.Equal("stainless", f.Pot(0)!.Melted);
        Assert.Null(f.Pot(1)!.Melted);
        Assert.Contains("melts at 1538 °C (", site.Info(f));
        f.SetFire(1545, 6, true);
        Run(f, 0.01);
        Assert.Equal("iron", f.Pot(1)!.Melted);
        Assert.Equal(300, f.Pot(1)!.MeltedUnits);
        Assert.Contains("West pot: molten, 300 units of Stainless steel", site.Info(f));

        // hot: no tongs, no lid and no pot
        site.Click(f, HolePoint, null, null);
        Assert.True(f.LidClosed);
        site.Tongs(true);
        site.Click(f, HolePoint, null, null);
        Assert.False(f.LidClosed);
        site.Tongs(false);
        Assert.Null(site.Click(f, PotPoint(0), null, null));
        Assert.NotNull(f.Pot(0));
        site.Tongs(true);
        // each click pulls the nearer pot
        var pot = site.Click(f, PotPoint(0), null, null);
        Assert.Null(f.Pot(0));
        Assert.NotNull(f.Pot(1));
        var smelted = Assert.IsType<BlockMeltingPotSmelted>(pot!.Collectible);
        Assert.Equal(Stainless.Ingot, smelted.GetContents(W, pot).Key.Collectible.Code.ToString());
        Assert.Equal(1, PotStack.Heats(pot));
        var iron = site.Click(f, PotPoint(1), null, null);
        Assert.Equal("game:ingot-iron", ((BlockMeltingPotSmelted)iron!.Collectible).GetContents(W, iron).Key.Collectible.Code.ToString());
        Assert.Equal(2, PotStack.Heats(iron));
        Assert.Null(f.Pot(1));
        // with no pot there, the click shuts the lid
        site.Click(f, PotPoint(1), null, null);
        Assert.True(f.LidClosed);
        site.Tongs(false);

        // cold: a pot comes out bare-handed, with its charge
        f.SetFire(20, 0, false);
        Lid(site, f, shut: false);
        Charge(site, f, 0, 1, (Stainless.Bit, 20));
        var back = site.Click(f, PotPoint(0), null, null);
        Assert.Equal(CrucibleFurnaceSystem.FiredPot, back!.Collectible.Code.ToString());
        Assert.Equal(new ChargeItem(Stainless.Bit, 20), Assert.Single(PotStack.Charge(back)));
        site.EmptyInventory();
    }

    /// <summary>Ctrl and right-click with the pot in <paramref name="site"/>'s hand on <paramref name="sel"/>
    /// (null: in the air), as the game calls a held interaction; the handling.</summary>
    private static EnumHandHandling HeldClick(Site site, BlockSelection? sel, bool ctrl = true, bool shift = false)
    {
        var controls = site.Player.Entity.Controls;
        controls.CtrlKey = ctrl;
        controls.ShiftKey = shift;
        try
        {
            var handling = EnumHandHandling.NotHandled;
            site.Hand.Itemstack!.Collectible.OnHeldInteractStart(site.Hand, site.Player.Entity, sel!, null!, true, ref handling);
            return handling;
        }
        finally
        {
            controls.CtrlKey = controls.ShiftKey = false;
        }
    }

    // A charged pot, held, empties as a bucket does: Ctrl and right-click on a block drops the charge
    // there and keeps the pot's heats; not in the air, not without Ctrl, not while too hot to touch.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_held_charged_pot_empties_with_ctrl_on_a_block_as_a_bucket_does()
    {
        var site = await Build("emptier", 164);
        var floor = site.Origin.AddCopy(0, -1, 1);
        var sel = new BlockSelection { Position = floor, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
        ItemStack Charged(int size = 1)
        {
            var pot = site.Stack(CrucibleFurnaceSystem.FiredPot, size);
            PotStack.SetHeats(pot, 2);
            PotStack.SetCharge(pot, [new ChargeItem(Stainless.Bit, 20), new ChargeItem("game:metalbit-iron", 70)]);
            return pot;
        }
        site.Hand.Itemstack = Charged();
        site.Hand.MarkDirty();
        var help = site.Hand.Itemstack.Collectible.GetHeldInteractionHelp(site.Hand);
        var empty = Assert.Single(help, h => h.ActionLangCode == "seraphhorizons:heldhelp-meltingpot-empty");
        Assert.Equal("ctrl", empty.HotKeyCode);
        Assert.True(empty.ShouldApply(empty, sel, null));

        // without Ctrl, with Shift too, or in the air: nothing
        Assert.NotEqual(EnumHandHandling.PreventDefaultAction, HeldClick(site, sel, ctrl: false));
        Assert.NotEqual(EnumHandHandling.PreventDefaultAction, HeldClick(site, null));
        Assert.Equal(2, PotStack.Charge(site.Hand.Itemstack).Count);
        // too hot to touch: as before, it keeps its charge
        site.Hand.Itemstack.Collectible.SetTemperature(W, site.Hand.Itemstack, 500);
        Assert.False(empty.ShouldApply(empty, sel, null));
        Assert.NotEqual(EnumHandHandling.PreventDefaultAction, HeldClick(site, sel));
        Assert.Equal(2, PotStack.Charge(site.Hand.Itemstack).Count);

        // cold: emptied where the click landed, the pot kept in the hand with its heats
        KillItems(site.Origin);
        site.Hand.Itemstack = Charged();
        site.Hand.MarkDirty();
        Assert.Equal(EnumHandHandling.PreventDefaultAction, HeldClick(site, sel));
        var held = site.Hand.Itemstack!;
        Assert.Equal(CrucibleFurnaceSystem.FiredPot, held.Collectible.Code.ToString());
        Assert.Empty(PotStack.Charge(held));
        Assert.Equal(2, PotStack.Heats(held));
        Assert.False(empty.ShouldApply(empty, sel, null));
        await World.Ticks(3);
        var drops = ItemsNear(site.Origin);
        Assert.Equal(20, drops.GetValueOrDefault(Stainless.Bit));
        Assert.Equal(70, drops.GetValueOrDefault("game:metalbit-iron"));
        // an empty pot: nothing more
        Assert.NotEqual(EnumHandHandling.PreventDefaultAction, HeldClick(site, sel));

        // a stack of two: one a click, the emptied one to the inventory
        KillItems(site.Origin);
        site.EmptyInventory();
        site.Hand.Itemstack = Charged(2);
        site.Hand.MarkDirty();
        Assert.Equal(EnumHandHandling.PreventDefaultAction, HeldClick(site, sel));
        Assert.Equal(1, site.Hand.StackSize);
        Assert.Equal(2, PotStack.Charge(site.Hand.Itemstack).Count);
        Assert.Equal(2, site.Count(CrucibleFurnaceSystem.FiredPot));
        await World.Ticks(3);
        Assert.Equal(20, ItemsNear(site.Origin).GetValueOrDefault(Stainless.Bit));
        KillItems(site.Origin);
        site.EmptyInventory();
    }

    // ---- Stainless and iron, end to end ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Stainless_melts_from_cold_on_its_coke_and_pours()
    {
        var site = await Build("stainlessmaker", 116);
        var f = await Place(site.Origin, "west");
        BuildUp(site, f);
        Chimney(f, 6);
        Charge(site, f, 0, 0, (Stainless.Ferrochrome, 12), ("game:ingot-iron", 2), ("game:metalbit-iron", 8));
        Charge(site, f, 1, 0, ("game:metalbit-stainlesssteel", 60));
        Assert.Null(site.Click(f, HolePoint, null, site.Stack("game:coke", 16)) is { StackSize: 4 } ? null : "the coke did not stop at 12");
        Assert.Equal(12, f.Coke, 6);
        Assert.True(f.TryLight(site.Player));
        Lid(site, f, shut: true);
        // one fill of coke on the shortest chimney: 7.55 hours to 1530 °C
        Run(f, 5);
        Assert.InRange(f.Temperature, 1000, 1040);
        Assert.Null(f.Pot(0)!.Melted);
        Run(f, 2.6);
        Assert.Equal("stainless", f.Pot(0)!.Melted);
        Assert.Equal("stainlessremelt", f.Pot(1)!.Melted);
        Assert.True(f.Lit);
        site.Tongs(true);
        Lid(site, f, shut: false);
        var pot = site.Click(f, PotPoint(0), null, null)!;
        var smelted = Assert.IsType<BlockMeltingPotSmelted>(pot.Collectible);
        var contents = smelted.GetContents(W, pot);
        Assert.Equal(300, contents.Value);
        Assert.False(smelted.HasSolidifed(pot, contents.Key, W));
        // the pour window: from the furnace's temperature to solid in 20 real seconds
        var temp = (ITreeAttribute)pot.Attributes["temperature"];
        double hoursPerSecond = W.Calendar.SpeedOfTime * W.Calendar.CalendarSpeedMul / 3600.0;
        double drop = temp.GetFloat("temperature") - 0.9 * 1530;
        Assert.Equal(drop / (20 * hoursPerSecond), temp.GetFloat("cooldownSpeed"), 0);
        var moldPos = f.CellPos(new Int3(0, 0, -2));
        World.SetBlock("game:ingotmold-blue-fired", moldPos);
        await World.Ticks(1);
        Assert.True(Assert.IsAssignableFrom<ILiquidMetalSink>(W.BlockAccessor.GetBlockEntity(moldPos)).CanReceive(contents.Key));
        // frozen: a right-click breaks the pot and knocks out 60 bits
        temp.SetFloat("temperature", 1300);
        temp.SetDouble("temperatureLastUpdate", W.Calendar.TotalHours);
        var handling = EnumHandHandling.NotHandled;
        smelted.OnHeldInteractStart(site.Hand, site.Player.Entity, null!, null!, true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
        Assert.Equal(60, site.Count(Stainless.Bit));
        site.EmptyInventory();
    }

    /// <summary>Pours the pot in the hand into the mold at <paramref name="mold"/> as a player holding
    /// right-click does, until the pot is empty or the pour stops; the handling of the start.</summary>
    private EnumHandHandling Pour(Site site, BlockPos mold, ItemStack pot)
    {
        site.Hand.Itemstack = pot;
        site.Hand.MarkDirty();
        var sel = new BlockSelection { Position = mold, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        var handling = EnumHandHandling.NotHandled;
        pot.Collectible.OnHeldInteractStart(site.Hand, site.Player.Entity, sel, null!, true, ref handling);
        if (handling == EnumHandHandling.NotHandled)
            return handling;
        float seconds = 1;
        for (int i = 0; i < 1000 && site.Hand.Itemstack?.Collectible is BlockMeltingPotSmelted held; i++)
        {
            seconds += 0.05f;
            if (!held.OnHeldInteractStep(seconds, site.Hand, site.Player.Entity, sel, null!))
                break;
        }
        return handling;
    }

    // #768: iron in both pots of one firing, teemed one after the other into a 600-unit cylinder mold
    // for its blank; a tool mold refuses a pot's iron.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Iron_from_both_pots_teems_into_one_cylinder_blank()
    {
        var site = await Build("ironfounder", 132);
        var f = await Place(site.Origin, "north");
        BuildUp(site, f, "cream");
        Charge(site, f, 0, 0, ("game:ingot-iron", 3));
        Charge(site, f, 1, 0, ("game:ingot-iron", 2), ("game:metalbit-iron", 20));
        Assert.Equal(1, site.Click(f, PotPoint(1), null, site.Stack("game:ingot-steel"))?.StackSize);   // refused
        site.Hand.Itemstack = null;
        Lid(site, f, shut: true);
        f.SetFire(1545, 6, true);
        Run(f, 0.01);
        Assert.Equal("iron", f.Pot(0)!.Melted);
        Assert.Equal("iron", f.Pot(1)!.Melted);
        site.Tongs(true);
        Lid(site, f, shut: false);
        var pots = new[] { site.Click(f, PotPoint(0), null, null)!, site.Click(f, PotPoint(1), null, null)! };
        site.Hand.Itemstack = null;
        var toolPos = f.CellPos(new Int3(-1, 0, -2));
        var cylPos = f.CellPos(new Int3(1, 0, -2));
        World.SetBlock("game:toolmold-blue-fired-axe", toolPos);
        World.SetBlock("seraphhorizons:cylindermold-blue-fired", cylPos);
        await World.Ticks(2);
        var tool = Assert.IsType<BlockEntityToolMold>(W.BlockAccessor.GetBlockEntity(toolPos));
        Assert.Equal(EnumHandHandling.PreventDefault, Pour(site, toolPos, pots[0]));
        Assert.Equal(0, tool.FillLevel);
        var cyl = Assert.IsType<BlockEntityToolMold>(W.BlockAccessor.GetBlockEntity(cylPos));
        Pour(site, cylPos, pots[0]);
        Assert.Equal(300, cyl.FillLevel);
        cyl.MetalContent.Collectible.SetTemperature(W, cyl.MetalContent, 400f, false);
        Pour(site, cylPos, pots[1]);
        Assert.Equal(600, cyl.FillLevel);
        Assert.True(cyl.IsFull);
        cyl.MetalContent.Collectible.SetTemperature(W, cyl.MetalContent, 20f, false);
        Assert.Equal("seraphhorizons:cylinderblank-iron", Assert.Single(cyl.GetStateAwareMoldedStacks()).Collectible.Code.ToString());
        site.EmptyInventory();
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Ferroalloys_break_out_and_a_charge_that_makes_nothing_will_not_light()
    {
        var site = await Build("ferromaker", 148);
        var f = await Place(site.Origin);
        BuildUp(site, f);
        site.Tongs(true);
        // too little ferrochrome: nothing made, and the coke will not light
        Charge(site, f, 0, 0, (Stainless.Ferrochrome, 2), ("game:ingot-iron", 2));
        Assert.Contains("Makes nothing: the ratio is off for stainless steel", site.Info(f));
        site.Click(f, HolePoint, null, site.Stack("game:coke", 2));
        Assert.False(f.TryLight(site.Player));
        Assert.True(site.Click(f, PotPoint(0), null, site.Stack(Stainless.Ferrochrome, 8)) == null);
        Assert.Contains("Makes stainless steel", site.Info(f));
        Assert.True(f.TryLight(site.Player));
        site.Click(f, PotPoint(0), null, null);   // the pot back out
        site.EmptyInventory();
        site.Tongs(true);
        // ferrosilicon in the west pot, ferrochrome on its last heat in the east one
        Charge(site, f, 0, 0, ("game:crushed-quartz", 30), ("game:metalbit-iron", 18), ("game:coke", 12));
        Charge(site, f, 1, 2, ("game:crushed-chromite", 30), (Stainless.Ferrosilicon, 18), ("game:lime", 12));
        Lid(site, f, shut: true);
        f.SetFire(1555, 6, true);
        Run(f, 0.01);
        Assert.Equal("ferrosilicon", f.Pot(0)!.Melted);
        Assert.Equal("ferrochrome", f.Pot(1)!.Melted);
        Lid(site, f, shut: false);
        site.Click(f, PotPoint(0), null, null);
        Assert.Equal(30, site.Count(Stainless.Ferrosilicon));
        Assert.Equal(1, PotStack.Heats(site.Hand.Itemstack));
        site.Hand.Itemstack = null;
        site.Click(f, PotPoint(1), null, null);
        Assert.Equal(30, site.Count(Stainless.Ferrochrome));
        Assert.Equal(12, site.Count("smex:slag"));
        Assert.Equal(0, site.Count(CrucibleFurnaceSystem.FiredPot));   // cracked
        // broken with a pot and coke in: they come back with the build
        site.EmptyInventory();
        f.SetFire(20, 3, false);
        Charge(site, f, 0, 1, (Stainless.Bit, 20));
        KillItems(site.Origin);
        W.BlockAccessor.GetBlock(site.Origin).OnBlockBroken(W, site.Origin, site.Player);
        await World.Ticks(3);
        var drops = ItemsNear(site.Origin);
        Assert.Equal(1, drops.GetValueOrDefault(CrucibleFurnaceSystem.FrameCode));
        Assert.Equal(1, drops.GetValueOrDefault(CrucibleFurnaceSystem.FiredPot));
        Assert.Equal(3, drops.GetValueOrDefault("game:coke"));
        Assert.Equal(25, drops.GetValueOrDefault("game:burnedbrick-red"));
        KillItems(site.Origin);
    }
}
