using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>What one step of a <see cref="TrunkHauler"/> came to.</summary>
public enum HaulStep
{
    /// <summary>Nothing to do: no trunk asked for, none carried.</summary>
    Idle,

    /// <summary>On its way, taking up, waiting at the infeed or laying down.</summary>
    Working,

    /// <summary>A trunk was just laid in the infeed (its oil is spent); idle again.</summary>
    Delivered,

    /// <summary>The trunk asked for is gone, moved, taken, or cannot be reached; idle again.</summary>
    Lost,
}

/// <summary>
/// One trunk hauled (README "Eidolon", hauling): the steps of the haul order, usable by any order
/// that moves a trunk entity to a machine's infeed (the crew order, #679). Give it a trunk entity
/// (<see cref="Fetch"/>) and call <see cref="Step"/> every tick of the order: it walks to stand square
/// to the trunk, takes it up (<c>trunk-pickup</c>, or <c>trunk-thick-pickup</c> in both arms), walks it
/// to the machine with the carry walk, waits holding it (<c>trunk-carry-idle</c>) while a trunk lies in
/// the infeed cells, and lays it there (<c>trunk-setdown</c>), where the machine takes it as it takes
/// any trunk lying there; then <c>SpendOil(TrunkDelivered)</c>. A trunk already carried
/// (<see cref="EntityBehaviorEidolonTrunk"/>, after a load or an interruption) is delivered without
/// fetching. <see cref="Interrupt"/> when the order stops: the carried trunk is kept, and the next step
/// carries on. Server side.
/// </summary>
public sealed class TrunkHauler(EntityLaborEidolon eidolon, BlockPos machine)
{
    private enum Phase { Idle, ToTrunk, TakingUp, ToInfeed, AtInfeed, LayingDown }

    private const double RetrySeconds = 3;
    private const double MovedTolerance = 0.75;
    private const double ArrivedTolerance = 1.25;

    private readonly EidolonNavigator _nav = new(eidolon);
    private Phase _phase;
    private long _trunkId;
    private Vec3d? _trunkAt;
    private HaulStand _stand;
    private HaulDrop? _drop;
    private HaulMove _move;
    private double _moveStarted;
    private bool _evented;
    private bool _arrived, _stuck;
    private double _retryAt;
    private string? _pose;

    /// <summary>The machine it delivers to (any of its cells).</summary>
    public BlockPos Machine => machine;

    /// <summary>The trunk entity it is going for, before it takes it up (0 for none).</summary>
    public long Fetching => _phase is Phase.ToTrunk or Phase.TakingUp ? _trunkId : 0;

    /// <summary>Trunks it has laid in the infeed.</summary>
    public int Delivered { get; private set; }

    private EntityBehaviorEidolonTrunk? Carry => eidolon.GetBehavior<EntityBehaviorEidolonTrunk>();

    /// <summary>Whether it has something to do: a trunk asked for or carried.</summary>
    public bool Busy => _phase != Phase.Idle || Carry?.Carrying == true;

    private double Now => eidolon.World.ElapsedMilliseconds / 1000.0;

    /// <summary>Goes for <paramref name="trunk"/>; false when it carries one already or is busy.</summary>
    public bool Fetch(EntityTrunk trunk)
    {
        if (Busy || Carry == null)
            return false;
        _trunkId = trunk.EntityId;
        _trunkAt = new Vec3d(trunk.Pos.X, trunk.Pos.Y, trunk.Pos.Z);
        _phase = Phase.ToTrunk;
        _arrived = _stuck = false;
        _retryAt = 0;
        return true;
    }

    /// <summary>The order stops (interrupted, replaced): it stands, its animations stopped; a trunk
    /// taken up stays carried, one not yet taken is let go.</summary>
    public void Interrupt()
    {
        _nav.Stop();
        _nav.MoveAnimation = null;
        if (_move.Animation != null)
            eidolon.AnimManager.StopAnimation(_move.Animation);
        _move = default;
        Pose(null);
        _phase = Phase.Idle;
    }

    /// <summary>One tick of hauling.</summary>
    public HaulStep Step()
    {
        if (Carry is not { } carry)
            return HaulStep.Idle;
        if (_phase == Phase.Idle && carry.Carrying)
            BeginDelivery();
        return _phase switch
        {
            Phase.ToTrunk => ToTrunk(),
            Phase.TakingUp => TakingUp(carry),
            Phase.ToInfeed => ToInfeed(),
            Phase.AtInfeed => AtInfeed(),
            Phase.LayingDown => LayingDown(carry),
            _ => HaulStep.Idle,
        };
    }

    // ---- Fetching ----

    private EntityTrunk? Wanted()
    {
        if (eidolon.World.GetEntityById(_trunkId) is not EntityTrunk { Alive: true } trunk || trunk.Grabbed || trunk.Driven || _trunkAt == null)
            return null;
        double dx = trunk.Pos.X - _trunkAt.X, dz = trunk.Pos.Z - _trunkAt.Z;
        return dx * dx + dz * dz <= MovedTolerance * MovedTolerance && Math.Abs(trunk.Pos.Y - _trunkAt.Y) <= MovedTolerance ? trunk : null;
    }

