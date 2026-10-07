using Atlas.XUnit;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, MandrelStation (mods-src/seraphhorizons/MandrelStation/): the mandrel forging
// station in the plain world, worked by hand. Every blow is a real right-click with a hammer through
// the block's own interaction call, as the game makes it, a few ticks apart (the station takes a blow
// no faster than Forging.BlowIntervalMs). Every scenario builds on a granite floor of its own 40 above
// spawn at x -460 to -508, z -460 to -500 (clear of the draw bench's at x -240 to -300, z -240 to
// -280, and the press brake's at x -320 to -368, z -320 to -340), and they share the gear cutter's
// player, gearcutterhand (the server takes 16 players at most, and the other scenarios use the rest).
public partial class SharedWorldScenarios
{
    private MandrelStationSystem MandrelMod => MandrelStationSystem.Of(World.Api);
    private MandrelStationRig MandrelRig => MandrelMod.Rig ?? throw new Xunit.Sdk.XunitException("the mandrel station's rig did not load");

    // What it makes: the pipe section, UnifiedPipes' item, two a hollow.
    private const string PipeSectionLead = "seraphhorizons:pipesection-lead";
    private const string PipeSectionCopper = "seraphhorizons:pipesection-copper";
    private const string MandrelFrame = "seraphhorizons:mandrelstation-frame-north";
    // The base hammer, whose blows the settings count (MandrelStationSettings.BaseHammerTier, the game's
    // copper hammer's tier), and the best: steel, tier 5 against copper's 2 (hammer.json's tooltierbytype).
    private const string MandrelHammer = "game:hammer-copper";
    private const string MandrelSteelHammer = "game:hammer-steel";

    private async Task<BEMandrelStation> PlaceMandrelStation(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:mandrelstation-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEMandrelStation ?? throw new Xunit.Sdk.XunitException($"no mandrel station at {pos}");
        be.PlaceGhosts();
        return be;
    }

    /// <summary>The far cell, the ghost under the mandrel's tip.</summary>
    private static BlockPos MandrelGhost(BEMandrelStation station) => station.GhostCells().Single();

    /// <summary>The mandrel fitted by a right-click with a real rod.</summary>
    private void FitMandrel(BEMandrelStation station, IPlayer player, string rod = "game:rod-iron")
    {
        Assert.Null(CutterClick(player, station.Pos, CutterItem(rod)));
        Assert.True(station.Complete);
    }

    /// <summary>One blow: right-clicks <paramref name="at"/> with <paramref name="hammer"/> in hand a
    /// tick apart until the station takes it (a blow struck, or the hollow finished).</summary>
    private async Task MandrelBlow(BEMandrelStation station, IPlayer player, ItemStack hammer, BlockPos? at = null)
    {
        int before = station.Job.Blows;
        for (int i = 0; i < 60; i++)
        {
            await World.Ticks(1);
            CutterClick(player, at ?? station.Pos, hammer);
            Assert.True(_cutterHandled, "the station did not take the hammer's click");
            if (station.Job.Blows != before || !station.HollowOn)
                return;
        }
        throw new Xunit.Sdk.XunitException("no blow was struck");
    }

