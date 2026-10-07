using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// The driver's place on a trunk entity (<see cref="EntityTrunk"/> is the <c>seatable</c>'s seat
/// supplier): the player stands on the ground <see cref="TrunkDrive.StandOff"/> beyond the end
/// they took (<see cref="EntityTrunk.DriveEnd"/>), feet at the trunk's underside (afloat, at the
/// waterline less <see cref="TrunkDrive.SwimFeetBelow"/>, swimming), facing the trunk along its
/// axis. The game puts a mounted player at <see cref="SeatPosition"/> every physics tick and feeds
/// their movement keys into <see cref="EntitySeat.Controls"/> on both sides, which the trunk's
/// drive reads; sneak dismounts (the game's <c>EntitySeat</c>). Not controllable in the game's
/// sense (<c>controllable: false</c>), so the server keeps running the trunk's physics; see the
/// README for why.
/// <para>Only the player the trunk's mark names may mount it on the server
/// (<see cref="CanMount"/>), which <see cref="EntityTrunk"/> sets just before it mounts them, so
/// nothing else (the <c>seatable</c>'s own click, a save's seat data or a player's saved
/// <c>mountedOn</c>) puts anyone on a trunk.</para>
/// </summary>
public class TrunkDriveSeat : EntitySeat
{
    /// <summary>The class name in a mounted player's <c>mountedOn</c>, registered with
    /// <see cref="GetMountable"/>.</summary>
    public const string ClassName = "seraphhorizons.trunkdrive";

    private readonly EntityPos _pos = new();
    private readonly Matrixf _transform = new();
    private readonly Vec3f _eye = new();

    public TrunkDriveSeat(IMountable mountable, string seatId, SeatConfig config) : base(mountable, seatId, config)
    {
    }

    private EntityTrunk? Trunk => Entity as EntityTrunk;

    // The view turns with the trunk and the head stays free (within EntityTrunk's limits).
    public override EnumMountAngleMode AngleMode => EnumMountAngleMode.PushYaw;

    public override EntityPos SeatPosition
    {
        get
        {
            if (Trunk is not { } trunk)
                return _pos;
            var pos = trunk.Pos;
            _pos.SetFrom(pos);
            var (x, z) = TrunkDrive.Stand(pos.X, pos.Z, pos.Yaw, TrunkBox.Size(trunk.TypeClass).Length, trunk.DriveEnd);
            double y = trunk.Afloat ? pos.Y + trunk.SwimmingOffsetY - TrunkDrive.SwimFeetBelow : pos.Y;
            _pos.SetPos(x, y, z);
            _pos.Motion.Set(0, 0, 0);
            _pos.Yaw = (float)TrunkDrive.FacingYaw(pos.Yaw, trunk.DriveEnd);
            _pos.Pitch = 0;
            _pos.Roll = 0;
            return _pos;
        }
    }

    public override Vec3f LocalEyePos => _eye.Set(0, (float)(Passenger?.Properties?.EyeHeight ?? 1.7), 0);

    public override Matrixf RenderTransform => _transform;

    public override float FpHandPitchFollow => 1f;

    /// <summary>Walking while the drive keys are held, standing otherwise; swimming afloat.</summary>
    public override AnimationMetaData? SuggestedAnimation
    {
        get
        {
            if (Passenger?.Properties?.Client?.AnimationsByMetaCode is not { } anims || Trunk is not { } trunk)
                return null;
            bool moving = Controls.Forward || Controls.Backward || Controls.Left || Controls.Right;
            string code = trunk.Afloat ? (moving ? "swim" : "swimidle") : (moving ? "walk" : "idle");
            return anims.TryGetValue(code, out var meta) ? meta : null;
        }
    }

    public override bool CanMount(EntityAgent entityAgent) =>
        entityAgent is EntityPlayer && Trunk is { Alive: true } trunk
        && (trunk.Api.Side == EnumAppSide.Client || trunk.GrabbedBy == entityAgent.EntityId);

    public override void MountableToTreeAttributes(TreeAttribute tree)
    {
        base.MountableToTreeAttributes(tree);
        tree.SetLong("entityIdMount", Entity.EntityId);
        tree.SetString("className", ClassName);
    }

    public override void DidMount(EntityAgent entityAgent)
    {
        base.DidMount(entityAgent);
        if (Trunk is not { } trunk)
            return;
        if (trunk.Api is ICoreClientAPI capi && capi.World.Player?.Entity?.EntityId == entityAgent.EntityId)
            capi.Input.MouseYaw = (float)TrunkDrive.FacingYaw(trunk.Pos.Yaw, trunk.DriveEnd);
        trunk.Api.Event.TriggerEntityMounted(entityAgent, this);
    }

    public override void DidUnmount(EntityAgent entityAgent)
    {
        if (entityAgent is EntityPlayer player)
        {
            player.BodyYawLimits = null;
            player.HeadYawLimits = null;
        }
        base.DidUnmount(entityAgent);
        controls.StopAllMovement();
        if (Trunk is not { } trunk)
            return;
        trunk.DriverLeft(entityAgent);
        trunk.Api.Event.TriggerEntityUnmounted(entityAgent, this);
    }

    /// <summary>The game's way back from a player's saved or synced <c>mountedOn</c> to the seat.
    /// On the server only a trunk marked as driven gives its seat, so a player saved while driving
    /// is not put back on the trunk when they come back (the mark never outlives a session).</summary>
    public static IMountableSeat? GetMountable(IWorldAccessor world, TreeAttribute tree)
    {
        if (world.GetEntityById(tree.GetLong("entityIdMount")) is not EntityTrunk trunk)
            return null;
        string? id = tree.GetString("seatId");
        var seat = trunk.GetBehavior<EntityBehaviorSeatable>()?.Seats?.FirstOrDefault(s => s.SeatId == id);
        return seat != null && (world.Side == EnumAppSide.Client || trunk.Grabbed) ? seat : null;
    }
}