    private HaulStep ToTrunk()
    {
        if (Wanted() is not { } trunk)
            return Lose();
        bool thick = trunk.TypeClass == Machines.Core.TrunkClass.Thick;
        if (_arrived)
        {
            if (Near(_stand.X, _stand.Z))
            {
                Face(_stand.Yaw);
                _nav.Stop();
                Start(HaulPlan.PickUp(thick));
                _phase = Phase.TakingUp;
                return HaulStep.Working;
            }
            _arrived = false;
        }
        if (_stuck || (!_nav.Active && Now >= _retryAt))
        {
            if (_stuck && Now < _retryAt)
                return HaulStep.Working;
            _stuck = false;
            _retryAt = Now + RetrySeconds;
            _nav.MoveAnimation = null;
            foreach (var stand in HaulPlan.PickupStands(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, thick, eidolon.Pos.X, eidolon.Pos.Z))
            {
                if (_nav.GoTo(new Vec3d(stand.X, trunk.Pos.Y, stand.Z), false, () => _arrived = true, () => _stuck = true))
                {
                    _stand = stand;
                    eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-totrunk");
                    return HaulStep.Working;
                }
            }
            return Lose();
        }
        return HaulStep.Working;
    }

    private HaulStep TakingUp(EntityBehaviorEidolonTrunk carry)
    {
        Face(_stand.Yaw);
        if (!_evented && Now >= _moveStarted + _move.EventAt)
        {
            _evented = true;
            if (Wanted() is not { } trunk || !carry.TakeUp(trunk))
            {
                StopMove();
                return Lose();
            }
        }
        if (Now < _moveStarted + _move.Ends)
            return HaulStep.Working;
        StopMove();
        if (!carry.Carrying)
            return Lose();
        BeginDelivery();
        return HaulStep.Working;
    }

    private HaulStep Lose()
    {
        _nav.Stop();
        Pose(null);
        _phase = Phase.Idle;
        return HaulStep.Lost;
    }

    // ---- Delivering ----

    private void BeginDelivery()
    {
        _phase = Phase.ToInfeed;
        _arrived = _stuck = false;
        _retryAt = 0;
        _drop = null;
    }

    private bool Thick => Carry?.Thick == true;

    private HaulStep ToInfeed()
    {
        if (_arrived && _drop is { } drop)
        {
            if (Near(drop.Stand.X, drop.Stand.Z))
            {
                Face(drop.Stand.Yaw);
                _phase = Phase.AtInfeed;
                return AtInfeed();
            }
            _arrived = false;
        }
        if (_nav.Active || Now < _retryAt)
            return HaulStep.Working;
        _retryAt = Now + RetrySeconds;
        _stuck = false;
        Pose(HaulPlan.CarryIdle(Thick));
        if (MachineInfeeds.Find(eidolon.World, machine) is not { } infeed || HaulPlan.Drop(infeed.CellMarks, infeed.OutwardX, infeed.OutwardZ, Thick) is not { } found)
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-nomachine");
            return HaulStep.Working;
        }
        _drop = found;
        _nav.MoveAnimation = HaulPlan.CarryWalk(Thick);
        if (_nav.GoTo(new Vec3d(found.Stand.X, found.Y, found.Stand.Z), false, () => _arrived = true, () => _stuck = true))
        {
            Pose(null);
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-toinfeed");
        }
        else
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-nopath");
        return HaulStep.Working;
    }

    private HaulStep AtInfeed()
    {
        if (_drop is not { } drop || MachineInfeeds.Find(eidolon.World, machine) is not { } infeed)
        {
            BeginDelivery();
            return HaulStep.Working;
        }
        Face(drop.Stand.Yaw);
        if (TrunkStations.FindInCells(eidolon.World, infeed.Cells, _ => true) != null)
        {
            Pose(HaulPlan.CarryIdle(Thick));
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-busy");
            return HaulStep.Working;
        }
        Pose(null);
        Start(HaulPlan.SetDown(Thick));
        _phase = Phase.LayingDown;
        eidolon.Orders?.SetStatus(null);
        return HaulStep.Working;
    }

    private HaulStep LayingDown(EntityBehaviorEidolonTrunk carry)
    {
        Face(_drop?.Stand.Yaw ?? eidolon.Pos.Yaw);
        if (!_evented && Now >= _moveStarted + _move.EventAt)
        {
            _evented = true;
            var infeed = MachineInfeeds.Find(eidolon.World, machine);
            if (_drop is not { } drop || infeed == null || TrunkStations.FindInCells(eidolon.World, infeed.Cells, _ => true) != null)
            {
                // Something got there first: hold on to it and wait again.
                StopMove();
                _phase = Phase.AtInfeed;
                return HaulStep.Working;
            }
            if (carry.LayDown(new Vec3d(drop.X, drop.Y, drop.Z), drop.TrunkYaw) != null)
            {
                Delivered++;
                eidolon.SpendOil(EidolonJob.TrunkDelivered);
            }
        }
        if (Now < _moveStarted + _move.Ends)
            return HaulStep.Working;
        StopMove();
        _nav.MoveAnimation = null;
        _phase = Phase.Idle;
        return HaulStep.Delivered;
    }

    // ---- Moving and animating ----

    private bool Near(double x, double z)
    {
        double dx = eidolon.Pos.X - x, dz = eidolon.Pos.Z - z;
        return dx * dx + dz * dz <= ArrivedTolerance * ArrivedTolerance;
    }

    private void Face(float yaw)
    {
        eidolon.Pos.Yaw = yaw;
        eidolon.BodyYaw = yaw;
    }

    private void Start(HaulMove move)
    {
        Pose(null);
        _move = move;
        _moveStarted = Now;
        _evented = false;
        eidolon.AnimManager.StartAnimation(move.Animation);
    }

    private void StopMove()
    {
        if (_move.Animation != null)
            eidolon.AnimManager.StopAnimation(_move.Animation);
        _move = default;
    }

    // A looping pose while it stands (the carry idles); null stops it.
    private void Pose(string? code)
    {
        if (_pose == code)
            return;
        if (_pose != null)
            eidolon.AnimManager.StopAnimation(_pose);
        _pose = code;
        if (code != null)
            eidolon.AnimManager.StartAnimation(code);
    }
}
