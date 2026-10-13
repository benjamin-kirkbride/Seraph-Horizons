using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The mine order's rules: the area's limits, what it breaks, the strike's length against a
/// seraph's steel pickaxe and its slam, its reach (never under its own box), which group it strikes
/// next, flat or upright, and where it stands to reach a square.</summary>
public class EidolonMiningTests
{
    // ---- the area ----

    private static (Marks Marks, MarkStep Step) Mark(MarkPos a, MarkPos b) =>
        EidolonMarking.Click(EidolonMarkKind.Area, new Marks(a, null), b, EidolonMining.MaxAreaSide, EidolonMining.MaxAreaBlocks);

    [Fact]
    public void AnAreaHoldsAtMost16384Blocks()
    {
        var origin = new MarkPos(0, 0, 0);
        // 32 × 16 × 32 is the most; one layer more is refused and the first corner kept.
        Assert.Equal(MarkStep.Area, Mark(origin, new MarkPos(31, 15, 31)).Step);
        var (marks, step) = Mark(origin, new MarkPos(31, 16, 31));
        Assert.Equal(MarkStep.TooManyBlocks, step);
        Assert.Equal(origin, marks.First);
        Assert.Null(marks.Second);
        Assert.Equal(16384, MarkArea.Between(origin, new MarkPos(31, 15, 31)).Volume);
    }

    [Fact]
    public void AnAreaIsAtMost1024BlocksOnItsLongestSide()
    {
        var origin = new MarkPos(0, 0, 0);
        // A strip 1024 long, 16 wide and one high is 16384 blocks.
        Assert.Equal(MarkStep.Area, Mark(origin, new MarkPos(1023, 0, 15)).Step);
        Assert.Equal(MarkStep.TooLarge, Mark(origin, new MarkPos(1024, 0, 0)).Step);
        Assert.True(EidolonMining.AreaFits(MarkArea.Between(origin, new MarkPos(0, 0, 1023))));
        Assert.False(EidolonMining.AreaFits(MarkArea.Between(origin, new MarkPos(15, 1, 1023))));
        Assert.False(EidolonMining.AreaFits(MarkArea.Between(origin, new MarkPos(0, 0, 1024))));
    }

