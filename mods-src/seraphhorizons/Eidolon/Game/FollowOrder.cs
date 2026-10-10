using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Follow a player (arguments <c>player</c>, their uid, and <c>name</c>; README "Eidolon", following):
/// it keeps <see cref="EidolonConfig.FollowDistance"/> blocks behind them by the wide pathfinder,
/// walking, or running beyond <see cref="EidolonConfig.FollowRunDistance"/>
/// (<see cref="EidolonFollow.Gait"/>), and searches again as they move on. Where it finds no way to
/// them (a gap it cannot cross, a door too small) it stands, says so in its info and tells them once,
/// and tries again every few seconds. Gone (logged off, too far, dead), it waits where it is. Never done.
/// </summary>
public sealed class FollowOrder(string playerUid, string? playerName) : IEidolonOrder
{
    public const string OrderCode = "follow";
    private const double CheckSeconds = 0.5;
    private const double RetrySeconds = 3;

    private EidolonNavigator? _nav;
    private FollowGait _gait;
    private Vec3d? _goal;
    private double _nextCheck;
    private double _retryAt;
    private bool _told;

    public string Code => OrderCode;

    public string PlayerUid => playerUid;

    /// <summary>Whether it is waiting for lack of a way to them.</summary>
    public bool Waiting { get; private set; }

    public static ITreeAttribute Args(IPlayer player)
    {
        var tree = new TreeAttribute();
        tree.SetString("player", player.PlayerUID);
        tree.SetString("name", player.PlayerName);
        return tree;
    }

    public static FollowOrder From(ITreeAttribute args) => new(args.GetString("player") ?? "", args.GetString("name"));

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _gait = FollowGait.Stand;
        _goal = null;
        _nextCheck = 0;
        _retryAt = 0;
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        if (now < _nextCheck || _nav == null)
            return true;
        _nextCheck = now + CheckSeconds;
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        var player = eidolon.World.PlayerByUid(playerUid);
        var them = player?.Entity;
        double distance = them == null ? double.MaxValue : them.Pos.DistanceTo(eidolon.Pos.XYZ);
        if (them is not { Alive: true } || them.Pos.Dimension != eidolon.Pos.Dimension
            || eidolon.World.GetEntityById(them.EntityId) == null || distance > config.CommandRange * 2)
        {
            Halt();
            Waiting = false;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-follow-gone", Name(player));
            return true;
        }

        var gait = EidolonFollow.Gait(distance, config.FollowDistance, config.FollowRunDistance, _gait);
        if (gait == FollowGait.Stand)
        {
            if (_gait != FollowGait.Stand || _nav.Active)
                Halt();
            if (Waiting)
                Clear(eidolon);
            _told = false;
            return true;
        }

        var goal = Ground(eidolon.World.BlockAccessor, them.Pos.XYZ);
        bool repath = !_nav.Active || gait != _gait || _goal == null || EidolonFollow.Repath(goal.DistanceTo(_goal), distance);
        if (!repath || (Waiting && now < _retryAt))
            return true;
        int tolerance = Math.Max(1, (int)Math.Floor(config.FollowDistance) - 1);
        if (_nav.GoTo(goal, gait == FollowGait.Run, () => { }, () => { }, tolerance))
        {
            _gait = gait;
            _goal = goal;
            if (Waiting)
                Clear(eidolon);
            return true;
        }

        // No way to them: stand, say so, and tell them once until it reaches them again.
        Halt();
        Waiting = true;
        _retryAt = now + RetrySeconds;
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-follow-noway", Name(player));
        if (!_told && player is IServerPlayer sp)
        {
            sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, "seraphhorizons:eidolon-follow-waiting"), EnumChatType.Notification);
            _told = true;
        }
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled) => Halt();

    private void Halt()
    {
        _nav?.Stop();
        _gait = FollowGait.Stand;
        _goal = null;
    }

    private void Clear(EntityLaborEidolon eidolon)
    {
        Waiting = false;
        eidolon.Orders?.SetStatus(null);
    }

    private string Name(IPlayer? player) => player?.PlayerName ?? playerName ?? "?";

    /// <summary>Where to walk to for someone at <paramref name="at"/>: the ground under them (they may
    /// be jumping, swimming or on a ladder), at most 8 blocks down.</summary>
    public static Vec3d Ground(IBlockAccessor blocks, Vec3d at)
    {
        int feet = (int)Math.Floor(at.Y + 0.01);
        var pos = new BlockPos((int)Math.Floor(at.X), feet, (int)Math.Floor(at.Z), 0);
        for (int i = 0; i <= 8; i++)
        {
            pos.Y = feet - i;
            if (blocks.GetBlock(pos).CollisionBoxes is { Length: > 0 })
                return i == 0 ? at.Clone() : new Vec3d(at.X, pos.Y + 1, at.Z);
        }
        return at.Clone();
    }
}
