using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Crew (#679; README "Eidolon", the crew): fell the trees of an area and haul their trunks to a
/// rosser's or bucking mill's infeed, in a loop (arguments as the haul order's: the area's corners
/// <c>x1</c>..<c>z2</c> and the machine's cell <c>mx</c>, <c>my</c>, <c>mz</c>). It fells one tree
/// with a <see cref="FellOrder"/> run <c>once</c> (the same instance throughout, so the trees it gave
/// up stay given up; it replants and spends the tree's oil), then looks for the trunk that tree threw:
/// a trunk entity new since it started on the tree, lying near the stump
/// (<see cref="EidolonCrew.NearStump"/>). That trunk it hauls with a <see cref="TrunkHauler"/> (the
/// trunk's oil), then walks back to the next tree, until no grown tree it can reach is left in the area
/// and no trunk of its felling (nor any the machine takes lying in the area) waits; then it tells its
/// owner, says so and waits, looking again every <see cref="EidolonCrew.RelookSeconds"/>. A tree that
/// throws no trunk (too small, or no Logging Expanded: its logs fall as items) is simply passed on
/// from; a trunk the machine does not take, or lost to the hauler <see cref="EidolonCrew.Tries"/>
/// times, is left. Without an axe it waits for one, as felling does (a trunk carried or waiting is
/// hauled first). Interrupted (self-defence, a dry reservoir, a slump), it keeps its state, the trunk
/// it carries included, and carries on where it was.
/// </summary>
public sealed class CrewOrder(MarkArea area, BlockPos machine) : IEidolonOrder
{
    public const string OrderCode = "crew";

    /// <summary>How often, at most, it looks for a trunk to haul while it has none.</summary>
    public const double ScanSeconds = 2;

    private FellOrder _fell = new(area, once: true);
    private TrunkHauler? _hauler;
    private readonly HashSet<long> _before = [];
    private readonly List<long> _felledTrunks = [];
    private readonly Dictionary<long, int> _losses = [];
    private readonly HashSet<long> _left = [];
    private bool _felling;
    private int _fellSeen;
    private BlockPos? _stump;
    private double _lookFrom, _lookUntil;
    private bool _looking;
    private long _hauling;
    private double _scanAt;
    private bool _clear;
    private double _relookAt;
    private bool _toldDone;

    public string Code => OrderCode;

    public MarkArea Area => area;

    public BlockPos Machine => machine;

    /// <summary>Trees it has felled.</summary>
    public int Felled { get; private set; }

    /// <summary>Trunks it has laid in the infeed.</summary>
    public int Delivered => _hauler?.Delivered ?? 0;

    /// <summary>The trunks of its felling still to haul (entity ids).</summary>
    public IReadOnlyList<long> Waiting => _felledTrunks;

    /// <summary>Whether it found the area clear and waits.</summary>
    public bool Done => _clear && !_felling && !_looking && _felledTrunks.Count == 0 && _hauler?.Busy != true;

    public static ITreeAttribute Args(MarkArea area, BlockPos machine) => HaulOrder.Args(area, machine);

    public static CrewOrder From(ITreeAttribute args)
    {
        var haul = HaulOrder.From(args);
        return new CrewOrder(haul.Area, haul.Machine);
    }

    public void Start(EntityLaborEidolon eidolon)
    {
        _hauler ??= new TrunkHauler(eidolon, machine);
        _scanAt = 0;
        _felling = false;
        eidolon.TaskAi?.PathTraverser?.Stop();
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        _hauler?.Interrupt();
        if (_felling)
            _fell.Stop(eidolon, cancelled);
        _felling = false;
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        if (_hauler == null)
            return true;
        double now = Now(eidolon);

        if (_felling)
        {
            bool going = _fell.Continue(eidolon, dt);
            if (going)
                return true;
            _felling = false;
            if (_fell.Felled == _fellSeen && _fell.Clear)
                Cleared(now);
        }
        // A tree came down (also when it was interrupted between the cut and the swing's end).
        if (_fell.Felled > _fellSeen)
        {
            Felled += _fell.Felled - _fellSeen;
            _fellSeen = _fell.Felled;
            _stump = _fell.LastStump?.Copy();
            _looking = _stump != null;
            _lookFrom = now + EidolonCrew.SettleSeconds;
            _lookUntil = now + EidolonCrew.LookSeconds;
            _toldDone = false;
        }

        if (_hauler.Busy)
        {
            Haul(eidolon);
            return true;
        }
        if (_looking)
        {
            if (now < _lookFrom)
                return true;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-crew-looking");
            if (FindFelledTrunks(eidolon) || now >= _lookUntil)
                _looking = false;
            return true;
        }
        if (MachineInfeeds.Find(eidolon.World, machine) is not { } infeed)
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-nomachine");
            return true;
        }
        // A trunk of its felling at once; one lying in the area at most every ScanSeconds.
        if (_felledTrunks.Count > 0 || now >= _scanAt)
        {
            _scanAt = now + ScanSeconds;
            if (NextTrunk(eidolon, infeed) is { } trunk && _hauler.Fetch(trunk))
            {
                _hauling = trunk.EntityId;
                Haul(eidolon);
                return true;
            }
        }
        if (!_clear)
        {
            BeginTree(eidolon, dt);
            return true;
        }
        if (now >= _relookAt)
        {
            // Look again with a fresh felling: the trees given up are tried again, and a grown one
            // (a replanted tree) is felled.
            _fell = new FellOrder(area, once: true);
            _fellSeen = 0;
            _clear = false;
            BeginTree(eidolon, dt);
            return true;
        }
        if (!_toldDone)
        {
            _toldDone = true;
            FellOrder.TellOwner(eidolon, "seraphhorizons:eidolon-crew-done-told", Felled, Delivered);
        }
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-crew-done");
        return true;
    }

    private void Haul(EntityLaborEidolon eidolon)
    {
        long fetching = _hauler!.Fetching;
        switch (_hauler.Step())
        {
            case HaulStep.Lost when fetching != 0:
                _losses[fetching] = _losses.GetValueOrDefault(fetching) + 1;
                if (_losses[fetching] >= EidolonCrew.Tries || eidolon.World.GetEntityById(fetching) is not EntityTrunk { Alive: true })
                    Leave(fetching);
                break;
            case HaulStep.Delivered:
                _felledTrunks.Remove(_hauling);
                _hauling = 0;
                break;
        }
    }

    private void BeginTree(EntityLaborEidolon eidolon, float dt)
    {
        // The trunks lying before this tree falls: the one it throws is new.
        _before.Clear();
        foreach (var trunk in TrunksAround(eidolon, AreaCentre(), AreaReach() + (float)EidolonCrew.TrunkReach))
            _before.Add(trunk.EntityId);
        _fell.Start(eidolon);
        _felling = true;
        if (!_fell.Continue(eidolon, dt))
        {
            _felling = false;
            if (_fell.Felled == _fellSeen && _fell.Clear)
                Cleared(Now(eidolon));
        }
    }

    private void Cleared(double now)
    {
        _clear = true;
        _relookAt = now + EidolonCrew.RelookSeconds;
    }

    /// <summary>Adds the trunks the last tree threw (new, near its stump); true when it found one.</summary>
    private bool FindFelledTrunks(EntityLaborEidolon eidolon)
    {
        if (_stump is not { } stump)
            return false;
        bool found = false;
        foreach (var trunk in TrunksAround(eidolon, new Vec3d(stump.X + 0.5, stump.Y, stump.Z + 0.5), (float)EidolonCrew.TrunkReach))
        {
            if (_before.Contains(trunk.EntityId) || _felledTrunks.Contains(trunk.EntityId) || _left.Contains(trunk.EntityId)
                || !EidolonCrew.NearStump(stump.X, stump.Y, stump.Z, trunk.Pos.X, trunk.Pos.Y, trunk.Pos.Z))
                continue;
            _felledTrunks.Add(trunk.EntityId);
            _before.Add(trunk.EntityId);
            found = true;
        }
        return found;
    }

    /// <summary>The next trunk to haul: one of its felling (nearest first), else the nearest lying in
    /// the area; one the machine does not take is left.</summary>
    private EntityTrunk? NextTrunk(EntityLaborEidolon eidolon, MachineInfeed infeed)
    {
        foreach (long id in _felledTrunks.ToList())
            if (eidolon.World.GetEntityById(id) is not EntityTrunk { Alive: true, Trunk: { } stack } || !infeed.Takes(stack))
                Leave(id);
        var own = _felledTrunks.Select(id => eidolon.World.GetEntityById(id)).OfType<EntityTrunk>()
            .Where(t => !t.Grabbed && !t.Driven)
            .OrderBy(t => t.Pos.SquareDistanceTo(eidolon.Pos)).FirstOrDefault();
        if (own != null)
            return own;
        return TrunksAround(eidolon, AreaCentre(), AreaReach())
            .Where(t => !t.Grabbed && !t.Driven && !_left.Contains(t.EntityId) && HaulPlan.InArea(area, t.Pos.X, t.Pos.Y, t.Pos.Z)
                        && t.Trunk is { } stack && infeed.Takes(stack))
            .OrderBy(t => t.Pos.SquareDistanceTo(eidolon.Pos))
            .FirstOrDefault();
    }

    private void Leave(long id)
    {
        _felledTrunks.Remove(id);
        _left.Add(id);
        _losses.Remove(id);
    }

    private IEnumerable<EntityTrunk> TrunksAround(EntityLaborEidolon eidolon, Vec3d centre, float horizontal)
    {
        float vertical = area.SizeY / 2f + HaulPlan.AreaHeadroom + (float)EidolonCrew.TrunkDrop + EidolonFelling.AreaHeightSlack;
        int dim = eidolon.Pos.Dimension;
        return eidolon.World.GetEntitiesAround(centre, horizontal, vertical, e => e is EntityTrunk { Alive: true } t && t.Pos.Dimension == dim)
            .OfType<EntityTrunk>();
    }

    private Vec3d AreaCentre() => new((area.Min.X + area.Max.X + 1) / 2.0, (area.Min.Y + area.Max.Y + 1) / 2.0, (area.Min.Z + area.Max.Z + 1) / 2.0);

    private float AreaReach() => Math.Max(area.SizeX, area.SizeZ) / 2f + 1;

    private static double Now(EntityLaborEidolon eidolon) => eidolon.World.ElapsedMilliseconds / 1000.0;
}