    [Fact]
    public void OtherModesHaveNoBlockLimitAndAreaThenBlockKeepsOne()
    {
        var origin = new MarkPos(0, 0, 0);
        // A fell area 32 a side and 64 high: no limit on blocks unless the mode sets one.
        Assert.Equal(MarkStep.Area, EidolonMarking.Click(EidolonMarkKind.Area, new Marks(origin, null), new MarkPos(31, 63, 31), 32).Step);
        Assert.Equal(MarkStep.TooManyBlocks,
            EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, new Marks(origin, null), new MarkPos(9, 9, 9), 32, 999).Step);
    }

    // ---- what it breaks ----

    private static readonly MineBlock Stone = new(false, false, false, false, true, 2, true);

    [Fact]
    public void ItBreaksWhatASteelPickBreaksUpToItsTier()
    {
        // The game's steel pickaxe is tier 5.
        int tier = EidolonMining.PickTier(5);
        Assert.Equal(MineRule.Mine, EidolonMining.Rule(Stone, tier));
        Assert.Equal(MineRule.Mine, EidolonMining.Rule(Stone with { RequiredTier = 5 }, tier));
        Assert.Equal(MineRule.Mine, EidolonMining.Rule(Stone with { RequiredTier = 0 }, tier));
        Assert.Equal(MineRule.TooHard, EidolonMining.Rule(Stone with { RequiredTier = 6 }, tier));
        // The mantle: tier 99.
        Assert.Equal(MineRule.TooHard, EidolonMining.Rule(Stone with { RequiredTier = 99 }, tier));
        Assert.Equal(MineRule.TooHard, EidolonMining.Rule(Stone with { PickBreaks = false }, tier));
        // Another pick's tier counts as read; none read, the steel pick's.
        Assert.Equal(MineRule.TooHard, EidolonMining.Rule(Stone with { RequiredTier = 4 }, EidolonMining.PickTier(3)));
        Assert.Equal(EidolonMining.SteelPickTier, EidolonMining.PickTier(null));
        Assert.Equal(EidolonMining.SteelPickTier, EidolonMining.PickTier(0));
        Assert.Equal(5, EidolonMining.SteelPickTier);
    }

    [Fact]
    public void ItLeavesLiquidsWhatTouchesThemBlockEntitiesAndClaims()
    {
        Assert.Equal(MineRule.Air, EidolonMining.Rule(Stone with { Air = true }));
        Assert.Equal(MineRule.Liquid, EidolonMining.Rule(Stone with { Liquid = true }));
        Assert.Equal(MineRule.LiquidBeside, EidolonMining.Rule(Stone with { LiquidBeside = true }));
        Assert.Equal(MineRule.BlockEntity, EidolonMining.Rule(Stone with { HasBlockEntity = true }));
        Assert.Equal(MineRule.Claimed, EidolonMining.Rule(Stone with { MayBreak = false }));
    }

    [Fact]
    public void APickBreaksABlockNeedingATierOnlyWithASpeedForItsMaterial()
    {
        Assert.True(EidolonMining.PickBreaks(0, false));
        Assert.True(EidolonMining.PickBreaks(2, true));
        Assert.False(EidolonMining.PickBreaks(1, false));
    }

    // ---- how long ----

    [Fact]
    public void ASeraphsSteelPickBreaksAsTheGameReckons()
    {
        // pickaxe-steel's stone speed is 9 and rock's resistance 8: 8/9 of a second.
        Assert.Equal(8 / 9.0, EidolonMining.PlayerSeconds(8, EidolonMining.PickSpeed(9)), 6);
        // Soil: no speed for its material, the bare hand's 1: its resistance in seconds.
        Assert.Equal(1.8, EidolonMining.PlayerSeconds(1.8, EidolonMining.PickSpeed(null)), 6);
        // The game's tool mining speed modifier multiplies the pick's speed.
        Assert.Equal(18, EidolonMining.PickSpeed(9, 2), 6);
    }

    [Fact]
    public void ItDigsAtTheBetterOfASteelPickaxesAndShovelsSpeed()
    {
        // Soil: the pickaxe has no speed for it, shovel-steel's is 7.
        Assert.Equal(7, EidolonMining.DigSpeed(null, 7), 6);
        Assert.Equal(1.8 / 7, EidolonMining.PlayerSeconds(1.8, EidolonMining.DigSpeed(null, 7)), 6);
        // Stone: the shovel has none, pickaxe-steel's is 9.
        Assert.Equal(9, EidolonMining.DigSpeed(9, null), 6);
        // Both: the faster.
        Assert.Equal(9, EidolonMining.DigSpeed(9, 4.4), 6);
        Assert.Equal(7, EidolonMining.DigSpeed(3.25, 7), 6);
        // Neither: the bare hand's 1.
        Assert.Equal(1, EidolonMining.DigSpeed(null, null), 6);
        // The modifier multiplies either.
        Assert.Equal(14, EidolonMining.DigSpeed(null, 7, 2), 6);
        // Soil now takes the fastest slam's time, no longer twice rock's.
        var soil = EidolonMining.Timing(EidolonMining.StrikeSeconds([EidolonMining.PlayerSeconds(1.8, EidolonMining.DigSpeed(null, 7))]));
        Assert.Equal(EidolonMining.MaxSlamSpeed, soil.SlamSpeed, 6);
        Assert.Equal(0.5, soil.Seconds, 6);
    }

    [Fact]
    public void AStrikeTakesItsSlowestBlockAtThreeQuartersOfASeraphsPace()
    {
        Assert.Equal(8 / 9.0 / 0.75, EidolonMining.StrikeSeconds([8 / 9.0, 0.2, 0.5]), 6);
        Assert.Equal(2.4, EidolonMining.StrikeSeconds([1.8, 8 / 9.0]), 6);
    }

    [Fact]
    public void TheSlamFillsTheStrikeAndLandsWithTheBlows()
    {
        // A strike as long as the slam: played as it is, landing on frame 35.
        var even = EidolonMining.Timing(1.5);
        Assert.Equal(1, even.SlamSpeed, 6);
        Assert.Equal(0, even.SlamAt, 6);
        Assert.Equal(35 / 30.0, even.ImpactAt, 6);
        Assert.Equal(1.5, even.Seconds, 6);
        // Rock (1.185 s): a little quicker, landing at 35/45 of the strike.
        var rock = EidolonMining.Timing(8 / 9.0 / 0.75);
        Assert.Equal(rock.Seconds * 35 / 45, rock.ImpactAt, 6);
        // A slow strike: the slowest slam, standing first, landing with the strike's end less the slam's tail.
        var slow = EidolonMining.Timing(6);
        Assert.Equal(EidolonMining.MinSlamSpeed, slow.SlamSpeed, 6);
        Assert.Equal(6, slow.Seconds, 6);
        Assert.Equal(3, slow.SlamAt, 6);
        Assert.Equal(3 + 35 / 30.0 / 0.5, slow.ImpactAt, 6);
        // A quick one takes the fastest slam's time.
        var quick = EidolonMining.Timing(0.1);
        Assert.Equal(EidolonMining.MaxSlamSpeed, quick.SlamSpeed, 6);
        Assert.Equal(0.5, quick.Seconds, 6);
        Assert.True(quick.ImpactAt < quick.Seconds);
    }

    // ---- reach ----

    [Fact]
    public void ItReachesTwoBlocksPastItsBoxFromUnderItsFeetToItsHead()
    {
        // At a node, its box stands over columns 9 and 10.
        Assert.Equal((9, 10, 19, 20), EidolonMining.Footprint(10, 20));
        Assert.True(EidolonMining.InReach(10, 5, 20, new MarkPos(7, 5, 20)));
        Assert.True(EidolonMining.InReach(10, 5, 20, new MarkPos(12, 5, 20)));
        Assert.False(EidolonMining.InReach(10, 5, 20, new MarkPos(6, 5, 20)));
        Assert.False(EidolonMining.InReach(10, 5, 20, new MarkPos(13, 5, 20)));
        Assert.True(EidolonMining.InReach(10, 5, 20, new MarkPos(11, 4, 20)));
        Assert.False(EidolonMining.InReach(10, 5, 20, new MarkPos(11, 3, 20)));
        Assert.True(EidolonMining.InReach(10, 5, 20, new MarkPos(11, 8, 20)));
        Assert.False(EidolonMining.InReach(10, 5, 20, new MarkPos(11, 9, 20)));
    }

    [Fact]
    public void ItNeverReachesUnderItsOwnBox()
    {
        for (int y = 0; y < 8; y++)
        {
            Assert.False(EidolonMining.InReach(10, 5, 20, new MarkPos(9, y, 19)));
            Assert.False(EidolonMining.InReach(10, 5, 20, new MarkPos(10, y, 20)));
        }
        // Off its node, its box stands over a third column, and that is under it too.
        Assert.Equal((9, 11, 19, 20), EidolonMining.Footprint(10.4, 20));
        Assert.True(EidolonMining.UnderFoot(10.4, 20, new MarkPos(11, 4, 20)));
        Assert.False(EidolonMining.InReach(10.4, 5, 20, new MarkPos(11, 4, 20)));
    }

    // ---- what next ----

    private static readonly MarkArea Pit = MarkArea.Between(new MarkPos(0, 0, 0), new MarkPos(5, 1, 5));

    private static Func<MarkPos, bool> Cells(IEnumerable<MarkPos> cells)
    {
        var set = cells.ToHashSet();
        return set.Contains;
    }

    private static IEnumerable<MarkPos> Box(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
            yield return new MarkPos(x, y, z);
    }

    [Fact]
    public void SquaresTileTheAreaFromItsLowerCorner()
    {
        var area = MarkArea.Between(new MarkPos(3, 0, -4), new MarkPos(8, 0, 2));
        Assert.Equal((3, -4), EidolonMining.Square(area, 4, -3));
        Assert.Equal((5, -2), EidolonMining.Square(area, 5, -1));
        Assert.Equal((7, 2), EidolonMining.Square(area, 8, 2));
        // A square on the area's edge holds only its columns inside.
        Assert.Equal(2, EidolonMining.SquareColumns(area, (7, 2)).Count());
    }

    [Fact]
    public void StandingOnTheLayerItStrikesFlatBesideItNeverUnderIt()
    {
        // On top of the pit's two layers, at the middle: it works the top layer flat.
        var left = Cells(Box(0, 5, 0, 1, 0, 5));
        var group = EidolonMining.Next(Pit, 1, left, 3, 2, 3);
        Assert.NotNull(group);
        Assert.False(group!.Upright);
        Assert.Equal(4, group.Cells.Count);
        Assert.All(group.Cells, c => Assert.Equal(1, c.Y));
        Assert.All(group.Cells, c => Assert.False(EidolonMining.UnderFoot(3, 3, c)));
        // The nearest square (ties go to the lowest x, then z).
        Assert.Contains(new MarkPos(0, 1, 2), group.Cells);
    }

    [Fact]
    public void AtAWallWithTwoLayersLeftItStrikesUpright()
    {
        // It stands in a trench on the lower layer; the wall of both layers is at x 4 and 5, x 5 out of reach.
        var left = Cells(Box(4, 5, 0, 1, 0, 5).Concat(Box(0, 3, 0, 0, 0, 5)));
        var group = EidolonMining.Next(Pit, 1, left, 2, 1, 3);
        Assert.NotNull(group);
        Assert.True(group!.Upright);
        Assert.Equal(4, group.Cells.Count);
        Assert.All(group.Cells, c => Assert.Equal(4, c.X));
        Assert.Equal(2, group.Cells.Count(c => c.Y == 1));
        Assert.Equal(2, group.Cells.Count(c => c.Y == 0));
    }

    [Fact]
    public void WhatIsOnlyUnderItIsNotStruckFromWhereItStands()
    {
        // Only the cells it stands on are left of the top layer: nothing to strike from here.
        var left = Cells(Box(2, 3, 1, 1, 2, 3).Concat(Box(0, 5, 0, 0, 0, 5)));
        Assert.Null(EidolonMining.Next(Pit, 1, left, 3, 2, 3));
        var nearest = EidolonMining.Nearest(Pit, Box(2, 3, 1, 1, 2, 3), 1, 3, 3);
        Assert.NotNull(nearest);
        Assert.Equal((2, 2), nearest!.Value.Square);
    }

    // ---- the search ----

    private static readonly MarkArea Quarry = MarkArea.Between(new MarkPos(0, 1, 0), new MarkPos(3, 3, 3));

    [Fact]
    public void ASearchFindsTheHighestLayerItMinesAndTheOneUnder()
    {
        // Stone in layers 1 and 2 everywhere, layer 3 air; a block given up in layer 2.
        var scan = EidolonMining.Scan(Quarry, (_, _) => true, c => c.Y <= 2 ? MineRule.Mine : MineRule.Air,
            c => c == new MarkPos(0, 2, 0));
        Assert.Equal(2, scan.Layer);
        // the block under the one given up holds it up: left too
        Assert.Equal(15 + 15, scan.Left.Count);
        Assert.DoesNotContain(new MarkPos(0, 2, 0), scan.Left);
        Assert.DoesNotContain(new MarkPos(0, 1, 0), scan.Left);
        Assert.Empty(scan.Unloaded);
        Assert.False(scan.Done);
        // Only what it may not break left: done.
        Assert.True(EidolonMining.Scan(Quarry, (_, _) => true, _ => MineRule.LiquidBeside, _ => false).Done);
    }

    [Fact]
    public void WhatItLeavesHoldsUpTheBlocksUnderItDownToAir()
    {
        // A chest on the top layer at (1, 3, 1), stone under it; at (2, 3, 2) a block it leaves over air
        // over stone: the stone under the air is mined.
        var area = MarkArea.Between(new MarkPos(0, 1, 0), new MarkPos(3, 3, 3));
        MineRule Rule(MarkPos c) => (c.X, c.Y, c.Z) switch
        {
            (1, 3, 1) => MineRule.BlockEntity,
            (2, 3, 2) => MineRule.TooHard,
            (2, 2, 2) => MineRule.Air,
            _ => MineRule.Mine,
        };
        var scan = EidolonMining.Scan(area, (_, _) => true, Rule, _ => false);
        Assert.Equal(3, scan.Layer);
        Assert.DoesNotContain(new MarkPos(1, 2, 1), scan.Left);
        Assert.Contains(new MarkPos(1, 2, 2), scan.Left);
        // the top layer done, the next search: the column under the chest still held
        bool Gone(MarkPos c) => c.Y == 3 && c != new MarkPos(1, 3, 1) && c != new MarkPos(2, 3, 2);
        scan = EidolonMining.Scan(area, (_, _) => true, c => Gone(c) ? MineRule.Air : Rule(c), _ => false);
        Assert.Equal(2, scan.Layer);
        Assert.DoesNotContain(new MarkPos(1, 2, 1), scan.Left);
        Assert.DoesNotContain(new MarkPos(1, 1, 1), scan.Left);
        Assert.Contains(new MarkPos(2, 1, 2), scan.Left);
        Assert.Equal(16 - 2 + 16 - 1, scan.Left.Count);
    }

    [Fact]
    public void EveryBlockEntityIsLeftButTheGroundItMinesThrough()
    {
        var through = EidolonConfig.Defaults.MineThroughBlockEntities;
        Assert.Equal(EidolonMining.GroundBlockEntities, through);
        // Desire Paths' worn soil, whole and as a slab: mined
        Assert.False(EidolonMining.LeavesBlockEntity("desirepaths:soilpath-low-5", through));
        Assert.False(EidolonMining.LeavesBlockEntity("desirepaths:soilpathslab-forest-0-down", through));
        // a chest, farmland, chiseled stone (stone to the game), a berry bush, a door of stone: left
        Assert.True(EidolonMining.LeavesBlockEntity("game:chest-east", through));
        Assert.True(EidolonMining.LeavesBlockEntity("game:farmland-dry-medium", through));
        Assert.True(EidolonMining.LeavesBlockEntity("game:chiseledblock", through));
        Assert.True(EidolonMining.LeavesBlockEntity("game:bigberrybush-blueberry-ripe", through));
        Assert.True(EidolonMining.LeavesBlockEntity("doorvariants:cobbledoor-granite", through));
        // and the rule leaves what it is told to
        Assert.Equal(MineRule.BlockEntity, EidolonMining.Rule(Stone with { HasBlockEntity = true }));
    }

    [Fact]
    public void CodesMatchWithWildcardsAndTheGameIsTheDefaultDomain()
    {
        Assert.True(EidolonMining.CodeMatches("desirepaths:soilpath-*", "desirepaths:soilpath-low-5"));
        Assert.False(EidolonMining.CodeMatches("desirepaths:soilpath-*", "desirepaths:soilpathslab-low-5"));
        Assert.True(EidolonMining.CodeMatches("soil-*-none", "game:soil-medium-none"));
        Assert.False(EidolonMining.CodeMatches("soil-*-none", "game:soil-medium-normal"));
        Assert.True(EidolonMining.CodeMatches("GAME:Chest-*", "game:chest-east"));
        Assert.True(EidolonMining.CodeMatches("game:chest-east", "game:chest-east"));
        Assert.False(EidolonMining.CodeMatches("game:chest", "game:chest-east"));
        Assert.True(EidolonMining.CodeMatches("*:*", "anything:at-all"));
        Assert.False(EidolonMining.CodeMatches("*", "anything:at-all"));
        Assert.True(EidolonMining.CodeMatches("a:*b*c", "a:xbyc"));
        Assert.False(EidolonMining.CodeMatches("a:*b*c", "a:xcyb"));
    }

    [Fact]
    public void ABlockLeftForALiquidBesideItHoldsNothingUpUnlessItFalls()
    {
        // A pit three deep (layers 1 to 3) with water beside (2, 2, 0), just outside it: that block is left,
        // and the one under it, which touches no liquid, is mined.
        var area = MarkArea.Between(new MarkPos(0, 1, 0), new MarkPos(3, 3, 3));
        MineRule Rule(MarkPos c) => c == new MarkPos(2, 2, 0) ? MineRule.LiquidBeside : MineRule.Mine;
        bool Gone(MarkPos c) => c.Y == 3;
        var scan = EidolonMining.Scan(area, (_, _) => true, c => Gone(c) ? MineRule.Air : Rule(c), _ => false);
        Assert.Equal(2, scan.Layer);
        Assert.DoesNotContain(new MarkPos(2, 2, 0), scan.Left);
        Assert.Contains(new MarkPos(2, 1, 0), scan.Left);
        Assert.Equal(15 + 16, scan.Left.Count);
        // Sand or gravel by the water would fall into the hole and open the pit onto it: it holds its column.
        Assert.Equal(MineRule.LiquidBesideFalls, EidolonMining.Rule(Stone with { LiquidBeside = true, Falls = true }));
        Assert.Equal(MineRule.LiquidBeside, EidolonMining.Rule(Stone with { LiquidBeside = true }));
        scan = EidolonMining.Scan(area, (_, _) => true,
            c => Gone(c) ? MineRule.Air : c == new MarkPos(2, 2, 0) ? MineRule.LiquidBesideFalls : MineRule.Mine, _ => false);
        Assert.DoesNotContain(new MarkPos(2, 1, 0), scan.Left);
        Assert.True(EidolonMining.Holds(MineRule.LiquidBesideFalls));
        // Left in a column already held (under a chest), it stays held.
        scan = EidolonMining.Scan(area, (_, _) => true, c => (c.X, c.Y, c.Z) switch
        {
            (2, 3, 0) => MineRule.BlockEntity,
            (2, 2, 0) => MineRule.LiquidBeside,
            _ => MineRule.Mine,
        }, _ => false);
        Assert.DoesNotContain(new MarkPos(2, 1, 0), scan.Left);
    }

    [Fact]
    public void ABlockGivenUpIsPassedOverAndWhatIsUnderItStillMined()
    {
        // (1, 3, 1) given up for want of a way: not held up, so (1, 2, 1) is still to mine
        var scan = EidolonMining.Scan(Quarry, (_, _) => true, _ => MineRule.Mine, _ => false, c => c == new MarkPos(1, 3, 1));
        Assert.Equal(3, scan.Layer);
        Assert.DoesNotContain(new MarkPos(1, 3, 1), scan.Left);
        Assert.Contains(new MarkPos(1, 2, 1), scan.Left);
        Assert.Equal(15 + 16, scan.Left.Count);
    }

    [Fact]
    public void ABlockItLeavesJustOverTheAreaHoldsUpTheColumnUnderIt()
    {
        // A chest on the surface over (2, 3, 2), a liquid over (0, 3, 0): only the chest holds its column
        MineRule Rule(MarkPos c) => (c.X, c.Y, c.Z) switch
        {
            (2, 4, 2) => MineRule.BlockEntity,
            (0, 4, 0) => MineRule.Liquid,
            (_, 4, _) => MineRule.Air,
            _ => MineRule.Mine,
        };
        var scan = EidolonMining.Scan(Quarry, (_, _) => true, Rule, _ => false);
        Assert.Equal(3, scan.Layer);
        Assert.DoesNotContain(new MarkPos(2, 3, 2), scan.Left);
        Assert.DoesNotContain(new MarkPos(2, 2, 2), scan.Left);
        Assert.Contains(new MarkPos(0, 3, 0), scan.Left);
        Assert.True(EidolonMining.Holds(MineRule.TooHard));
        Assert.True(EidolonMining.Holds(MineRule.Claimed));
        Assert.False(EidolonMining.Holds(MineRule.LiquidBeside));
        Assert.False(EidolonMining.Holds(MineRule.Mine));
    }

    [Fact]
    public void AnUnloadedColumnReadsAsAirButIsNeverDone()
    {
        // Columns x 2 and 3 unloaded: read as air, they are not searched, and the area is not done
        // with nothing left in the loaded half.
        bool Loaded(int x, int z) => x < 2;
        var scan = EidolonMining.Scan(Quarry, Loaded, c => c.X < 2 ? MineRule.Air : MineRule.Mine, _ => false);
        Assert.Null(scan.Layer);
        Assert.Equal(8, scan.Unloaded.Count);
        Assert.All(scan.Unloaded, c => Assert.True(c.X >= 2));
        Assert.False(scan.Done);
        // What is left in the loaded half is worked first.
        scan = EidolonMining.Scan(Quarry, Loaded, c => c.Y == 1 ? MineRule.Mine : MineRule.Air, _ => false);
        Assert.Equal(1, scan.Layer);
        Assert.All(scan.Left, c => Assert.True(c.X < 2));
        Assert.False(scan.Done);
        // The nearest unloaded column, to walk toward.
        Assert.Equal((2, 0), EidolonMining.NearestColumn(scan.Unloaded, 0.5, 0.5));
        Assert.Null(EidolonMining.NearestColumn([], 0, 0));
    }

    [Fact]
    public void SquaresComeNearestFirst()
    {
        var squares = EidolonMining.Squares(Pit, Box(0, 5, 1, 1, 0, 5), 1, 5.5, 5.5).Select(s => s.Square).ToList();
        Assert.Equal(9, squares.Count);
        Assert.Equal((4, 4), squares[0]);
        Assert.Equal((0, 0), squares[^1]);
    }

    // ---- where it stands ----

    private sealed class Voxels(IEnumerable<(int, int, int)> solid) : IWideSpace
    {
        private readonly HashSet<(int, int, int)> _solid = solid.ToHashSet();

        public bool Free(double x, double y, double z)
        {
            const double eps = 1e-6;
            for (int bx = (int)Math.Floor(x - 0.85); bx <= (int)Math.Floor(x + 0.85 - eps); bx++)
            for (int by = (int)Math.Floor(y); by <= (int)Math.Floor(y + 3.75 - eps); by++)
            for (int bz = (int)Math.Floor(z - 0.85); bz <= (int)Math.Floor(z + 0.85 - eps); bz++)
                if (_solid.Contains((bx, by, bz)))
                    return false;
            return true;
        }

        public float Cost(PathCell cell) => 0;
    }

    [Fact]
    public void ItStandsWhereItReachesTheSquareOnTheGroundAndOffWhatIsLeft()
    {
        // A floor at y -1, and the pit's top layer solid over a 6 × 6 platform.
        var platform = Box(0, 5, 0, 0, 0, 5).ToList();
        var space = new Voxels(Box(-8, 13, -1, -1, -8, 13).Concat(platform).Select(c => (c.X, c.Y, c.Z)));
        var left = Cells(platform);
        var square = Box(0, 1, 0, 0, 0, 1).ToList();
        var stands = EidolonMining.Stands(space, square, left, 3, 1, 3);
        Assert.NotEmpty(stands);
        foreach (var s in stands)
        {
            Assert.True(space.Free(s.X, s.Y, s.Z));
            Assert.False(space.Free(s.X, s.Y - 1, s.Z));
            Assert.All(square, c => Assert.True(EidolonMining.InReach(s.X, s.Y, s.Z, c), $"{s} {c}"));
        }
        // The first stands on none of what is left to mine: beside the platform, on the floor.
        var first = stands[0];
        Assert.Equal(0, first.Y);
        Assert.DoesNotContain(Box(first.X - 1, first.X, -1, -1, first.Z - 1, first.Z), c => left(c with { Y = 0 }));
        // Standing on the platform is a later choice, never over the square itself.
        Assert.Contains(stands, s => s.Y == 1);
        Assert.DoesNotContain(stands, s => square.Any(c => EidolonMining.UnderFoot(s.X, s.Z, c)));
    }

    [Fact]
    public void ItStandsInItsOwnSectorNotOverAnothers()
    {
        // A floor at y -1; the square at the west edge of its sector (x 4 to 7), another's sector west of
        // it (x 0 to 3), coming from the west: the nearest stands are over the other's columns, but the
        // first is wholly in its own, its box clear of the other's sector by the margin.
        var space = new Voxels(Box(-8, 13, -1, -1, -8, 13).Select(c => (c.X, c.Y, c.Z)));
        var square = Box(4, 5, 0, 0, 0, 1).ToList();
        var near = EidolonMining.Stands(space, square, _ => false, 0, 0, 1);
        Assert.True(near[0].X <= 4);
        var stands = EidolonMining.Stands(space, square, _ => false, 0, 0, 1, (x, _) => x <= 3);
        Assert.Equal(near.Count, stands.Count);
        var first = stands[0];
        Assert.True(first.X - EidolonMining.HalfWidth >= 4 + Others.Margin / 2, $"{first}");
        Assert.All(square, c => Assert.True(EidolonMining.InReach(first.X, first.Y, first.Z, c), $"{first} {c}"));
    }

    [Fact]
    public void NoGroundNoStand()
    {
        var square = Box(0, 1, 0, 0, 0, 1).ToList();
        Assert.Empty(EidolonMining.Stands(new Voxels(square.Select(c => (c.X, c.Y, c.Z))), square, _ => false, 0, 0, 0));
    }

    // ---- oil ----

    [Fact]
    public void AStrikeCostsHalfAPointOfOil()
    {
        var c = new EidolonConfig();
        Assert.Equal(0.5, EidolonOil.Cost(c, EidolonJob.MiningStrike));
        c.OilPerMiningStrike = -1;
        Assert.Contains(c.Sanitise(), f => f.StartsWith("OilPerMiningStrike", StringComparison.Ordinal));
        Assert.Equal(0.5, c.OilPerMiningStrike);
    }
}
