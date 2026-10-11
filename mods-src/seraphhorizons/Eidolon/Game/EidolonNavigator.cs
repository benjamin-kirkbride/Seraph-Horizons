using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
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
/// </summary>
public sealed class EidolonNavigator(EntityLaborEidolon eidolon)
{
    /// <summary>How near the foot of a climb the walk there counts as there; the climb moves it the rest.</summary>
    private const float FootWithin = 0.25f;

    private string? _animation;
    private bool _steering;
    private bool _steerRun;
    private long _retargetedAt;
    private Route? _route;
    private Climb? _climb;

    /// <summary>Whether it is walking a path now, or climbing on one.</summary>
    public bool Active => _climb != null || eidolon.TaskAi?.PathTraverser?.Active == true;

    /// <summary>The nodes the last search visited.</summary>
    public int LastVisited { get; private set; }

    /// <summary>A path from where it stands (or from <paramref name="from"/>) to within
    /// <paramref name="tolerance"/> blocks of <paramref name="target"/>, or null when there is none
    /// within the search budget.</summary>
    public List<PathCell>? FindPath(Vec3d target, int tolerance = 0, PathCell? from = null)
    {
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        var space = new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox);
        var finder = new WidePath(space, config.MaxFallBlocks, config.MaxClimbBlocks);
        var path = finder.Find(from ?? WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z),
            WidePath.NodeAt(target.X, target.Y, target.Z), config.PathSearchNodes, tolerance);
        LastVisited = finder.Visited;
        return path;
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
        var route = _route = new Route(path, waypoints, run, arriveWithin, onArrived, onStuck);
        if (_climb == null)
            Walk(route, 0);
        return true;
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
            if (climb >= 0)
                StartClimb(route, climb);
            else
            {
                Animate(null);
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
    // up to it), and what to do at its end.
    private sealed record Route(List<PathCell> Path, List<Vec3d> Waypoints, bool Run, float ArriveWithin, Action OnArrived, Action OnStuck);

    // A climb under way: from the foot (the route's node at Index) to the top, begun at (X, Y, Z).
    private sealed record Climb(Route Route, int Index, PathCell Foot, PathCell Top, double X, double Y, double Z, long StartedAt, float Yaw);
}
