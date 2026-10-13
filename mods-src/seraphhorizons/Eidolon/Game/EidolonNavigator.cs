using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Essentials;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The world as the eidolon's pathfinder sees it: its collision box against the blocks (the game's
/// collision tester, as the game's own pathfinder uses it), and the traversal cost of the blocks
/// under and around its 2 × 2 footprint at its feet.
/// </summary>
public sealed class GameWideSpace(IBlockAccessor blocks, Cuboidf box, EnumAICreatureType creatureType = EnumAICreatureType.Default) : IWideSpace
{
    private readonly CollisionTester _tester = new();
    private readonly Vec3d _at = new();
    private readonly BlockPos _pos = new(0);

    public bool Free(double x, double y, double z) => !_tester.IsColliding(blocks, box, _at.Set(x, y, z), false);

    public float Cost(PathCell cell)
    {
        float cost = 0;
        for (int dx = -1; dx <= 0; dx++)
        for (int dz = -1; dz <= 0; dz++)
        {
            _pos.Set(cell.X + dx, cell.Y, cell.Z + dz);
            float feet = blocks.GetBlock(_pos, BlockLayersAccess.Fluid).GetTraversalCost(_pos, creatureType)
                         + Math.Max(0, blocks.GetBlock(_pos, BlockLayersAccess.Solid).GetTraversalCost(_pos, creatureType));
            if (feet > WidePath.Impassable)
                return feet;
            cost += Math.Max(0, feet);
        }
        return cost / 4;
    }
}

/// <summary>
/// Walks the eidolon along a path from <see cref="WidePath"/> (Eidolon/README.md, "Pathfinding"),
/// driving the task AI's own waypoint traverser (so its steering, turning and stuck detection are the
/// game's) with the waypoints at the path's block corners. The search runs on the server's thread,
/// bounded by <see cref="EidolonConfig.PathSearchNodes"/>; callers repath at most every few seconds.
/// A step of two up on the path is climbed (<see cref="EidolonClimb"/>): the traverser walks it to the
/// foot, the climb moves it up and onto the step after every physics tick while the shape plays
/// <c>climb</c>, and the traverser walks on from the top.
///
/// <para>Other eidolons are in its way (README "Eidolon", pathfinding): a search goes round each
/// (<see cref="WidePath.FindAround"/>), one standing where it stands, one walking where it goes over the
/// next <see cref="SweepSeconds"/> too; but one walking with a higher id is left out, since it gives way:
/// so of two walking at each other only one turns aside, and they never both turn the same way. While
/// it walks it looks along its path ahead every <see cref="WatchMs"/>; one there, it searches again
/// (not while it climbs). With no way round, it walks through, unless the one in its way has the lower
/// id: then it waits for it, at most <see cref="YieldMs"/>, so two meeting in a narrow way never both
/// stand. One climbing is moved with no motion, so it counts as standing; where its climb lands
/// (<see cref="EntityLaborEidolon.ClimbTop"/>) is kept clear too. At the foot of a climb it waits while
/// another eidolon is where it would land, or lands there, or walks over it within the climb's time, at
/// most <see cref="YieldMs"/>: the climb, once begun, goes on to the top whatever is there; but not for
/// one with a higher id waiting at a climb's foot itself (<see cref="EntityLaborEidolon.ClimbWaiting"/>),
/// which waits for it. And one
/// walking with a lower id that would meet it within <see cref="GiveWaySeconds"/>, each going on as it
/// goes now, it stops for, while that one would still meet it where it stands (<see cref="YieldMs"/> at
/// most), then searches again: the one with the lower id does not turn aside for it, and once it stands
/// that one goes round it. But where it would be met standing too (ahead of that one on its way, or where
/// that one's path ends, <see cref="EntityLaborEidolon.WalkGoal"/>), it searches again at once instead,
/// going round where that one goes, and stops only with no way round. Where a walker with a lower id
/// stops is kept clear as where it stands.</para>
/// </summary>
public sealed class EidolonNavigator(EntityLaborEidolon eidolon)
{
    /// <summary>How near the foot of a climb the walk there counts as there; the climb moves it the rest.</summary>
    private const float FootWithin = 0.25f;

