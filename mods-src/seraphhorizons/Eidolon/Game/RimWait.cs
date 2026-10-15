using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Waiting at the rim of a shared job's area (README "Eidolon", working together), for the orders that
/// work one (<see cref="MineOrder"/>, <see cref="FellOrder"/>): a member with nothing it may work for now
/// walks to a place on the area's rim (<see cref="EidolonTeamwork.Rim"/>) with ground under it, away from
/// the other eidolons and the places the others wait at, which it notes with the job system
/// (<see cref="EidolonJobSystem.SetRim"/>), and keeps only while none stands by it
/// (<see cref="EidolonTeamwork.KeepsPlace"/>). One that finds no way to the rim (deeper in a pit than it
/// climbs, say) steps aside instead, to the nearest place round it the order says is clear of the job's
/// work (mining: columns with nothing left and held by no one; felling: away from every tree held), and
/// says so rather than that it waits at the edge. One per order.
/// </summary>
/// <param name="area">The job's area.</param>
/// <param name="heightSlack">How far below its lower corner and above its higher one it looks for
/// ground (felling's stumps stand up to <see cref="EidolonFelling.AreaHeightSlack"/> outside it).</param>
/// <param name="keepClear">Whether it never sets off on a way that goes through another eidolon, found no
/// way round it (mining, in a pit too narrow to pass one at work: it waits where it is, and tries again).</param>
internal sealed class RimWait(MarkArea area, int heightSlack = 0, bool keepClear = false)
{
    /// <summary>Places on the rim it searches a path to before it gives up for now.</summary>
    private const int PathTries = 3;

    /// <summary>Places on the rim it looks for ground at before it steps aside.</summary>
    private const int RimLooks = 16;

    /// <summary>How far round itself, in path nodes, it looks for a place to step aside to.</summary>
    private const int AsideReach = 6;

    /// <summary>How far up or down from its feet it looks for ground to step aside to: a climb.</summary>
    private const int AsideRise = 2;

    private (double X, double Z)? _place;
    private bool _aside;
    private bool _settled;
    private double _retryAt;

