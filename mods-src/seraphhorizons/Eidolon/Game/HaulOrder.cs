using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Haul (#678; README "Eidolon", hauling): carry every trunk entity lying in an area, one at a time,
/// to a rosser's or bucking mill's infeed (arguments: the area's corners <c>x1</c>..<c>z2</c> and the
/// machine's cell <c>mx</c>, <c>my</c>, <c>mz</c>). It looks over the area every
/// <see cref="ScanSeconds"/> for a trunk the machine takes (not one being driven), nearest first, and
/// hauls it with a <see cref="TrunkHauler"/>; a trunk it cannot reach is left alone for
/// <see cref="SkipSeconds"/>. With none left it says so and waits, still looking, so trunks felled into
/// the area later are hauled too. Never done.
/// </summary>
public sealed class HaulOrder(MarkArea area, BlockPos machine) : IEidolonOrder
{
    public const string OrderCode = "haul";
    public const double ScanSeconds = 2;
    public const double SkipSeconds = 30;

    private TrunkHauler? _hauler;
    private readonly Dictionary<long, double> _skip = [];
    private double _scanAt;

    public string Code => OrderCode;

    public MarkArea Area => area;

    public BlockPos Machine => machine;

    /// <summary>The steps it hauls with (once started).</summary>
    public TrunkHauler? Hauler => _hauler;

    public static ITreeAttribute Args(MarkArea area, BlockPos machine)
    {
        var tree = new TreeAttribute();
        tree.SetInt("x1", area.Min.X);
        tree.SetInt("y1", area.Min.Y);
        tree.SetInt("z1", area.Min.Z);
        tree.SetInt("x2", area.Max.X);
        tree.SetInt("y2", area.Max.Y);
        tree.SetInt("z2", area.Max.Z);
        tree.SetInt("mx", machine.X);
        tree.SetInt("my", machine.Y);
        tree.SetInt("mz", machine.Z);
        return tree;
    }

    public static HaulOrder From(ITreeAttribute args) => new(
        MarkArea.Between(new MarkPos(args.GetInt("x1"), args.GetInt("y1"), args.GetInt("z1")),
                         new MarkPos(args.GetInt("x2"), args.GetInt("y2"), args.GetInt("z2"))),
        new BlockPos(args.GetInt("mx"), args.GetInt("my"), args.GetInt("mz")));

    public void Start(EntityLaborEidolon eidolon)
    {
        _hauler ??= new TrunkHauler(eidolon, machine);
        _scanAt = 0;
        eidolon.TaskAi?.PathTraverser?.Stop();
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        if (_hauler == null)
            return true;
        if (_hauler.Busy)
        {
            long fetching = _hauler.Fetching;
            if (_hauler.Step() == HaulStep.Lost && fetching != 0)
                _skip[fetching] = Now(eidolon) + SkipSeconds;
            return true;
        }
        if (Now(eidolon) < _scanAt)
            return true;
        _scanAt = Now(eidolon) + ScanSeconds;
        if (MachineInfeeds.Find(eidolon.World, machine) is not { } infeed)
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-nomachine");
            return true;
        }
        if (Next(eidolon, infeed) is { } trunk && _hauler.Fetch(trunk))
            _hauler.Step();
        else
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-haul-clear");
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled) => _hauler?.Interrupt();

    /// <summary>The nearest trunk entity lying in the area that the machine takes, not being driven
    /// and not skipped.</summary>
    private EntityTrunk? Next(EntityLaborEidolon eidolon, MachineInfeed infeed)
    {
        double now = Now(eidolon);
        foreach (var gone in _skip.Where(s => s.Value <= now).Select(s => s.Key).ToList())
            _skip.Remove(gone);
        var centre = new Vec3d((area.Min.X + area.Max.X + 1) / 2.0, (area.Min.Y + area.Max.Y + 1) / 2.0, (area.Min.Z + area.Max.Z + 1) / 2.0);
        float horizontal = Math.Max(area.SizeX, area.SizeZ) / 2f + 1;
        float vertical = area.SizeY / 2f + HaulPlan.AreaHeadroom + 1;
        return eidolon.World.GetEntitiesAround(centre, horizontal, vertical, e => e is EntityTrunk { Alive: true } t
                && t.Pos.Dimension == eidolon.Pos.Dimension && !t.Grabbed && !t.Driven && !_skip.ContainsKey(t.EntityId)
                && HaulPlan.InArea(area, t.Pos.X, t.Pos.Y, t.Pos.Z) && t.Trunk is { } stack && infeed.Takes(stack))
            .OrderBy(e => e.Pos.SquareDistanceTo(eidolon.Pos))
            .FirstOrDefault() as EntityTrunk;
    }

    private static double Now(EntityLaborEidolon eidolon) => eidolon.World.ElapsedMilliseconds / 1000.0;
}

/// <summary>
/// Hauling's registrations (#678): the carried trunk's behaviour, the eidolon's renderer that draws
/// it, the <c>haul</c> order and its command tool mode ("Haul trunks", wheel place 60), which marks an
/// area and then a rosser or bucking mill (any of its cells). Without trunk entities the mode refuses.
/// </summary>
public class EidolonHaulSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorEidolonTrunk.Code, typeof(EntityBehaviorEidolonTrunk));
        EidolonOrders.Register(HaulOrder.OrderCode, (_, args) => HaulOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = HaulOrder.OrderCode,
            Order = 60,
            Icon = new AssetLocation("game", "textures/icons/moveew.svg"),
            Mark = EidolonMarkKind.AreaThenBlock,
            CheckTarget = (world, pos) => MachineInfeeds.Find(world, pos) == null
                ? EidolonCommand.Refuse("eidolon-haul-notmachine")
                : null,
            Command = c =>
                !TrunkEntitySystem.Of(c.Eidolon.Api).Enabled ? EidolonCommand.Refuse("eidolon-haul-notrunks")
                : c.Area is not { } area || c.Target is not { } target ? EidolonCommand.Refuse("eidolon-haul-notmachine")
                : EidolonCommand.Order(HaulOrder.OrderCode, HaulOrder.Args(area, target)),
        });
    }

    public override void StartClientSide(ICoreClientAPI api) =>
        api.RegisterEntityRendererClass(EidolonShapeRenderer.ClassName, typeof(EidolonShapeRenderer));
}
