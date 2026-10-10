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
/// </summary>
public sealed class EidolonNavigator(EntityLaborEidolon eidolon)
{
    private string? _animation;
    private bool _steering;
    private bool _steerRun;
    private long _retargetedAt;

    /// <summary>Whether it is walking a path now.</summary>
    public bool Active => eidolon.TaskAi?.PathTraverser?.Active == true;

    /// <summary>The nodes the last search visited.</summary>
    public int LastVisited { get; private set; }

    /// <summary>A path from where it stands to within <paramref name="tolerance"/> blocks of
    /// <paramref name="target"/>, or null when there is none within the search budget.</summary>
    public List<PathCell>? FindPath(Vec3d target, int tolerance = 0)
    {
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        var space = new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox);
        var finder = new WidePath(space, config.MaxFallBlocks);
        var path = finder.Find(WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z),
            WidePath.NodeAt(target.X, target.Y, target.Z), config.PathSearchNodes, tolerance);
        LastVisited = finder.Visited;
        return path;
    }

    /// <summary>Sets off for <paramref name="target"/>, walking or running (with the animation its
    /// stance gives, <see cref="EntityLaborEidolon.MoveAnimation"/>); false (and standing) when
    /// there is no path. <paramref name="tolerance"/>: stop this many blocks short (following);
    /// <paramref name="arriveWithin"/>: how near the last waypoint counts as there.</summary>
    public bool GoTo(Vec3d target, bool run, Action onArrived, Action onStuck, int tolerance = 0, float arriveWithin = 0.5f)
    {
        var traverser = eidolon.TaskAi?.PathTraverser;
        if (traverser == null || FindPath(target, tolerance) is not { } path)
            return false;
        var waypoints = path.Skip(1).Select(c => new Vec3d(c.X, c.Y, c.Z)).ToList();
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
        if (waypoints.Count == 0)
        {
            onArrived();
            return true;
        }
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        _steering = false;
        Animate(eidolon.MoveAnimation(run));
        traverser.FollowRoute(waypoints, run ? config.RunSpeed : config.WalkSpeed, arriveWithin,
            () => { Animate(null); onArrived(); },
            () => { Animate(null); onStuck(); });
        return true;
    }

    /// <summary>Walks (or runs) straight at <paramref name="target"/>, with no search: for a creature
    /// close by on open ground, called every tick with where it is now, which only moves the goal (as
    /// the game's own seek task does), so it follows it without stopping. The traverser counts it
    /// stuck when its distance to the goal holds for a few seconds, as it does keeping pace with a
    /// creature, so the goal is set again (<c>Retarget</c>) every second, which leaves its other stuck
    /// checks (pressing against a wall) as they are. <paramref name="onStuck"/>: stuck all the same,
    /// and it stands.</summary>
    public bool Steer(Vec3d target, bool run, Action onStuck)
    {
        if (eidolon.TaskAi?.PathTraverser is not WaypointsTraverser traverser)
            return false;
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
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        _steering = true;
        _steerRun = run;
        _retargetedAt = eidolon.World.ElapsedMilliseconds;
        Animate(eidolon.MoveAnimation(run));
        traverser.WalkTowards(target.Clone(), run ? config.RunSpeed : config.WalkSpeed, 0.2f,
            () => { _steering = false; Animate(null); },
            () => { _steering = false; Animate(null); onStuck(); });
        return true;
    }

    /// <summary>Whether it is steering straight at a goal (<see cref="Steer"/>) rather than walking a path.</summary>
    public bool Steering => _steering && Active;

    public void Stop()
    {
        _steering = false;
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
}
