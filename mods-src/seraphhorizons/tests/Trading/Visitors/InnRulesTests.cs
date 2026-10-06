using SeraphHorizons.Mod.Trading.Visitors.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Visitors;

/// <summary>A synthetic block grid: everything not set is open air.</summary>
internal sealed class Grid : IInnArea
{
    private readonly Dictionary<InnPos, (InnCell Cell, int Light)> _cells = new();

    public InnCell At(InnPos p) => _cells.TryGetValue(p, out var c) ? c.Cell : InnCell.Open;
    public int LightAt(InnPos p) => _cells.TryGetValue(p, out var c) ? c.Light : 0;

    public Grid Set(int x, int y, int z, InnCell cell, int light = 0)
    {
        _cells[new InnPos(x, y, z)] = (cell, light);
        return this;
    }

    public Grid Clear(int x, int y, int z)
    {
        _cells.Remove(new InnPos(x, y, z));
        return this;
    }

    /// <summary>A 5×5 room inside x, z in -3..3: a floor at y 0, walls up to y 3, a roof at y 4;
    /// an inn sign at (0,1,0), a bed at (2,1,2), a table at (-2,1,2) with a crock on it, a torch on
    /// the wall at (0,2,-2). Every rule passes.</summary>
    public static Grid Inn()
    {
        var g = new Grid();
        for (int x = -3; x <= 3; x++)
        for (int z = -3; z <= 3; z++)
        {
            g.Set(x, 0, z, InnCell.Solid);
            g.Set(x, 4, z, InnCell.Solid);
            if (Math.Abs(x) == 3 || Math.Abs(z) == 3)
                for (int y = 1; y <= 3; y++) g.Set(x, y, z, InnCell.Solid);
        }
        g.Set(0, 1, 0, InnCell.Stall);
        g.Set(2, 1, 2, InnCell.Solid | InnCell.Bed);
        g.Set(-2, 1, 2, InnCell.Solid | InnCell.Table);
        g.Set(-2, 2, 2, InnCell.Solid | InnCell.Food);
        g.Set(0, 2, -2, InnCell.Open, 14);
        return g;
    }
}

public class InnRulesTests
{
    private static readonly InnPos Flag = new(1, 1, 1);

    private static InnReport Check(Grid g, params InnPos[] stalls) => InnRules.Evaluate(g, Flag, stalls);

    [Fact]
    public void A_complete_inn_passes_every_rule_and_has_a_floor_cell_for_the_visitor()
    {
        var r = Check(Grid.Inn());
        Assert.True(r.Passed, string.Join("; ", r.Checks.Where(c => !c.Passed)));
        Assert.Equal(new InnPos(0, 1, 0), r.Stall);
        Assert.Equal(new InnPos(2, 1, 2), r[InnRule.Bed].At);
        Assert.Equal(new InnPos(-2, 1, 2), r[InnRule.Table].At);
        Assert.Equal(3, r[InnRule.Roof].Value);
        Assert.Equal(14 - 3, r[InnRule.Light].Value); // the torch is 3 blocks from the stall's cell
        var spawn = Assert.NotNull(r.SpawnAt);
        Assert.NotEqual(r.Stall, spawn);
        Assert.Equal(1, spawn.Y);
        Assert.True(r.RoomCells > 20);
    }

    [Fact]
    public void A_hole_in_a_wall_fails_the_walls_and_says_where_but_still_checks_the_rest()
    {
        var r = Check(Grid.Inn().Clear(3, 1, 0));
        Assert.Equal([InnRule.Walls], r.Failed);
        Assert.Equal("open", r[InnRule.Walls].Reason);
        var leak = Assert.NotNull(r[InnRule.Walls].At);
        Assert.True(leak.Manhattan(new InnPos(3, 1, 0)) <= 14);
    }

