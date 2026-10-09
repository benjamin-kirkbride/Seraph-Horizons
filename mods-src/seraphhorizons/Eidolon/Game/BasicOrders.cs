using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>Stay: hold where it is (the default pose plays). Never done.</summary>
public sealed class StayOrder : IEidolonOrder
{
    public const string OrderCode = "stay";

    public string Code => OrderCode;

    public void Start(EntityLaborEidolon eidolon) => eidolon.TaskAi?.PathTraverser?.Stop();

    public bool Continue(EntityLaborEidolon eidolon, float dt) => true;

    public void Stop(EntityLaborEidolon eidolon, bool cancelled) { }
}

/// <summary>
/// Go to a point (arguments <c>x</c>, <c>y</c>, <c>z</c>, and <c>run</c>): walk there by the wide
/// pathfinder, done on arrival. With no way there it says so (status "no way there") and tries again
/// every few seconds; stuck on the way, it searches again. The pathfinding proof for #673 and the
/// building block of follow (#675) and the jobs.
/// </summary>
public sealed class GoToOrder(Vec3d target, bool run) : IEidolonOrder
{
    public const string OrderCode = "goto";
    private const double RetrySeconds = 4;

    private EidolonNavigator? _nav;
    private bool _arrived;
    private double _retryAt;

    public string Code => OrderCode;

    public Vec3d Target => target;

    public static ITreeAttribute Args(Vec3d target, bool run)
    {
        var tree = new TreeAttribute();
        tree.SetDouble("x", target.X);
        tree.SetDouble("y", target.Y);
        tree.SetDouble("z", target.Z);
        tree.SetBool("run", run);
        return tree;
    }

    public static GoToOrder From(ITreeAttribute args) =>
        new(new Vec3d(args.GetDouble("x"), args.GetDouble("y"), args.GetDouble("z")), args.GetBool("run"));

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _arrived = false;
        _retryAt = 0;
        Set(eidolon);
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        if (_arrived)
            return false;
        if (_nav is { Active: false } && eidolon.World.ElapsedMilliseconds / 1000.0 >= _retryAt)
            Set(eidolon);
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled) => _nav?.Stop();

    private void Set(EntityLaborEidolon eidolon)
    {
        _retryAt = eidolon.World.ElapsedMilliseconds / 1000.0 + RetrySeconds;
        if (eidolon.Pos.XYZ.HorizontalSquareDistanceTo(target) < 0.5 * 0.5 && Math.Abs(eidolon.Pos.Y - target.Y) < 1.5)
        {
            _arrived = true;
            return;
        }
        bool going = _nav!.GoTo(target, run, () => _arrived = true, () => { });
        eidolon.Orders?.SetStatus(going ? null : "seraphhorizons:eidolon-status-nopath");
    }
}
