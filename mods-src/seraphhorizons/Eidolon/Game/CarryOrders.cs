using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>How walking up to a block goes.</summary>
public enum ApproachState
{
    Walking,
    /// <summary>On a stand point beside the block, facing it.</summary>
    There,
    /// <summary>No stand point it can reach (it tries again every few seconds).</summary>
    NoWay,
}

/// <summary>
/// Walks the eidolon up to a block to lift it or set its load down there
/// (<see cref="EidolonCarrying.Stands"/>: a side of the block where its box is free and has ground,
/// nearest first), then puts it exactly on that stand point facing the block, so its box is clear
/// of the block's cell and the shape's reach lines up. Shared by the carry and set-down orders.
/// </summary>
public sealed class BlockApproach(BlockPos block)
{
    private const double RetrySeconds = 3;

    private CarryStand? _stand;
    private double _retryAt;

    public BlockPos Block => block;

    public void Reset()
    {
        _stand = null;
        _retryAt = 0;
    }

    public ApproachState Step(EntityLaborEidolon eidolon, EidolonNavigator nav, double now)
    {
        var pos = eidolon.Pos;
        if (_stand is { } stand && !nav.Active && EidolonCarrying.Near(stand, pos.X, pos.Y, pos.Z))
        {
            var space = new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox);
            if (space.Free(stand.X, stand.Y, stand.Z))
                pos.SetPos(stand.X, stand.Y, stand.Z);
            pos.Yaw = stand.Yaw;
            eidolon.BodyYaw = stand.Yaw;
            return ApproachState.There;
        }
        if (nav.Active)
            return ApproachState.Walking;
        if (now < _retryAt)
            return _stand == null ? ApproachState.NoWay : ApproachState.Walking;
        _retryAt = now + RetrySeconds;
        var stands = EidolonCarrying.Stands(block.X, block.Y, block.Z, pos.X, pos.Z,
            new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox));
        foreach (var s in stands)
            if (nav.GoTo(new Vec3d(s.X, s.Y, s.Z), false, () => { }, () => { }, 0, 0.3f))
            {
                _stand = s;
                return ApproachState.Walking;
            }
        _stand = null;
        return ApproachState.NoWay;
    }
}

/// <summary>What the carry orders share: the commander (told when a job fails), the block, the walk to it.</summary>
public abstract class CarryOrderBase(BlockPos target, string playerUid, string? playerName) : IEidolonOrder
{
    protected readonly BlockApproach Approach = new(target);
    protected EidolonNavigator? Nav;
    protected CarryPhase Phase;
    protected double StartedAt;

    public abstract string Code { get; }

    public BlockPos Target => target;

    public string PlayerUid => playerUid;

    public static ITreeAttribute Args(BlockPos target, IPlayer player)
    {
        var tree = new TreeAttribute();
        tree.SetInt("x", target.X);
        tree.SetInt("y", target.Y);
        tree.SetInt("z", target.Z);
        tree.SetString("player", player.PlayerUID);
        tree.SetString("name", player.PlayerName);
        return tree;
    }

    protected static BlockPos TargetOf(ITreeAttribute args) => new(args.GetInt("x"), args.GetInt("y"), args.GetInt("z"), 0);

    protected static double Now(EntityLaborEidolon eidolon) => eidolon.World.ElapsedMilliseconds / 1000.0;

    protected static EntityBehaviorEidolonCarry? Carry(EntityLaborEidolon eidolon) => eidolon.GetBehavior<EntityBehaviorEidolonCarry>();

    public abstract void Start(EntityLaborEidolon eidolon);

    public abstract bool Continue(EntityLaborEidolon eidolon, float dt);

    public virtual void Stop(EntityLaborEidolon eidolon, bool cancelled) => Nav?.Stop();

    /// <summary>Walks to the block; true once there.</summary>
    protected bool WalkUp(EntityLaborEidolon eidolon)
    {
        Nav ??= new EidolonNavigator(eidolon);
        switch (Approach.Step(eidolon, Nav, Now(eidolon)))
        {
            case ApproachState.There:
                eidolon.Orders?.SetStatus(null);
                return true;
            case ApproachState.NoWay:
                eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-nopath");
                return false;
            default:
                eidolon.Orders?.SetStatus(null);
                return false;
        }
    }