    /// <summary>How often, walking, it looks for an eidolon on its path ahead.</summary>
    private const int WatchMs = 500;

    /// <summary>How many of its path's nodes ahead it looks along.</summary>
    private const int LookAhead = 6;

    /// <summary>How often at most it searches again for one in its way.</summary>
    private const int RepathMs = 2000;

    /// <summary>How long at most it waits for one with a lower id in its only way.</summary>
    private const int YieldMs = 4000;

    /// <summary>How far round it a search heeds other eidolons: further off, they will have moved.</summary>
    private const float OthersRange = 24;

    /// <summary>How far ahead, in seconds, a walking eidolon's way is kept clear, at its pace now, in
    /// steps of <see cref="SweepStep"/>.</summary>
    private const double SweepSeconds = 3;
    private const double SweepStep = 0.5;

    /// <summary>How far ahead, in seconds, it looks for one walking with a lower id meeting it, in steps of
    /// <see cref="SweepStep"/>.</summary>
    private const double GiveWaySeconds = 1.5;

    /// <summary>Motion, blocks a tick, under which another eidolon counts as standing.</summary>
    internal const double StandingMotion = 0.004;

    private string? _animation;
    private bool _steering;
    private bool _steerRun;
    private long _retargetedAt;
    private Route? _route;
    private Climb? _climb;
    private bool _watching;
    private int _progress;
    private long _watchAt;
    private long _repathAt;
    private long _yieldUntil;
    private int _climbWait = -1;
    private bool _givingWay;

    /// <summary>Whether it is walking a path now, climbing on one, or waiting on it for another eidolon.</summary>
    public bool Active => _climb != null || _yieldUntil > 0 || eidolon.TaskAi?.PathTraverser?.Active == true;

    /// <summary>The nodes the last search visited.</summary>
    public int LastVisited { get; private set; }

    /// <summary>Whether the last search found its way round the other eidolons (false: through one).</summary>
    public bool LastWentAround { get; private set; } = true;

    /// <summary>Searches again for another eidolon in its way, and waits for one, so far (for a test).</summary>
    public int Repaths { get; private set; }
    public int Yields { get; private set; }