    private static string MandrelInfo(BEMandrelStation station, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        station.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private static int MandrelHeld(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    // ---- Loading ----

    [AtlasScenario]
    public void Mandrel_station_loads_with_its_blocks_recipe_and_items()
    {
        Assert.True(MandrelStationSystem.Applies(World.Api));
        Assert.NotNull(MandrelMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMandrelStation>(W.GetBlock(new AssetLocation($"seraphhorizons:mandrelstation-frame-{side}")));
        Assert.IsType<BlockMandrelStationGhost>(W.GetBlock(new AssetLocation("seraphhorizons:mandrelstation-ghost")));
        // the mandrels: the game's rods
        foreach (var code in MandrelPart.Codes)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false }, $"no {code}");
        // what it forges, the game's lead and copper chute sections (the hollow), and what comes off:
        // UnifiedPipes' pipe sections
        foreach (var k in new[] { 1, 2 })
        {
            Assert.True(W.GetItem(new AssetLocation(Forging.HollowFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Forging.HollowFor(k)}");
            Assert.True(W.GetItem(new AssetLocation(Forging.SectionFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Forging.SectionFor(k)}");
        }
        Assert.Equal((PipeSectionLead, PipeSectionCopper), (Forging.SectionFor(1), Forging.SectionFor(2)));
        Assert.True(W.GetItem(new AssetLocation(MandrelHammer)) is { Id: > 0, IsMissing: false });
        // the base tier is the copper hammer's, as the game defines it
        Assert.Equal(W.GetItem(new AssetLocation(MandrelHammer))!.ToolTier, MandrelMod.Config.BaseHammerTier);
        Assert.Equal("Mandrel forging station frame", new ItemStack(W.GetBlock(BlockMandrelStation.ItemCode)).GetName());

        // the grid: the frame, an oak log, an iron plate and nails and strips, with a hammer
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == MandrelFrame && r.Enabled);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:log-placed-oak-ud");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the settings and the rig's pace agree
        Assert.Equal(MandrelRig.BlowsPerHollow[1], MandrelMod.Config.BlowsPerHollowLead);
        Assert.Equal(MandrelRig.BlowsPerHollow[2], MandrelMod.Config.BlowsPerHollowCopper);
        // in the creative inventory's mechanics tab
        Assert.Contains("mechanics", W.GetBlock(BlockMandrelStation.ItemCode)!.CreativeInventoryTabs ?? []);
    }

    // ---- Placing ----

    // On all four facings: room for both cells, a ghost stamped in the far one, the station running
    // away along the facing from the stump, each cell's collision boxes its selection boxes and no
    // lid (a hand station has no deck to walk on); broken through the ghost, both cells are cleared
    // and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_places_on_all_four_facings_with_its_ghost_and_no_lids()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(-460 - 12 * i++, -460);
            var station = await PlaceMandrelStation(pos, side.Code());
            Assert.Equal(side, station.Side);
            var ghost = MandrelGhost(station);
            var n = side.Normal();
            Assert.Equal(pos.AddCopy(n.X, 0, n.Z), ghost);
            Assert.Equal(pos, Assert.IsType<BEMandrelStationGhost>(W.BlockAccessor.GetBlockEntity(ghost)).Principal);
            int c = 0;
            foreach (var cell in new[] { pos, ghost })
            {
                var block = W.BlockAccessor.GetBlock(cell);
                var selection = block.GetSelectionBoxes(W.BlockAccessor, cell);
                var collision = block.GetCollisionBoxes(W.BlockAccessor, cell);
                Assert.NotNull(selection);
                Assert.Null(MandrelRig.Cells[c++].Lid);
                Assert.Equal(selection.Length, collision.Length);
            }
            Assert.Equal("Mandrel forging station frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
            Assert.Equal("Mandrel forging station frame", W.BlockAccessor.GetBlock(ghost).GetPlacedBlockName(W, ghost));
            // the infeed is beside the stump (native west), the outfeed beyond the tip
            var west = Footprint.ToWorld(Side.West, side).Normal();
            Assert.Contains(pos.AddCopy(west.X, 0, west.Z), MandrelRig.InfeedNeighbours().Select(station.CellPos));
            Assert.Equal(pos.AddCopy(2 * n.X, 0, 2 * n.Z), station.CellPos(MandrelRig.OutputNeighbour()));

            W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.Equal(0, W.BlockAccessor.GetBlock(ghost).Id);
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(MandrelFrame));
            CutterKillItems(pos);
        }
    }

    // ---- The mandrel ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_fits_its_mandrel_and_gives_it_back()
    {
        var pos = await CutterSite(-460, -480);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "east");
        var ghost = MandrelGhost(station);
        Assert.Contains("Next part: mandrel", MandrelInfo(station, player));

        // no hollow before the mandrel, and no blow on a bare frame
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Forging.LeadHollow))?.StackSize);
        Assert.False(station.HollowOn);
        Assert.NotNull(CutterClick(player, pos, CutterItem(MandrelHammer)));
        Assert.False(station.HollowOn);
        // a rod of a metal it does not take is the item's own business; so is an empty hand
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-copper"))?.StackSize);
        Assert.False(_cutterHandled);
        Assert.Null(CutterClick(player, pos, null));
        Assert.False(_cutterHandled);

        // a steel rod, one from a stack of two, through the ghost; a second has nowhere to go
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:rod-steel", 2))?.StackSize);
        Assert.Equal("game:rod-steel", station.Mandrel);
        Assert.Equal("steel", station.MandrelMetal);
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-iron"))?.StackSize);
        Assert.Equal("game:rod-steel", station.Mandrel);
        Assert.Equal("Mandrel forging station", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Mandrel bare", MandrelInfo(station, player));
        Assert.True(station.PartFitted("mandrel"));

        // a save keeps it
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        station.ToTreeAttributes(tree);
        station.FromTreeAttributes(tree, W);
        Assert.Equal("game:rod-steel", station.Mandrel);

        // Ctrl takes it back; the creative shortcut fits an iron one free
        CutterClick(player, pos, null, ctrl: true);
        Assert.Equal(1, MandrelHeld(player, "game:rod-steel"));
        Assert.False(station.Complete);
        Assert.Null(CutterClick(player, ghost, null, ctrl: true, creative: true));
        Assert.Equal("game:rod-iron", station.Mandrel);
    }

    // ---- Forging ----

    // A lead hollow goes on by hand and is forged a blow at a time, each a right-click with a copper
    // hammer (the base) that costs the hammer a point; at the sixth two lead pipe sections drop beyond the tip and the
    // hollow is used up. An angle, a pipe section, an ingot and a plate never go on.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_strikes_a_lead_hollow_into_two_pipe_sections()
    {
        var pos = await CutterSite(-472, -480);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "south");
        FitMandrel(station, player);

        // not what it forges: they stay in hand
        foreach (var code in new[] { "seraphhorizons:angle-lead", "seraphhorizons:angle-copper", PipeSectionLead, PipeSectionCopper, "game:ingot-lead", "game:metalplate-copper" })
        {
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false }, $"no {code}");
            Assert.Equal(1, CutterClick(player, pos, CutterItem(code))?.StackSize);
            Assert.False(station.HollowOn, $"{code} went on");
        }
        // a blow on a bare mandrel with no infeed does nothing
        Assert.NotNull(CutterClick(player, pos, CutterItem(MandrelHammer)));
        Assert.True(_cutterHandled);
        Assert.False(station.HollowOn);

        // a lead hollow goes on, one from the stack; another in hand does not
        Assert.Equal(2, CutterClick(player, pos, CutterItem(Forging.LeadHollow, 3))?.StackSize);
        Assert.True(station.HollowOn);
        Assert.Equal(1, station.Job.Class);
        Assert.Equal(1, station.HollowClass);
        Assert.Equal(Forging.LeadHollow, station.Hollow?.Collectible.Code.ToString());
        Assert.Equal(2, CutterClick(player, pos, CutterItem(Forging.LeadHollow, 2))?.StackSize);
        Assert.Contains("A lead hollow section on the mandrel: 0 blows struck, 0% forged", MandrelInfo(station, player));

        // nothing moves without blows; each blow, from the stump or the ghost, is a sixth
        await World.Ticks(10);
        Assert.Equal(0, station.Job.Work);
        var hammer = CutterItem(MandrelHammer);
        int durability = hammer.Collectible.GetRemainingDurability(hammer);
        for (int b = 1; b <= 5; b++)
        {
            await MandrelBlow(station, player, hammer, b % 2 == 0 ? MandrelGhost(station) : pos);
            Assert.Equal(b, station.Job.Blows);
            Assert.Equal(b / 6.0, station.Job.Work, 6);
            Assert.True(station.HollowOn);
        }
        Assert.Equal(durability - 5 * MandrelMod.Config.HammerWearPerBlow, hammer.Collectible.GetRemainingDurability(hammer));
        Assert.Contains("5 blows struck, 83% forged", MandrelInfo(station, player));
        // a save keeps the blows and W
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        station.ToTreeAttributes(tree);
        station.FromTreeAttributes(tree, W);
        Assert.Equal((1, 5), (station.Job.Class, station.Job.Blows));
        Assert.Equal(5 / 6.0, station.Job.Work, 6);

        // the last blow: two lead pipe sections drop beyond the tip, the hollow is used up
        await MandrelBlow(station, player, hammer);
        Assert.False(station.HollowOn);
        await World.Ticks(5);
        var near = CutterItemsNear(pos);
        Assert.Equal(2, near.GetValueOrDefault(PipeSectionLead));
        Assert.Equal(0, near.GetValueOrDefault(Forging.LeadHollow));
        foreach (var e in World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
                     .OfType<EntityItem>().Where(e => e.Itemstack.Collectible.Code.ToString() == PipeSectionLead))
        {
            var local = Footprint.ToLocal(new Float3((float)(e.Pos.X - pos.X), (float)(e.Pos.Y - pos.Y), (float)(e.Pos.Z - pos.Z)), station.Side);
            Assert.True(local.Z > 1.99f, $"the section is at native {local}");
        }
        Assert.Contains("Mandrel bare", MandrelInfo(station, player));
        CutterKillItems(pos);
    }

    // Copper forges the same, into two copper pipe sections, at nine blows against lead's six.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_takes_more_blows_for_copper()
    {
        var pos = await CutterSite(-484, -480);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "west");
        FitMandrel(station, player, "game:rod-meteoriciron");
        Assert.Null(CutterClick(player, pos, CutterItem(Forging.CopperHollow)));
        Assert.Equal(2, station.Job.Class);
        Assert.Equal(9, station.BlowsNeeded);
        var hammer = CutterItem(MandrelHammer);
        for (int b = 1; b <= 6; b++)
            await MandrelBlow(station, player, hammer);
        // six blows finish a lead hollow, two thirds of a copper one
        Assert.True(station.HollowOn);
        Assert.Equal(6 / 9.0, station.Job.Work, 6);
        for (int b = 7; b <= 9; b++)
            await MandrelBlow(station, player, hammer);
        Assert.False(station.HollowOn);
        await World.Ticks(5);
        Assert.Equal(2, CutterItemsNear(pos).GetValueOrDefault(PipeSectionCopper));
        CutterKillItems(pos);
    }

    // A steel hammer (tier 5) forges two and a half copper blows a blow, the game's ratio: lead in
    // three blows, copper in four, each costing the hammer its one point.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_forges_faster_with_a_steel_hammer_by_its_tier()
    {
        var pos = await CutterSite(-460, -500);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "north");
        FitMandrel(station, player);
        var hammer = CutterItem(MandrelSteelHammer);
        int steel = hammer.Collectible.ToolTier, copper = W.GetItem(new AssetLocation(MandrelHammer))!.ToolTier;
        Assert.Equal((5, 2), (steel, copper));
        double perBlow = (double)steel / copper / MandrelMod.Config.BlowsPerHollowLead;
        int durability = hammer.Collectible.GetRemainingDurability(hammer);

        Assert.Null(CutterClick(player, pos, CutterItem(Forging.LeadHollow)));
        for (int b = 1; b <= 2; b++)
        {
            await MandrelBlow(station, player, hammer);
            Assert.True(station.HollowOn);
            Assert.Equal(b * perBlow, station.Job.Work, 6);
        }
        await MandrelBlow(station, player, hammer);
        Assert.False(station.HollowOn);
        Assert.Equal(durability - 3 * MandrelMod.Config.HammerWearPerBlow, hammer.Collectible.GetRemainingDurability(hammer));
        await World.Ticks(5);
        Assert.Equal(2, CutterItemsNear(pos).GetValueOrDefault(PipeSectionLead));
        CutterKillItems(pos);

        // copper, nine copper blows: four of steel
        Assert.Null(CutterClick(player, pos, CutterItem(Forging.CopperHollow)));
        for (int b = 1; b <= 3; b++)
        {
            await MandrelBlow(station, player, hammer);
            Assert.True(station.HollowOn);
            Assert.Equal(b * steel / (double)copper / MandrelMod.Config.BlowsPerHollowCopper, station.Job.Work, 6);
        }
        await MandrelBlow(station, player, hammer);
        Assert.False(station.HollowOn);
        Assert.Equal(4, Forging.BlowsWith(MandrelMod.Config.BlowsPerHollowCopper, steel, copper));
        await World.Ticks(5);
        Assert.Equal(2, CutterItemsNear(pos).GetValueOrDefault(PipeSectionCopper));
        CutterKillItems(pos);
    }

    // A hammer with no tool tier counts as the base: the steel hammer with its tier taken away for the
    // scenario (and given back) forges lead in the copper hammer's six blows.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_counts_a_hammer_with_no_tier_as_the_base()
    {
        var pos = await CutterSite(-472, -500);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "east");
        FitMandrel(station, player);
        var item = W.GetItem(new AssetLocation(MandrelSteelHammer))!;
        int tier = item.ToolTier;
        try
        {
            item.ToolTier = 0;
            var hammer = CutterItem(MandrelSteelHammer);
            Assert.Null(CutterClick(player, pos, CutterItem(Forging.LeadHollow)));
            for (int b = 1; b <= 5; b++)
            {
                await MandrelBlow(station, player, hammer);
                Assert.True(station.HollowOn);
                Assert.Equal(b / 6.0, station.Job.Work, 6);
            }
            await MandrelBlow(station, player, hammer);
            Assert.False(station.HollowOn);
        }
        finally
        {
            item.ToolTier = tier;
        }
        await World.Ticks(5);
        Assert.Equal(2, CutterItemsNear(pos).GetValueOrDefault(PipeSectionLead));
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_gives_back_an_unstruck_hollow_and_everything_when_broken()
    {
        var pos = await CutterSite(-496, -480);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "north");
        FitMandrel(station, player, "game:rod-steel");

        // an unstruck hollow comes back by Ctrl; the mandrel stays while one is on
        Assert.Null(CutterClick(player, pos, CutterItem(Forging.CopperHollow)));
        CutterClick(player, pos, null, ctrl: true);
        Assert.False(station.HollowOn);
        Assert.Equal(1, MandrelHeld(player, Forging.CopperHollow));
        Assert.True(station.Complete);
        // once struck it stays, and so does the mandrel
        Assert.Null(CutterClick(player, pos, CutterItem(Forging.LeadHollow)));
        await MandrelBlow(station, player, CutterItem(MandrelHammer));
        CutterClick(player, MandrelGhost(station), null, ctrl: true);
        Assert.True(station.HollowOn);
        Assert.True(station.Complete);
        Assert.Equal(0, MandrelHeld(player, Forging.LeadHollow));
        Assert.Equal(0, MandrelHeld(player, "game:rod-steel"));

        // broken with a struck hollow on: the frame and the mandrel, and the hollow is lost
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(MandrelGhost(station)).OnBlockBroken(W, MandrelGhost(station), player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(MandrelFrame));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-steel"));
        Assert.Equal(0, drops.GetValueOrDefault(Forging.LeadHollow));
        Assert.Equal(0, drops.GetValueOrDefault(PipeSectionLead));
        CutterKillItems(pos);

        // broken with an unstruck hollow on, the hollow comes back too
        station = await PlaceMandrelStation(pos, "north");
        FitMandrel(station, player);
        Assert.Null(CutterClick(player, pos, CutterItem(Forging.LeadHollow)));
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(Forging.LeadHollow));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-iron"));
        Assert.Equal(1, drops.GetValueOrDefault(MandrelFrame));
        CutterKillItems(pos);
    }

    // ---- Infeed and outfeed ----

    // A blow on a bare mandrel takes a hollow from a chest beside the stump, never anything else (an
    // ingot, an angle, a pipe section), and the sections go into a chest beyond the tip.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_takes_hollows_from_a_chest_when_struck_and_puts_sections_in_one()
    {
        var pos = await CutterSite(-508, -480);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "north");
        FitMandrel(station, player);
        var infeed = station.CellPos(new Int3(-1, 0, 0));
        Assert.Contains(new Int3(-1, 0, 0), MandrelRig.InfeedNeighbours());
        var outfeed = station.CellPos(MandrelRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        source.Inventory[0].Itemstack = CutterItem("game:ingot-copper");
        source.Inventory[1].Itemstack = CutterItem("seraphhorizons:angle-lead");
        source.Inventory[2].Itemstack = CutterItem(Forging.LeadHollow, 2);
        source.Inventory[3].Itemstack = CutterItem(PipeSectionCopper);
        source.MarkDirty(true);

        // a hand station: nothing is taken until it is struck
        await World.Ticks(20);
        Assert.False(station.HollowOn);
        var hammer = CutterItem(MandrelHammer);
        CutterClick(player, pos, hammer);
        Assert.True(station.HollowOn);
        Assert.Equal(1, station.Job.Class);
        Assert.Equal(0, station.Job.Blows);
        Assert.Equal(1, source.Inventory[2].StackSize);
        for (int b = 0; b < 6; b++)
            await MandrelBlow(station, player, hammer);
        Assert.False(station.HollowOn);
        Assert.Equal(2, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == PipeSectionLead).Sum(s => s.StackSize));
        Assert.DoesNotContain(PipeSectionLead, CutterItemsNear(pos).Keys);
        // struck again, the next hollow goes on
        CutterClick(player, pos, hammer);
        Assert.True(station.HollowOn);
        Assert.True(source.Inventory[2].Empty);
        for (int b = 0; b < 6; b++)
            await MandrelBlow(station, player, hammer);
        Assert.Equal(4, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == PipeSectionLead).Sum(s => s.StackSize));
        // only the ingot, the angle and the pipe section are left, and nothing goes on
        CutterClick(player, pos, hammer);
        Assert.False(station.HollowOn);
        Assert.Equal(1, source.Inventory[0].StackSize);
        Assert.Equal(1, source.Inventory[1].StackSize);
        Assert.Equal(1, source.Inventory[3].StackSize);
    }
}