    /// <summary>
    /// Waits: unless it has a place on the rim or is walking, sets off for one; finding no way to any,
    /// it steps aside to a place round it where <paramref name="clear"/> holds (a path node, its box over
    /// the two columns each side) and tries the rim again <paramref name="retrySeconds"/> later. A place
    /// it has is kept only while <see cref="EidolonTeamwork.KeepsPlace"/> holds (another come to stand by
    /// it, or its walk ended away from it: it chooses again); once it stands there, its place is where it
    /// stands. Its status is <paramref name="status"/> while it waits at the rim or makes for it, else
    /// <paramref name="asideStatus"/> (set only when it changes: it waits here every second).
    /// </summary>
    public void Wait(EntityLaborEidolon eidolon, EidolonNavigator nav, EidolonJobSystem? jobs, string status, string asideStatus,
        Func<int, int, bool> clear, double now, double retrySeconds)
    {
        if (_place is { } place)
        {
            bool walking = nav.Active;
            var taken = Standing(eidolon, jobs, eidolon.Pos.XYZ, AsideReach + 4, AsideReach + 4);
            if (!EidolonTeamwork.KeepsPlace(place, (eidolon.Pos.X, eidolon.Pos.Z), walking, taken))
            {
                Drop(jobs, eidolon);
                if (walking)
                    nav.Stop();
                _retryAt = now;
            }
            else if (!walking && !_settled)
            {
                // there: its place is where it stands, which the others keep clear of
                _settled = true;
                _place = (eidolon.Pos.X, eidolon.Pos.Z);
                jobs?.SetRim(eidolon.EntityId, _place);
            }
        }
        bool rim = _place != null && !_aside;
        if (!rim && !nav.Active && now >= _retryAt && !ToRim(eidolon, nav, jobs))
        {
            _retryAt = now + retrySeconds;
            if (_place == null)
                StepAside(eidolon, nav, jobs, clear);
        }
        string shown = _place != null && !_aside ? status : asideStatus;
        if (eidolon.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") != shown)
            eidolon.Orders?.SetStatus(shown);
    }

    // Where the other eidolons near it stand and the others wait.
    private static List<(double X, double Z)> Taken(EntityLaborEidolon eidolon, EidolonJobSystem? jobs, Vec3d around, float range, float height) =>
        Others(eidolon, jobs, around, range, height, _ => true);

    // Where the other eidolons near it stand still (not walking past) and the others wait.
    private static List<(double X, double Z)> Standing(EntityLaborEidolon eidolon, EidolonJobSystem? jobs, Vec3d around, float range, float height) =>
        Others(eidolon, jobs, around, range, height,
            e => e is EntityLaborEidolon { ClimbTop: not null } || Math.Sqrt(e.Pos.Motion.X * e.Pos.Motion.X + e.Pos.Motion.Z * e.Pos.Motion.Z) < EidolonNavigator.StandingMotion);

    private static List<(double X, double Z)> Others(EntityLaborEidolon eidolon, EidolonJobSystem? jobs, Vec3d around, float range, float height,
        Func<Entity, bool> which) =>
        eidolon.World.GetEntitiesAround(around, range, height, e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive && which(e))
            .Select(e => (e.Pos.X, e.Pos.Z))
            .Concat(jobs?.RimsBut(eidolon.EntityId) ?? []).ToList();

    // Claims a place to wait at, walking to it.
    private void Claim(EntityLaborEidolon eidolon, EidolonJobSystem? jobs, (double X, double Z) place, bool aside)
    {
        _place = place;
        _aside = aside;
        _settled = false;
        jobs?.SetRim(eidolon.EntityId, place);
    }

    private void Drop(EidolonJobSystem? jobs, EntityLaborEidolon eidolon)
    {
        _place = null;
        _aside = false;
        _settled = false;
        jobs?.SetRim(eidolon.EntityId, null);
    }

    /// <summary>Sets off for the nearest place round it, within <see cref="AsideReach"/>, that is
    /// <paramref name="clear"/>, on the ground a climb or a drop at most from its feet, and
    /// <see cref="EidolonTeamwork.Apart"/> from every other eidolon and where one waits or makes for; false
    /// when it finds no way to one (it waits where it is).</summary>
    private bool StepAside(EntityLaborEidolon eidolon, EidolonNavigator nav, EidolonJobSystem? jobs, Func<int, int, bool> clear)
    {
        var blocks = eidolon.World.BlockAccessor;
        var space = new GameWideSpace(blocks, eidolon.CollisionBox);
        double ex = eidolon.Pos.X, ez = eidolon.Pos.Z;
        int feet = (int)Math.Floor(eidolon.Pos.Y + 0.01), nx = (int)Math.Round(ex), nz = (int)Math.Round(ez);
        var others = Taken(eidolon, jobs, eidolon.Pos.XYZ, AsideReach + 4, AsideReach + 4);
        var places = new List<(int X, int Z, double From)>();
        for (int x = nx - AsideReach; x <= nx + AsideReach; x++)
        for (int z = nz - AsideReach; z <= nz + AsideReach; z++)
        {
            if (!EidolonTeamwork.Apart(x, z, others) || !clear(x, z))
                continue;
            places.Add((x, z, (x - ex) * (x - ex) + (z - ez) * (z - ez)));
        }
        int tries = 0;
        foreach (var (x, z, _) in places.OrderBy(p => p.From).ThenBy(p => p.X).ThenBy(p => p.Z))
        {
            int? ground = null;
            for (int y = feet + AsideRise; y >= Math.Max(1, feet - AsideRise); y--)
                if (space.Free(x, y, z) && !space.Free(x, y - 1, z))
                {
                    ground = y;
                    break;
                }
            if (ground is not { } at)
                continue;
            // a way through another's work (a miner's claimed columns) is not taken: the next place is tried
            if (Crosses?.Invoke(new Vec3d(x, at, z)) == true)
            {
                if (++tries >= PathTries)
                    break;
                continue;
            }
            if (nav.GoTo(new Vec3d(x, at, z), false, () => { }, () => { }, arriveWithin: (float)EidolonTeamwork.WaitArrive))
            {
                if (keepClear && !nav.LastWentAround)
                    nav.Stop();
                else
                {
                    Claim(eidolon, jobs, (x, z), aside: true);
                    return true;
                }
            }
            if (++tries >= PathTries)
                break;
        }
        return false;
    }

    /// <summary>Sets off for a place on the area's rim with ground under it, away from the other
    /// eidolons and the places the others wait at or make for; false when it finds no way to one.</summary>
    private bool ToRim(EntityLaborEidolon eidolon, EidolonNavigator nav, EidolonJobSystem? jobs)
    {
        var blocks = eidolon.World.BlockAccessor;
        var space = new GameWideSpace(blocks, eidolon.CollisionBox);
        var pos = new BlockPos(eidolon.Pos.Dimension);
        var centre = new Vec3d((area.Min.X + area.Max.X + 1) / 2.0, (area.Min.Y + area.Max.Y) / 2.0, (area.Min.Z + area.Max.Z + 1) / 2.0);
        float range = area.LongestSide / 2f + EidolonTeamwork.RimGap + 4;
        var taken = Taken(eidolon, jobs, centre, range, Math.Max(range, area.SizeY + 2 * heightSlack + 8));
        int looked = 0, tries = 0;
        foreach (var (x, z) in EidolonTeamwork.Rim(area, eidolon.Pos.X, eidolon.Pos.Z, taken))
        {
            if (++looked > RimLooks)
                break;
            // the highest ground it may stand on there, from over the area's top to its bottom
            int? ground = null;
            for (int y = area.Max.Y + 3 + heightSlack; y >= Math.Max(1, area.Min.Y - heightSlack); y--)
            {
                if (blocks.GetChunkAtBlockPos(pos.Set(x - 1, y, z - 1)) == null || blocks.GetChunkAtBlockPos(pos.Set(x, y, z)) == null)
                    break;
                if (space.Free(x, y, z) && !space.Free(x, y - 1, z))
                {
                    ground = y;
                    break;
                }
            }
            if (ground is not { } feet)
                continue;
            // a way through another's work (a miner's claimed columns) is not taken: the next place is tried
            if (Crosses?.Invoke(new Vec3d(x, feet, z)) == true)
            {
                if (++tries >= PathTries)
                    break;
                continue;
            }
            if (nav.GoTo(new Vec3d(x, feet, z), false, () => { }, () => { }, arriveWithin: (float)EidolonTeamwork.WaitArrive))
            {
                if (keepClear && !nav.LastWentAround)
                    nav.Stop();
                else
                {
                    Claim(eidolon, jobs, (x, z), aside: false);
                    return true;
                }
            }
            if (++tries >= PathTries)
                break;
        }
        return false;
    }

    /// <summary>Steps aside, as when it finds no way to the rim, to the nearest place round it that is
    /// <paramref name="clear"/> (mining: off the stair while another walks it); false when it is walking
    /// already or finds none.</summary>
    public bool Aside(EntityLaborEidolon eidolon, EidolonNavigator nav, EidolonJobSystem? jobs, Func<int, int, bool> clear) =>
        !nav.Active && StepAside(eidolon, nav, jobs, clear);

    /// <summary>Whether the way to a place crosses another's work (mining: columns another member has
    /// claimed); such a place is passed over. None: any way goes.</summary>
    public Func<Vec3d, bool>? Crosses { get; set; }

    /// <summary>Off the rim, back to work (or stopped, or done).</summary>
    public void Leave(EntityLaborEidolon eidolon, EidolonJobSystem? jobs)
    {
        if (_place == null)
            return;
        Drop(jobs, eidolon);
    }
}