    /// <summary>A path from where it stands (or from <paramref name="from"/>) to within
    /// <paramref name="tolerance"/> blocks of <paramref name="target"/>, or null when there is none
    /// within the search budget.</summary>
    public List<PathCell>? FindPath(Vec3d target, int tolerance = 0, PathCell? from = null)
    {
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        var space = new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox);
        var finder = new WidePath(space, config.MaxFallBlocks, config.MaxClimbBlocks);
        var path = finder.FindAround(from ?? WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z),
            WidePath.NodeAt(target.X, target.Y, target.Z), OthersNear(), config.PathSearchNodes, tolerance);
        LastVisited = finder.Visited;
        LastWentAround = finder.WentAround;
        return path;
    }

    // The other eidolons near it it keeps clear of, its box as its own: each where it stands, and one
    // walking where it goes; not one walking with a higher id, which gives way to this one.
    private Others OthersNear() => new(Obstacles(OthersRange).SelectMany(o => o.At), HalfWidth, Height);

    private double HalfWidth => (eidolon.CollisionBox.X2 - eidolon.CollisionBox.X1) / 2;
    private double Height => eidolon.CollisionBox.Y2 - eidolon.CollisionBox.Y1;

    private List<(Entity Other, List<(double X, double Y, double Z)> At)> Obstacles(float range)
    {
        var list = new List<(Entity, List<(double, double, double)>)>();
        foreach (var e in eidolon.World.GetEntitiesAround(eidolon.Pos.XYZ, range, range,
                     e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive))
        {
            var p = e.Pos;
            double mx = p.Motion.X, mz = p.Motion.Z;
            bool walking = Math.Sqrt(mx * mx + mz * mz) >= StandingMotion;
            if (walking && e.EntityId > eidolon.EntityId)
                continue;
            var ways = Ways(e, walking ? SweepSeconds : 0);
            // one walking with a lower id stops where its path ends: kept clear of, as where it stands
            if (walking && e is EntityLaborEidolon { WalkGoal: { } goal })
                ways.Add((goal.X, goal.Y, goal.Z));
            list.Add((e, ways));
        }
        return list;
    }

    // Where another eidolon stands, where it walks over the next seconds at its pace now, and where the
    // climb it is making lands.
    private static List<(double X, double Y, double Z)> Ways(Entity e, double seconds)
    {
        var p = e.Pos;
        // motion is blocks a tick, at 60 ticks a second
        var at = Others.Sweep(p.X, p.Y, p.Z, p.Motion.X * 60, p.Motion.Z * 60, seconds, SweepStep).ToList();
        if (e is EntityLaborEidolon { ClimbTop: { } top })
            at.Add((top.X, top.Y, top.Z));
        return at;
    }

    // Whether another eidolon is where a climb to top lands, lands there itself, or walks over it
    // within the climb's time; not one with a higher id waiting at the foot of a climb itself, which
    // waits for this one, so two waiting at feet side by side never both wait nor both climb.
    private bool TopTaken(PathCell top)
    {
        foreach (var e in eidolon.World.GetEntitiesAround(eidolon.Pos.XYZ, LookAhead + 8, LookAhead + 8,
                     e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive))
            if (!(e is EntityLaborEidolon { ClimbWaiting: true } && e.EntityId > eidolon.EntityId)
                && new Others(Ways(e, EidolonClimb.Seconds), HalfWidth, Height).Touches(top.X, top.Y, top.Z))
                return true;
        return false;
    }

    /// <summary>Sets off for <paramref name="target"/>, walking or running (with the animation its
    /// stance gives, <see cref="EntityLaborEidolon.MoveAnimation"/>); false (and standing) when
    /// there is no path. <paramref name="tolerance"/>: stop this many blocks short (following);
    /// <paramref name="arriveWithin"/>: how near the last waypoint counts as there. Called while it
    /// climbs, the path is searched from the top of the step, and walked once the climb is done.</summary>
    public bool GoTo(Vec3d target, bool run, Action onArrived, Action onStuck, int tolerance = 0, float arriveWithin = 0.5f)
    {
        var traverser = eidolon.TaskAi?.PathTraverser;
        if (traverser == null || FindPath(target, tolerance, _climb?.Top) is not { } path)
            return false;
        // a waypoint for each node, the start's included, so a waypoint's index is its node's
        var waypoints = path.Select(c => new Vec3d(c.X, c.Y, c.Z)).ToList();
        if (tolerance == 0)
        {
            // The search ends within two blocks of the target's height; the last waypoint is where it
            // ends. The traverser counts a waypoint reached only near its height too, so a target in the
            // air or in a block (a trunk's height, not the floor beside it) would never be reached:
            // it would walk on the spot until something else stopped it.
            var end = path[^1];
            var last = target.Clone();
            if (Math.Abs(last.Y - end.Y) >= 1)
                last.Y = end.Y;
            waypoints.Add(last);
        }
        _steering = false;
        _yieldUntil = 0;
        _climbWait = -1;
        ClearClimbWait();
        _givingWay = false;
        _progress = 0;
        var route = _route = new Route(path, waypoints, run, arriveWithin, onArrived, onStuck, target.Clone(), tolerance);
        eidolon.WalkGoal = path[^1];
        Watch();
        if (_climb == null)
            Walk(route, 0);
        return true;
    }

    // Looks along the path ahead after every physics tick while it has one.
    private void Watch()
    {
        if (_watching)
            return;
        _watching = true;
        eidolon.AfterPhysicsTick += OnWatchTick;
    }

    private void Unwatch()
    {
        if (!_watching)
            return;
        _watching = false;
        eidolon.AfterPhysicsTick -= OnWatchTick;
    }

    // Every WatchMs, walking (not climbing): another eidolon on its path ahead makes it search again,
    // at most every RepathMs; with no way round one with a lower id, it waits for it (YieldMs at most).
    private void OnWatchTick()
    {
        if (_route is not { } route)
        {
            Unwatch();
            return;
        }
        long now = eidolon.World.ElapsedMilliseconds;
        if (_climb != null || now < _watchAt)
            return;
        _watchAt = now + WatchMs;
        if (_climbWait >= 0)
        {
            // at the foot of a climb, its top taken: climbs once it is clear, or waited long enough
            int index = _climbWait;
            if (now < _yieldUntil && TopTaken(route.Path[index + 1]))
                return;
            _climbWait = -1;
            ClearClimbWait();
            _yieldUntil = 0;
            StartClimb(route, index);
            return;
        }
        if (_yieldUntil > 0)
        {
            if (now < _yieldUntil && (_givingWay ? Meets(standing: true) : InTheWay(route) != null))
                return;
            _yieldUntil = 0;
            _givingWay = false;
            if (!GoTo(route.Target, route.Run, route.OnArrived, route.OnStuck, route.Tolerance, route.ArriveWithin))
            {
                _route = null;
                eidolon.WalkGoal = null;
                route.OnStuck();
            }
            return;
        }
        if (eidolon.TaskAi?.PathTraverser?.Active != true)
            return;
        if (Meets(standing: false))
        {
            // One with the lower id walks at it. Where it stands now is in that one's way (it would meet
            // it standing too, as one ahead on the same way, or crossing it): stopping there would only
            // have that one walk into it, so it turns aside at once, its search going round where that one
            // goes. Else, or with no way round, it stops for it to pass.
            if (Meets(standing: true) && now >= _repathAt)
            {
                _repathAt = now + RepathMs;
                Repaths++;
                if (GoTo(route.Target, route.Run, route.OnArrived, route.OnStuck, route.Tolerance, route.ArriveWithin) && LastWentAround)
                    return;
                if (_route == null || eidolon.TaskAi?.PathTraverser?.Active != true)
                    return;
            }
            Yields++;
            eidolon.TaskAi?.PathTraverser?.Stop();
            Animate(null);
            _givingWay = true;
            _yieldUntil = now + YieldMs;
            return;
        }
        if (now < _repathAt || InTheWay(route) is not { } other)
            return;
        _repathAt = now + RepathMs;
        Repaths++;
        if (!GoTo(route.Target, route.Run, route.OnArrived, route.OnStuck, route.Tolerance, route.ArriveWithin))
            return;
        if (LastWentAround || other.EntityId > eidolon.EntityId)
            return;
        // no way round one with the lower id: it goes first
        Yields++;
        eidolon.TaskAi?.PathTraverser?.Stop();
        Animate(null);
        _yieldUntil = now + YieldMs;
    }

    // No longer waiting at the foot of a climb (a climb under way keeps its top).
    private void ClearClimbWait()
    {
        if (!eidolon.ClimbWaiting)
            return;
        eidolon.ClimbWaiting = false;
        if (_climb == null)
            eidolon.ClimbTop = null;
    }

    // Whether one walking with a lower id meets it (their boxes within the margin) within GiveWaySeconds,
    // each going on as it goes now, or this one standing where it is.
    private bool Meets(bool standing)
    {
        var p = eidolon.Pos;
        double mx = standing ? 0 : p.Motion.X * 60, mz = standing ? 0 : p.Motion.Z * 60;
        var mine = Others.Sweep(p.X, p.Y, p.Z, mx, mz, GiveWaySeconds, SweepStep).ToList();
        foreach (var e in eidolon.World.GetEntitiesAround(p.XYZ, LookAhead + 8, LookAhead + 8,
                     e => e is EntityLaborEidolon && e.EntityId < eidolon.EntityId && e.Alive))
        {
            var o = e.Pos;
            if (Math.Sqrt(o.Motion.X * o.Motion.X + o.Motion.Z * o.Motion.Z) < StandingMotion)
                continue;
            var theirs = Others.Sweep(o.X, o.Y, o.Z, o.Motion.X * 60, o.Motion.Z * 60, GiveWaySeconds, SweepStep).ToList();
            for (int i = 0; i < mine.Count && i < theirs.Count; i++)
                if (new Others([theirs[i]], HalfWidth, Height).Touches(mine[i].X, mine[i].Y, mine[i].Z))
                    return true;
            // standing where that one's path ends is in its way however long it waits
            if (standing && e is EntityLaborEidolon { WalkGoal: { } goal } && new Others([(goal.X, goal.Y, goal.Z)], HalfWidth, Height).Touches(p.X, p.Y, p.Z))
                return true;
        }
        return false;
    }

    // The eidolon on the path's next nodes from where it is now (or where it walks to), if any: not one
    // at the path's end (it stands where it goes, which a search does not walk round) nor one it already
    // touches.
    private Entity? InTheWay(Route route)
    {
        var path = route.Path;
        double x = eidolon.Pos.X, z = eidolon.Pos.Z;
        double Away(PathCell c) => (c.X - x) * (c.X - x) + (c.Z - z) * (c.Z - z);
        for (int i = _progress + 1; i < Math.Min(path.Count, _progress + 1 + LookAhead); i++)
            if (Away(path[i]) <= Away(path[_progress]))
                _progress = i;
        var here = WidePath.NodeAt(x, eidolon.Pos.Y, z);
        foreach (var (other, at) in Obstacles(LookAhead + 8))
        {
            var one = new Others(at, HalfWidth, Height);
            var stands = new Others([at[0]], HalfWidth, Height);
            if (stands.Touches(here.X, here.Y, here.Z) || stands.Touches(path[^1].X, path[^1].Y, path[^1].Z))
                continue;
            for (int i = _progress; i < Math.Min(path.Count, _progress + 1 + LookAhead); i++)
                if (one.Touches(path[i].X, path[i].Y, path[i].Z))
                    return other;
        }
        return null;
    }

    // Walks the route on from its node at index: to the foot of its next climb, then climbs, or to its end.
    private void Walk(Route route, int index)
    {
        if (eidolon.TaskAi?.PathTraverser is not { } traverser)
            return;
        int climb = EidolonClimb.NextClimb(route.Path, index);
        int end = climb < 0 ? route.Waypoints.Count - 1 : climb;
        void Reached()
        {
            if (_route != route)
                return;
            if (climb >= 0 && TopTaken(route.Path[climb + 1]))
            {
                Yields++;
                eidolon.TaskAi?.PathTraverser?.Stop();
                Animate(null);
                _climbWait = climb;
                _yieldUntil = eidolon.World.ElapsedMilliseconds + YieldMs;
                eidolon.ClimbTop = route.Path[climb + 1];
                eidolon.ClimbWaiting = true;
            }
            else if (climb >= 0)
                StartClimb(route, climb);
            else
            {
                Animate(null);
                _route = null;
                eidolon.WalkGoal = null;
                route.OnArrived();
            }
        }
        if (end <= index)
        {
            Reached();
            return;
        }
        var stats = eidolon.FittedStats;
        Animate(eidolon.MoveAnimation(route.Run));
        traverser.FollowRoute(route.Waypoints.GetRange(index + 1, end - index), route.Run ? stats.RunSpeed : stats.WalkSpeed,
            climb < 0 ? route.ArriveWithin : FootWithin, Reached,
            () =>
            {
                if (_route != route)
                    return;
                Animate(null);
                _route = null;
                eidolon.WalkGoal = null;
                route.OnStuck();
            });
    }

    // Climbs from the route's node at index to the next, two (or more) up.
    private void StartClimb(Route route, int index)
    {
        eidolon.TaskAi?.PathTraverser?.Stop();
        Animate(null);
        PathCell foot = route.Path[index], top = route.Path[index + 1];
        var pos = eidolon.Pos;
        _climb = new Climb(route, index, foot, top, pos.X, pos.Y, pos.Z, eidolon.World.ElapsedMilliseconds, EidolonClimb.Yaw(foot, top));
        eidolon.ClimbTop = top;
        eidolon.AnimManager.StartAnimation(EidolonClimb.Animation);
        eidolon.AfterPhysicsTick += OnClimbTick;
        OnClimbTick();
    }

    // After every physics tick while it climbs: set where the climb has it, with no motion, so neither
    // gravity nor its controls move it, as the dock holds it on a gantry's spine; at the end, walk on.
    private void OnClimbTick()
    {
        if (_climb is not { } climb)
            return;
        double seconds = (eidolon.World.ElapsedMilliseconds - climb.StartedAt) / 1000.0;
        var (x, y, z) = EidolonClimb.At(seconds, climb.X, climb.Y, climb.Z, climb.Foot, climb.Top);
        var pos = eidolon.Pos;
        pos.SetPos(x, y, z);
        pos.Motion.Set(0, 0, 0);
        pos.Yaw = climb.Yaw;
        eidolon.BodyYaw = climb.Yaw;
        eidolon.Controls.StopAllMovement();
        eidolon.ServerControls.StopAllMovement();
        if (seconds < EidolonClimb.Seconds)
            return;
        EndClimb();
        // on along the route it climbed on, or along one given while it climbed (searched from the top)
        if (_route is { } route)
            Walk(route, route == climb.Route ? climb.Index + 1 : 0);
    }

    private void EndClimb()
    {
        if (_climb == null)
            return;
        _climb = null;
        eidolon.ClimbTop = null;
        eidolon.AfterPhysicsTick -= OnClimbTick;
        eidolon.AnimManager.StopAnimation(EidolonClimb.Animation);
    }

    /// <summary>Walks (or runs) straight at <paramref name="target"/>, with no search: for a creature
    /// close by on open ground, called every tick with where it is now, which only moves the goal (as
    /// the game's own seek task does), so it follows it without stopping. The traverser counts it
    /// stuck when its distance to the goal holds for a few seconds, as it does keeping pace with a
    /// creature, so the goal is set again (<c>Retarget</c>) every second, which leaves its other stuck
    /// checks (pressing against a wall) as they are. <paramref name="onStuck"/>: stuck all the same,
    /// and it stands. A climb under way is finished first.</summary>
    public bool Steer(Vec3d target, bool run, Action onStuck)
    {
        if (eidolon.TaskAi?.PathTraverser is not WaypointsTraverser traverser)
            return false;
        if (_climb != null)
            return true;
        if (_steering && _steerRun == run && traverser.Active && traverser.CurrentTarget is { } goal)
        {
            goal.Set(target);
            if (eidolon.World.ElapsedMilliseconds - _retargetedAt > 1000)
            {
                _retargetedAt = eidolon.World.ElapsedMilliseconds;
                traverser.Retarget();
            }
            return true;
        }
        var stats = eidolon.FittedStats;
        _route = null;
        eidolon.WalkGoal = null;
        _yieldUntil = 0;
        _climbWait = -1;
        ClearClimbWait();
        _givingWay = false;
        _steering = true;
        _steerRun = run;
        _retargetedAt = eidolon.World.ElapsedMilliseconds;
        Animate(eidolon.MoveAnimation(run));
        traverser.WalkTowards(target.Clone(), run ? stats.RunSpeed : stats.WalkSpeed, 0.2f,
            () => { _steering = false; Animate(null); },
            () => { _steering = false; Animate(null); onStuck(); });
        return true;
    }

    /// <summary>Whether it is steering straight at a goal (<see cref="Steer"/>) rather than walking a path.</summary>
    public bool Steering => _steering && Active;

    /// <summary>Stands. A climb under way stops where it is, and the game's physics takes it from
    /// there (its box is free there, so at most it drops back to the foot of the step).</summary>
    public void Stop()
    {
        _steering = false;
        _route = null;
        eidolon.WalkGoal = null;
        _yieldUntil = 0;
        _climbWait = -1;
        ClearClimbWait();
        _givingWay = false;
        Unwatch();
        EndClimb();
        eidolon.TaskAi?.PathTraverser?.Stop();
        Animate(null);
    }

    private void Animate(string? code)
    {
        if (_animation == code)
            return;
        if (_animation != null)
            eidolon.AnimManager.StopAnimation(_animation);
        _animation = code;
        if (code != null)
            eidolon.AnimManager.StartAnimation(code);
    }

    // A path being walked: its nodes, a waypoint for each (and the target's own after them, walking right
    // up to it), what to do at its end, and what it was searched for (to search again).
    private sealed record Route(List<PathCell> Path, List<Vec3d> Waypoints, bool Run, float ArriveWithin, Action OnArrived, Action OnStuck,
        Vec3d Target, int Tolerance);

    // A climb under way: from the foot (the route's node at Index) to the top, begun at (X, Y, Z).
    private sealed record Climb(Route Route, int Index, PathCell Foot, PathCell Top, double X, double Y, double Z, long StartedAt, float Yaw);
}