    [Fact]
    public void No_roof_fails_the_roof_and_lets_the_room_out_upwards()
    {
        var g = Grid.Inn();
        for (int x = -3; x <= 3; x++)
        for (int z = -3; z <= 3; z++)
            g.Clear(x, 4, z);
        var r = Check(g);
        Assert.Contains(InnRule.Roof, r.Failed);
        Assert.Contains(InnRule.Walls, r.Failed);
        Assert.Equal("noroof", r[InnRule.Roof].Reason);
    }

    [Fact]
    public void A_door_is_a_wall_open_or_shut()
    {
        var g = Grid.Inn().Set(3, 1, 0, InnCell.Door).Set(3, 2, 0, InnCell.Door);
        Assert.True(Check(g).Passed);
    }

    [Fact]
    public void A_table_needs_food_on_or_beside_it_and_the_room_needs_a_table()
    {
        var bare = Check(Grid.Inn().Clear(-2, 2, 2));
        Assert.Equal([InnRule.Table], bare.Failed);
        Assert.Equal("nofood", bare[InnRule.Table].Reason);

        // Food beside the table counts as well as on it.
        Assert.True(Check(Grid.Inn().Clear(-2, 2, 2).Set(-1, 1, 2, InnCell.Solid | InnCell.Food)).Passed);

        var none = Check(Grid.Inn().Clear(-2, 1, 2));
        Assert.Equal("notable", none[InnRule.Table].Reason);
    }

    [Fact]
    public void Lamps_light_the_stall_by_distance_and_a_dark_stall_fails()
    {
        var dark = Check(Grid.Inn().Clear(0, 2, -2));
        Assert.Equal([InnRule.Light], dark.Failed);
        Assert.Equal(0, dark[InnRule.Light].Value);

        // A weak lamp in the far corner is not enough (a candle: 4, five blocks off).
        var dim = Check(Grid.Inn().Clear(0, 2, -2).Set(-2, 1, -2, InnCell.Open, 4));
        Assert.False(dim[InnRule.Light].Passed);

        // A lamp outside the room does not light it, however bright.
        var outside = Check(Grid.Inn().Clear(0, 2, -2).Set(0, 2, -5, InnCell.Open, 31));
        Assert.False(outside[InnRule.Light].Passed);
    }

    [Fact]
    public void Furniture_outside_the_room_does_not_count()
    {
        var g = Grid.Inn().Clear(2, 1, 2).Set(2, 1, 5, InnCell.Solid | InnCell.Bed);
        var r = Check(g);
        Assert.Equal([InnRule.Bed], r.Failed);
        Assert.Equal("nobed", r[InnRule.Bed].Reason);
    }

    [Fact]
    public void Without_a_stall_the_report_says_so_and_a_market_stall_entity_counts()
    {
        var g = Grid.Inn().Clear(0, 1, 0);
        var none = Check(g);
        Assert.Equal([InnRule.Stall], none.Failed);
        Assert.Equal("nostall", none[InnRule.Stall].Reason);
        Assert.Null(none.Stall);

        var stall = Check(g, new InnPos(-1, 1, -1));
        Assert.True(stall.Passed);
        Assert.Equal(new InnPos(-1, 1, -1), stall.Stall);

        // Too far from the flag.
        Assert.False(Check(g, new InnPos(40, 1, 0))[InnRule.Stall].Passed);
    }

    [Fact]
    public void The_nearest_stall_to_the_flag_is_the_inns()
    {
        var r = Check(Grid.Inn(), new InnPos(1, 1, 2), new InnPos(-2, 1, -2));
        Assert.Equal(new InnPos(1, 1, 2), r.Stall);
    }

    [Fact]
    public void A_stall_walled_in_on_every_side_has_no_room()
    {
        var g = new Grid().Set(0, 1, 0, InnCell.Stall | InnCell.Solid);
        foreach (var f in InnPos.Faces) g.Set(f.X, 1 + f.Y, f.Z, InnCell.Solid);
        var r = InnRules.Evaluate(g, new InnPos(0, 1, 0), []);
        Assert.Equal("noroom", r[InnRule.Walls].Reason);
        Assert.Equal([InnRule.Roof, InnRule.Walls, InnRule.Bed, InnRule.Table, InnRule.Light], r.Failed);
    }
}
