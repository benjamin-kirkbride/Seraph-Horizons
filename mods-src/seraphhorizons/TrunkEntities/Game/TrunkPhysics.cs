using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// The trunk's physics: the game's <c>passivephysicsmultibox</c> with the drive and the step-up
/// done inside its tick (<see cref="EntityTrunk.BeforeCollision"/>), after the game's drag and
/// gravity and before its collision. So the drive's motion is the motion the collision moves the
/// trunk by, exactly, whatever the ground's drag, and a lift onto a rise is in place when the
/// collision runs, in the same tick.
/// <para>Who ticks it is the game's, as for any mount with a controllable seat (see the README's
/// Drive): the server's physics manager, unless the seatable's <c>Controller</c> is a living
/// player, and then that player's client, from its own player physics at 60 Hz, which sends the
/// trunk's position back (<see cref="OnReceivedClientPos"/>). The server takes its
/// <c>Controller</c> away again when those positions stop coming (<see cref="EntityTrunk"/>'s
/// tick), so it ticks the trunk itself. The tick is the same code on both sides, from the same
/// keys (the seat's controls), pose, logs and water.</para>
/// <para>The game's passive physics ticks nothing on the server for an entity with a passenger
/// in a controllable seat (<c>IsBeingControlled</c>), whoever its <c>Controller</c>, so this
/// re-implements <see cref="IPhysicsTickable.OnPhysicsTick"/> with the physics manager's own rule
/// instead; <see cref="IRemotePhysics"/> is re-implemented to mark the client's positions.</para>
/// </summary>
public class EntityBehaviorTrunkPhysics(Entity entity) : EntityBehaviorPassivePhysicsMultiBox(entity), IPhysicsTickable, IRemotePhysics
{
    /// <summary>The behaviour's code in the entity types.</summary>
    public const string Code = "seraphhorizons.trunkphysics";

    /// <summary>Whether a client ticks the trunk now (the seatable's <c>Controller</c> a living
    /// player), which is the physics manager's own test for leaving it alone.</summary>
    public bool ClientDriven => mountableSupplier?.Controller is EntityPlayer { Alive: true };

    /// <summary>The game's tick, from the server's physics manager or the driver's client's
    /// player physics. On the server it does nothing while a client drives the trunk.</summary>
    public new void OnPhysicsTick(float dt)
    {
        if (entity.World?.Side == EnumAppSide.Server && ClientDriven)
            return;
        Step(dt);
    }

    /// <summary>One physics tick whoever asks (the base's <c>OnPhysicsTick</c> without its test
    /// of the seat): the drive, step-up and collision, in sub-steps when moving fast.</summary>
    public void Step(float dt)
    {
        if (entity.State != EnumEntityState.Active || !Ticking)
            return;
        // Thread-static: set on the game's physics threads; a caller on another (a test) gets its own.
        collisionTester ??= new CachingCollisionTester();
        mcollisionTester ??= new MultiCollisionTester();
        var pos = entity.Pos;
        collisionTester.AssignToEntity(this, pos.Dimension);
        int steps = pos.Motion.Length() > 0.1 ? 10 : 1;
        float sub = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            SetState(pos);
            MotionAndCollision(pos, sub);
            ApplyTests(pos);
        }
    }

    /// <summary>The server's word that the driver's client sent the trunk's position (already
    /// applied, <c>ServerUdpNetwork.HandleMountPosition</c>): the trunk notes it, so the server
    /// leaves the physics to that client, and the base works out the ground and water flags from
    /// the position, as for any entity moved remotely.</summary>
    public new void OnReceivedClientPos(int version)
    {
        (entity as EntityTrunk)?.ClientPositionReceived();
        collisionTester ??= new CachingCollisionTester();
        base.OnReceivedClientPos(version);
        AdjustCollisionBoxesToYaw(1, false, entity.Pos.Yaw);
    }

    /// <summary>Other clients' interpolation of a trunk no one predicts there (the base: ground
    /// and water flags from the positions), with the boxes turned to the yaw.</summary>
    public new void HandleRemotePhysics(float dt, bool isTeleport)
    {
        base.HandleRemotePhysics(dt, isTeleport);
        AdjustCollisionBoxesToYaw(1, false, entity.Pos.Yaw);
    }

    protected override void applyCollision(EntityPos pos, float dtFactor)
    {
        if (entity is EntityTrunk trunk)
            trunk.BeforeCollision(this, pos, dtFactor);
        base.applyCollision(pos, dtFactor);
    }
}
