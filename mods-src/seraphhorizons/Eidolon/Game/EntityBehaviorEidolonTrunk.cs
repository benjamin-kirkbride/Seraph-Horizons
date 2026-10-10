using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The trunk an eidolon carries (README "Eidolon", hauling): a trunk entity it took up is removed and
/// its trunk stack, unchanged, kept in the watched attribute <see cref="TrunkKey"/> (as a trunk
/// entity keeps it), so it syncs to clients, which draw it at its attachment point
/// (<see cref="EidolonShapeRenderer"/>), and saves with the eidolon. Laying it down spawns a trunk
/// entity again (<see cref="LayDown"/>). Only an order that hauls holds one
/// (<see cref="Holders"/>): with any other order, or none, a carried trunk is laid down in front
/// of it, at once and without the animation, so a trunk is never stuck on a shoulder.
/// </summary>
public class EntityBehaviorEidolonTrunk(Entity entity) : EntityBehavior(entity), IEidolonStance
{
    public const string Code = "seraphhorizons.eidolonTrunk";
    public const string TrunkKey = "seraphhorizons:carriedTrunk";

    /// <summary>The order codes that may hold a trunk: the haul order, and the crew order (#679),
    /// which adds its own (<see cref="EidolonCrewSystem"/>).</summary>
    public static readonly HashSet<string> Holders = new(StringComparer.Ordinal) { HaulOrder.OrderCode };

    private float _sinceCheck;

    public override string PropertyName() => Code;

    /// <summary>The trunk it carries (its own stack, resolved), or null.</summary>
    public ItemStack? Trunk
    {
        get
        {
            var stack = entity.WatchedAttributes.GetItemstack(TrunkKey);
            if (stack != null && entity.World != null && !stack.ResolveBlockOrItem(entity.World))
                return null;
            return stack;
        }
    }

    public bool Carrying => entity.WatchedAttributes.HasAttribute(TrunkKey);

    /// <summary>Whether the carried trunk is a thick one (Logging Expanded's xl and xxl).</summary>
    public bool Thick => Trunk is { } stack && IsThick(stack);

    /// <summary>Carrying, it walks with <c>trunk-carry-walk</c> or <c>trunk-thick-carry-walk</c>
    /// (<see cref="IEidolonStance"/>).</summary>
    public string? MoveAnimation(bool run) => Carrying ? HaulPlan.CarryWalk(Thick) : null;

    public static bool IsThick(ItemStack trunk) => TrunkBox.ClassOf(trunk.Block?.Variant["size"]) == TrunkClass.Thick;

    /// <summary>Takes <paramref name="trunk"/> up: its stack is kept and the entity removed. False when
    /// it already carries one or the trunk is gone. Server side.</summary>
    public bool TakeUp(EntityTrunk trunk)
    {
        if (Carrying || entity.Api.Side != EnumAppSide.Server || TrunkStations.TakeEntity(trunk) is not { } stack)
            return false;
        Hold(stack);
        return true;
    }

    /// <summary>Holds <paramref name="stack"/> as the carried trunk (server side): for a trunk taken
    /// some other way.</summary>
    public void Hold(ItemStack stack)
    {
        entity.WatchedAttributes.SetItemstack(TrunkKey, stack);
        entity.WatchedAttributes.MarkPathDirty(TrunkKey);
    }

    /// <summary>
    /// Lays the carried trunk down as a trunk entity, its underside's middle at <paramref name="at"/>
    /// lying along <paramref name="yaw"/>; with no place given, in front of the eidolon, across its
    /// facing, where its animations put it. The trunk entity, or null when it carried none (or the
    /// stack was no trunk any more: it is dropped as an item). Server side.
    /// </summary>
    public EntityTrunk? LayDown(Vec3d? at = null, float? yaw = null)
    {
        if (entity.Api.Side != EnumAppSide.Server || !Carrying)
            return null;
        var stack = Trunk;
        entity.WatchedAttributes.RemoveAttribute(TrunkKey);
        entity.WatchedAttributes.MarkPathDirty(TrunkKey);
        if (stack == null)
            return null;
        float facing = entity.Pos.Yaw;
        double reach = HaulPlan.Reach(IsThick(stack));
        var place = at ?? new Vec3d(entity.Pos.X + Math.Sin(facing) * reach, entity.Pos.Y, entity.Pos.Z + Math.Cos(facing) * reach);
        var spawned = TrunkSpawns.Spawn(entity.World, stack, place, yaw ?? facing + GameMath.PIHALF, entity.Pos.Dimension);
        if (spawned == null)
            entity.World.SpawnItemEntity(stack, place);
        return spawned;
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server || !Carrying)
            return;
        _sinceCheck += deltaTime;
        if (_sinceCheck < 1)
            return;
        _sinceCheck = 0;
        if (entity is EntityLaborEidolon { Orders: var orders } && (orders?.OrderCode is not { } code || !Holders.Contains(code)))
            LayDown();
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        if (Trunk is { } stack)
            infotext.AppendLine(Lang.Get("seraphhorizons:eidolon-info-trunk", stack.GetName()));
    }
}
