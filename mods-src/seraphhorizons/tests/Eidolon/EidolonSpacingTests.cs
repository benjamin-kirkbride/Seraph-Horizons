using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests;

public class EidolonSpacingTests
{
    private const double Reach = 1.7;

    [Fact]
    public void Apart_or_just_touching_there_is_no_push()
    {
        Assert.Equal((0.0, 0.0), EidolonSpacing.Push(2, 0, Reach, 1, 2));
        Assert.Equal((0.0, 0.0), EidolonSpacing.Push(1.25, 1.25, Reach, 1, 2));
        Assert.Equal((0.0, 0.0), EidolonSpacing.Push(Reach, 0, Reach, 1, 2));
    }

    [Fact]
    public void Overlapping_it_is_pushed_straight_away_from_the_other_harder_the_deeper()
    {
        var (x, z) = EidolonSpacing.Push(1.5, 0, Reach, 1, 2);
        Assert.True(x < 0);
        Assert.Equal(0, z, 9);
        Assert.Equal(-0.2 * EidolonSpacing.Stiffness, x, 9);
        var (dx, dz) = EidolonSpacing.Push(-0.6, -0.8, Reach, 1, 2);   // 1 away, diagonally
        Assert.Equal(0.6 * 0.7 * EidolonSpacing.Stiffness, dx, 9);
        Assert.Equal(0.8 * 0.7 * EidolonSpacing.Stiffness, dz, 9);
        // deep in, capped
        var (cx, cz) = EidolonSpacing.Push(0.1, 0, Reach, 1, 2);
        Assert.Equal(-EidolonSpacing.MaxPush, cx, 9);
        Assert.Equal(0, cz, 9);
    }

    [Fact]
    public void Two_in_one_place_go_opposite_ways()
    {
        var a = EidolonSpacing.Push(0, 0, Reach, 7, 12);
        var b = EidolonSpacing.Push(0, 0, Reach, 12, 7);
        Assert.Equal(EidolonSpacing.MaxPush, Math.Sqrt(a.X * a.X + a.Z * a.Z), 9);
        Assert.Equal(-a.X, b.X, 9);
        Assert.Equal(-a.Z, b.Z, 9);
        // itself is never pushed
        Assert.Equal((0.0, 0.0), EidolonSpacing.Push(0, 0, Reach, 7, 7));
    }

    [Fact]
    public void Pushed_apart_tick_by_tick_two_in_one_place_come_clear()
    {
        // Each moves by its own push (motion taken as the move, no friction): the two part.
        double ax = 0, az = 0, bx = 0, bz = 0;
        int ticks = 0;
        double Apart() => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
        while (Apart() < Reach - 0.01 && ticks < 200)
        {
            var (pax, paz) = EidolonSpacing.Push(bx - ax, bz - az, Reach, 3, 4);
            var (pbx, pbz) = EidolonSpacing.Push(ax - bx, az - bz, Reach, 4, 3);
            (ax, az, bx, bz) = (ax + pax, az + paz, bx + pbx, bz + pbz);
            ticks++;
        }
        Assert.InRange(ticks, 10, 100);
    }
}
