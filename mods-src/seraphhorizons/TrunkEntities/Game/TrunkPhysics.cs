using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// The trunk's physics: the game's <c>passivephysicsmultibox</c> with the drive and the step-up
/// done inside its tick (<see cref="EntityTrunk.BeforeCollision"/>), server side, after the
/// game's drag and gravity and before its collision. So the drive's motion is the motion the
/// collision moves the trunk by, exactly, whatever the ground's drag, and a lift onto a rise is
/// in place when the collision runs, in the same tick, rather than from the entity's own game
/// tick, which runs apart from the physics ticks (an earlier build stepped up there and could see
/// a motion the collision had already zeroed, so the trunk fell back off a step a few times
/// before it caught). The client runs no physics of its own for a trunk (it interpolates the
/// server's positions, <c>interpolateposition</c>), so nothing is done there.
/// </summary>
public class EntityBehaviorTrunkPhysics(Entity entity) : EntityBehaviorPassivePhysicsMultiBox(entity)
{
    /// <summary>The behaviour's code in the entity types.</summary>
    public const string Code = "seraphhorizons.trunkphysics";

    protected override void applyCollision(EntityPos pos, float dtFactor)
    {
        if (entity is EntityTrunk trunk && entity.Api?.Side == EnumAppSide.Server)
            trunk.BeforeCollision(this, pos, dtFactor);
        base.applyCollision(pos, dtFactor);
    }
}