/// <summary>
/// The crew order's registrations (#679): the <c>crew</c> order (which may hold a trunk:
/// <see cref="EntityBehaviorEidolonTrunk.Holders"/>) and its command tool mode ("Fell and haul",
/// wheel place 70), which marks an area and then a rosser or bucking mill (any of its cells), as the
/// haul mode does. Refused without an axe (as felling is) or with a block in its arms (as hauling is).
/// Without trunk entities or Logging Expanded it still fells, with nothing to haul.
/// </summary>
public class EidolonCrewSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        EntityBehaviorEidolonTrunk.Holders.Add(CrewOrder.OrderCode);
        EidolonOrders.Register(CrewOrder.OrderCode, (_, args) => CrewOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = CrewOrder.OrderCode,
            Order = 70,
            Icon = new AssetLocation("game", "textures/icons/worldmap/tree.svg"),
            Mark = EidolonMarkKind.AreaThenBlock,
            MaxAreaSide = EidolonFellSystem.MaxAreaSide,
            CheckTarget = (world, pos) => MachineInfeeds.Find(world, pos) == null
                ? EidolonCommand.Refuse("eidolon-haul-notmachine")
                : null,
            Command = c =>
                c.Eidolon.GetBehavior<EntityBehaviorEidolonAxe>()?.Axe == null ? EidolonCommand.Refuse("seraphhorizons:eidolon-fell-noaxe")
                : c.Area is not { } area || c.Target is not { } target ? EidolonCommand.Refuse("eidolon-haul-notmachine")
                : c.Eidolon.GetBehavior<EntityBehaviorEidolonCarry>()?.LoadStack is { } held
                    ? EidolonCommand.Refuse("eidolon-carry-full", held.GetName())
                : EidolonCommand.Order(CrewOrder.OrderCode, CrewOrder.Args(area, target)),
        });
    }
}