    /// <summary>Starts a one-shot (lift, setdown), the stand animation kept off while it plays.</summary>
    protected void Play(EntityLaborEidolon eidolon, string animation)
    {
        if (Carry(eidolon) is { } carry)
            carry.Busy = true;
        eidolon.AnimManager.StopAnimation(EntityBehaviorEidolonCarry.IdleAnimation);
        eidolon.AnimManager.StartAnimation(animation);
        StartedAt = Now(eidolon);
    }

    protected void EndPlay(EntityLaborEidolon eidolon, string animation)
    {
        eidolon.AnimManager.StopAnimation(animation);
        if (Carry(eidolon) is not { } carry)
            return;
        carry.Busy = false;
        // lift ends in carry-idle's first frame: straight on into it, with no frame of the rest idle.
        if (carry.Carrying)
            eidolon.AnimManager.StartAnimation(EntityBehaviorEidolonCarry.IdleAnimation);
    }

    /// <summary>Tells the commander (if online) why the job ended.</summary>
    protected void Tell(EntityLaborEidolon eidolon, string key, params object[] args)
    {
        if (eidolon.World.PlayerByUid(playerUid) is IServerPlayer player && player.ConnectionState == EnumClientState.Playing)
            player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "seraphhorizons:" + key, args), EnumChatType.Notification);
    }

    protected string Commander(EntityLaborEidolon eidolon) => eidolon.World.PlayerByUid(playerUid)?.PlayerName ?? playerName ?? "?";
}

/// <summary>
/// Carry (#676): walk up to the marked block (any Carry On lets a player carry), play <c>lift</c>, take
/// it out of the world on its grab frame (<see cref="EidolonCarryOn.Lift"/>), and carry it
/// (<see cref="EntityBehaviorEidolonCarry"/>), following the player who gave the order
/// (<see cref="FollowOrder"/>) until told to set it down. Done, with a word to them, when the
/// block is gone or cannot be lifted. Interrupted before the grab it starts the walk again; once
/// it holds the block it only follows.
/// </summary>
public sealed class CarryOrder(BlockPos target, string playerUid, string? playerName) : CarryOrderBase(target, playerUid, playerName)
{
    public const string OrderCode = "carry";
    public const string Animation = "lift";

    private readonly FollowOrder _follow = new(playerUid, playerName);

    public override string Code => OrderCode;

    public static CarryOrder From(ITreeAttribute args) => new(TargetOf(args), args.GetString("player") ?? "", args.GetString("name"));

    public override void Start(EntityLaborEidolon eidolon)
    {
        Nav ??= new EidolonNavigator(eidolon);
        Approach.Reset();
        if (Carry(eidolon)?.Carrying == true)
        {
            Phase = CarryPhase.Holding;
            _follow.Start(eidolon);
        }
        else
            Phase = CarryPhase.Approach;
    }

    public override bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        if (Carry(eidolon) is not { } carry)
            return false;
        double now = Now(eidolon);
        switch (Phase)
        {
            case CarryPhase.Approach:
                var block = eidolon.World.BlockAccessor.GetBlock(Target);
                if (!EidolonCarryOn.IsCarryable(eidolon.Api, block))
                {
                    Tell(eidolon, "eidolon-carry-gone");
                    return false;
                }
                if (!WalkUp(eidolon))
                    return true;
                Play(eidolon, Animation);
                Phase = CarryPhase.Reaching;
                return true;
            case CarryPhase.Reaching:
                if (now - StartedAt < EidolonCarrying.GrabSeconds)
                    return true;
                var name = eidolon.World.BlockAccessor.GetBlock(Target).GetPlacedBlockName(eidolon.World, Target);
                if (EidolonCarryOn.Lift(eidolon, Target, out string? failure) is not { } load)
                {
                    EndPlay(eidolon, Animation);
                    Tell(eidolon, "eidolon-carry-failed", name, failure ?? "?");
                    return false;
                }
                carry.Hold(load);
                Phase = CarryPhase.Finishing;
                return true;
            case CarryPhase.Finishing:
                if (now - StartedAt < EidolonCarrying.LiftSeconds)
                    return true;
                EndPlay(eidolon, Animation);
                Phase = CarryPhase.Holding;
                _follow.Start(eidolon);
                return true;
            default:
                return carry.Carrying && _follow.Continue(eidolon, dt);
        }
    }

    public override void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        base.Stop(eidolon, cancelled);
        _follow.Stop(eidolon, cancelled);
        if (Phase is CarryPhase.Reaching or CarryPhase.Finishing)
        {
            EndPlay(eidolon, Animation);
            Phase = Phase == CarryPhase.Reaching ? CarryPhase.Approach : CarryPhase.Holding;
        }
    }
}

