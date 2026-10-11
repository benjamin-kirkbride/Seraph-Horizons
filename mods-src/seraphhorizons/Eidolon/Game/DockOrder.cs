using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Dock (Eidolon/README.md, "Docking"): walk into the gantry at <c>x</c>, <c>y</c>, <c>z</c> (its controller)
/// to where its body hangs, and back onto the spine (<see cref="EntityBehaviorEidolonDock.Begin"/>), to be
/// stripped and refitted. Done once it begins to dock; until then, with no way in it says so and tries
/// again every few seconds, and with the spine taken (a body being built, another eidolon docked) it waits
/// before the gantry. <c>then</c>: the order it was doing, given back when the crank wakes it.
/// </summary>
public sealed class DockOrder(BlockPos gantry, ITreeAttribute? then) : IEidolonOrder
{
    public const string OrderCode = "dock";
    private const double RetrySeconds = 4;

    private EidolonNavigator? _nav;
    private double _retryAt;

    public string Code => OrderCode;

    /// <summary>The gantry it docks on (its controller).</summary>
    public BlockPos Gantry => gantry;

    public static ITreeAttribute Args(BlockPos gantry, ITreeAttribute? then)
    {
        var tree = new TreeAttribute();
        tree.SetInt("x", gantry.X);
        tree.SetInt("y", gantry.Y);
        tree.SetInt("z", gantry.Z);
        if (then != null)
            tree["then"] = then.Clone();
        return tree;
    }

    public static DockOrder From(ITreeAttribute args) =>
        new(new BlockPos(args.GetInt("x"), args.GetInt("y"), args.GetInt("z")), args.GetTreeAttribute("then"));

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _retryAt = 0;
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        if (BEEidolonGantry.At(eidolon.World.BlockAccessor, gantry) is not { BodyPoint: { } point } be
            || be.GetBehavior<BEBehaviorEidolonBody>() is not { } body || !be.WinchComplete)
        {
            _nav?.Stop();
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-nogantry");
            return true;
        }
        bool there = eidolon.Pos.XYZ.HorizontalSquareDistanceTo(point) <= EidolonDocking.ArriveBlocks * EidolonDocking.ArriveBlocks
                     && Math.Abs(eidolon.Pos.Y - point.Y) < 1.5;
        if (!body.SpineFreeFor(eidolon))
        {
            // waits outside, not in the dock
            if (_nav is { Active: true } && there)
                _nav.Stop();
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-docktaken");
            return true;
        }
        if (there)
        {
            _nav?.Stop();
            return eidolon.Dock?.Begin(be, then) != true;
        }
        if (_nav == null || _nav.Active || now < _retryAt)
            return true;
        _retryAt = now + RetrySeconds;
        bool going = _nav.GoTo(point, false, () => { }, () => { });
        eidolon.Orders?.SetStatus(going ? null : "seraphhorizons:eidolon-status-nopath");
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled) => _nav?.Stop();

    /// <summary>Whether another loaded eidolon than <paramref name="except"/> is on its way to dock on
    /// <paramref name="gantry"/>, so a second is sent to another.</summary>
    public static bool Claimed(IWorldAccessor world, BlockPos gantry, Entity except) =>
        world.GetEntitiesAround(gantry.ToVec3d(), 64, 16, e => e is EntityLaborEidolon && e != except && e.Alive)
            .OfType<EntityLaborEidolon>()
            .Any(e => e.Orders is { OrderCode: OrderCode, OrderArgs: { } args }
                      && args.GetInt("x") == gantry.X && args.GetInt("y") == gantry.Y && args.GetInt("z") == gantry.Z);
}