/// <summary>
/// Set down (#676): walk up to the marked place (the marked block if the load can replace it, as
/// grass, else the block above it; decided when the order is given), play <c>setdown</c>, and on its
/// release frame put the load there as it stood, its contents in it
/// (<see cref="EntityBehaviorEidolonCarry.TryPlace"/>), which costs <see cref="EidolonJob.LoadCarried"/>'s
/// oil. Done then; done with a word to the commander when the place has been taken meanwhile.
/// </summary>
public sealed class SetDownOrder(BlockPos target, string playerUid, string? playerName) : CarryOrderBase(target, playerUid, playerName)
{
    public const string OrderCode = "setdown";
    public const string Animation = "setdown";

    private bool _placed;

    public override string Code => OrderCode;

    public static SetDownOrder From(ITreeAttribute args) => new(TargetOf(args), args.GetString("player") ?? "", args.GetString("name"));

    /// <summary>Where the load goes for a click on <paramref name="clicked"/>: that block if the load
    /// can replace it, else the one above; null when neither will take it.</summary>
    public static BlockPos? PlaceFor(EntityBehaviorEidolonCarry carry, BlockPos clicked)
    {
        if (carry.CanPlace(clicked, out _))
            return clicked.Copy();
        var above = clicked.UpCopy();
        return carry.CanPlace(above, out _) ? above : null;
    }

    public override void Start(EntityLaborEidolon eidolon)
    {
        Nav ??= new EidolonNavigator(eidolon);
        Approach.Reset();
        Phase = _placed ? CarryPhase.Holding : CarryPhase.Approach;
    }

    public override bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        if (Carry(eidolon) is not { } carry)
            return false;
        double now = Now(eidolon);
        switch (Phase)
        {
            case CarryPhase.Approach:
                if (!carry.Carrying)
                    return false;
                if (!WalkUp(eidolon))
                    return true;
                if (!carry.CanPlace(Target, out _))
                {
                    Tell(eidolon, "eidolon-setdown-failed");
                    return false;
                }
                Play(eidolon, Animation);
                Phase = CarryPhase.Reaching;
                return true;
            case CarryPhase.Reaching:
                if (now - StartedAt < EidolonCarrying.ReleaseSeconds)
                    return true;
                if (!carry.TryPlace(Target, out _))
                {
                    EndPlay(eidolon, Animation);
                    Tell(eidolon, "eidolon-setdown-failed");
                    return false;
                }
                _placed = true;
                eidolon.SpendOil(EidolonJob.LoadCarried);
                Phase = CarryPhase.Finishing;
                return true;
            case CarryPhase.Finishing:
                if (now - StartedAt < EidolonCarrying.SetDownSeconds)
                    return true;
                EndPlay(eidolon, Animation);
                return false;
            default:
                return false;
        }
    }

    public override void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        base.Stop(eidolon, cancelled);
        if (Phase is CarryPhase.Reaching or CarryPhase.Finishing)
            EndPlay(eidolon, Animation);
        // Set down already: nothing left to do when it starts again.
        Phase = _placed ? CarryPhase.Holding : CarryPhase.Approach;
    }
}