/// <summary>
/// The dock order and the command tool's <c>dock</c> mode (wheel place 90): click a gantry (any of its
/// cells) to send each bound eidolon in range to it, or the air to send each to the nearest gantry with
/// its spine free within <see cref="EidolonDocking.SearchBlocks"/> (a gantry another is already on its way
/// to is passed over). Registered on both sides, as the wheel is chosen by index.
/// </summary>
public class EidolonDockSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorEidolonDock.Code, typeof(EntityBehaviorEidolonDock));
        EidolonOrders.Register(DockOrder.OrderCode, (_, args) => DockOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = DockOrder.OrderCode,
            Order = 90,
            Icon = new AssetLocation("game", "textures/icons/worldmap/home.svg"),
            Mark = EidolonMarkKind.Block,
            MarkOptional = true,
            CheckTarget = (world, pos) => BEEidolonGantry.At(world.BlockAccessor, pos) is { WinchComplete: true }
                ? null
                : EidolonCommand.Refuse("seraphhorizons:eidoloncommander-dock-notgantry"),
            Command = Command,
        });
    }

    private static EidolonCommand Command(EidolonCommandContext c)
    {
        var eidolon = c.Eidolon;
        if (eidolon.Dock?.Holds == true)
            return EidolonCommand.Refuse("seraphhorizons:eidoloncommander-dock-already");
        var world = eidolon.World;
        BEEidolonGantry? gantry;
        if (c.Target is { } target)
        {
            gantry = BEEidolonGantry.At(world.BlockAccessor, target);
            if (gantry == null)
                return EidolonCommand.Refuse("seraphhorizons:eidoloncommander-dock-notgantry");
            if (!Free(gantry, eidolon))
                return EidolonCommand.Refuse("seraphhorizons:eidoloncommander-dock-taken");
        }
        else if ((gantry = Nearest(eidolon)) == null)
            return EidolonCommand.Refuse("seraphhorizons:eidoloncommander-dock-none", (int)EidolonDocking.SearchBlocks);
        var orders = eidolon.Orders;
        var then = orders?.OrderArgs is { } running && EidolonDocking.Resumes(orders.OrderCode) ? running : null;
        return EidolonCommand.Order(DockOrder.OrderCode, DockOrder.Args(gantry.Pos, then));
    }

    private static bool Free(BEEidolonGantry gantry, EntityLaborEidolon eidolon) =>
        gantry.WinchComplete && gantry.GetBehavior<BEBehaviorEidolonBody>()?.SpineFreeFor(eidolon) == true
                             && !DockOrder.Claimed(eidolon.World, gantry.Pos, eidolon);

    /// <summary>The nearest gantry to <paramref name="eidolon"/> with its spine free for it, within
    /// <see cref="EidolonDocking.SearchBlocks"/> (block entities of the loaded chunks round it).</summary>
    public static BEEidolonGantry? Nearest(EntityLaborEidolon eidolon)
    {
        var ba = eidolon.World.BlockAccessor;
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        double reach = EidolonDocking.SearchBlocks;
        var at = eidolon.Pos.XYZ;
        BEEidolonGantry? best = null;
        double bestDistance = double.MaxValue;
        var pos = new BlockPos(0);
        for (int cx = (int)Math.Floor((at.X - reach) / size); cx <= (int)Math.Floor((at.X + reach) / size); cx++)
        for (int cy = (int)Math.Floor((at.Y - 8) / size); cy <= (int)Math.Floor((at.Y + 8) / size); cy++)
        for (int cz = (int)Math.Floor((at.Z - reach) / size); cz <= (int)Math.Floor((at.Z + reach) / size); cz++)
        {
            pos.Set(cx * size, cy * size, cz * size);
            if (ba.GetChunkAtBlockPos(pos) is not { BlockEntities: { } entities })
                continue;
            foreach (var be in entities.Values)
            {
                if (be is not BEEidolonGantry gantry || gantry.BodyPoint is not { } point)
                    continue;
                double d = point.DistanceTo(at);
                if (d <= reach && d < bestDistance && Free(gantry, eidolon))
                {
                    best = gantry;
                    bestDistance = d;
                }
            }
        }
        return best;
    }
}
