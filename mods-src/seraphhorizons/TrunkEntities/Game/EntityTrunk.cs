using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// A Logging Expanded tree trunk lying in the world. Its payload is the trunk's own item stack
/// (<c>loggingmod:treetrunk-{wood}-{size}-{branches}-{side}</c> with its logs, branches, resin and
/// char state in its attributes), kept in <see cref="TrunkKey"/> of the watched attributes, so it
/// syncs to clients and saves with the entity, and anything that takes the trunk takes that stack
/// unchanged. The entity type (<c>seraphhorizons:trunk-thin</c> or <c>-thick</c>) gives its
/// collision boxes, its display class's (<see cref="TrunkBoxes"/>); its weight follows its logs
/// (<see cref="TrunkWeight.Weight"/>), on its own copy of the type's properties. It is never picked
/// up as an item; it is dragged (<see cref="TrunkGrab"/>, or a rope through the game's
/// <c>ropetieable</c>), shoved, floated, or shouldered through Carry On.
/// </summary>
public class EntityTrunk : Entity
{
    /// <summary>The watched attribute holding the trunk's stack.</summary>
    public const string TrunkKey = "trunk";

    /// <summary>The watched attribute holding the server's weight for the trunk.</summary>
    public const string WeightKey = "seraphhorizons:weight";

    /// <summary>The watched attribute holding the entity id of the player whose rope-less grab
    /// holds the trunk (0 or missing: none).</summary>
    public const string GrabbedByKey = "seraphhorizons:grabbedBy";

    /// <summary>The watched attribute holding the cloth id of that grab's rope.</summary>
    public const string GrabClothKey = "seraphhorizons:grabCloth";

    /// <summary>The wood's density, for floating (water is 1000).</summary>
    public const float Density = 700f;

    /// <summary>More interaction help for a trunk entity, client side (a tool's hold, say): each
    /// gives the entries for the trunk and the player looking at it.</summary>
    public static readonly List<System.Func<EntityTrunk, IClientPlayer, IEnumerable<WorldInteraction>>> HelpProviders = [];

    /// <summary>The payload, resolved; null only before it is set.</summary>
    public ItemStack? Trunk
    {
        get
        {
            var stack = WatchedAttributes.GetItemstack(TrunkKey);
            if (stack != null && stack.Collectible == null && World != null)
                stack.ResolveBlockOrItem(World);
            return stack;
        }
    }

    /// <summary>Thin or thick: the stack's <c>size</c> variant's class, else the entity type's. Hides the
    /// registry's class name, which <c>base.Class</c> still gives.</summary>
    public new TrunkClass Class =>
        Trunk?.Block?.Variant["size"] is { } size ? TrunkBox.ClassOf(size)
        : Code?.Path == TrunkEntitySystem.ThickCode.Path ? TrunkClass.Thick : TrunkClass.Thin;

    /// <summary>The entity type's class, which its collision boxes are made for.</summary>
    public TrunkClass TypeClass => Code?.Path == TrunkEntitySystem.ThickCode.Path ? TrunkClass.Thick : TrunkClass.Thin;

    /// <summary>The stored logs.</summary>
    public int Logs => Trunk is { } stack && World != null ? Trunks.StoredLogs(stack, World) : 0;

    /// <summary>Whether a player's rope-less grab holds it.</summary>
    public bool Grabbed => GrabbedBy != 0;

    /// <summary>The entity id of the player whose grab holds it, 0 for none.</summary>
    public long GrabbedBy => WatchedAttributes.GetLong(GrabbedByKey);

    /// <summary>
    /// Rewrites the payload and syncs it; the weight follows. Null or a stack with no logs kills the
    /// entity (removed, nothing dropped). Server side.
    /// </summary>
    public void SetTrunk(ItemStack? stack)
    {
        if (stack == null || World == null || Trunks.StoredLogs(stack, World) <= 0)
        {
            if (Alive)
                Die(EnumDespawnReason.Removed);
            return;
        }
        WatchedAttributes.SetItemstack(TrunkKey, stack);
        WatchedAttributes.MarkPathDirty(TrunkKey);
        UpdateWeight();
    }

    public override bool IsInteractable => true;

    // Found by players' shoving and by the creature partition, as the game's boat is.
    public override bool IsCreature => true;

    public override bool ApplyGravity => true;

    public override float MaterialDensity => Density;

    // Floats about half under: the display class's height less half of it.
    public override double SwimmingOffsetY => TrunkBox.Size(TypeClass).Height / 2.0;

    public override double FrustumSphereRadius => TrunkBoxes.Radius(TypeClass) + 0.5;

    public override bool CanCollect(Entity byEntity) => false;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        // Its own copy: the weight is per trunk, and the type's properties are shared.
        base.Initialize(properties.Clone(), api, InChunkIndex3d);
        Properties.Weight = WatchedAttributes.GetFloat(WeightKey, Properties.Weight);
        WatchedAttributes.RegisterModifiedListener(WeightKey, () => Properties.Weight = WatchedAttributes.GetFloat(WeightKey, Properties.Weight));
        if (api.Side == EnumAppSide.Server)
            UpdateWeight();
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        if (Api.Side != EnumAppSide.Server)
            return;
        if (Trunk == null || Logs <= 0)
        {
            Die(EnumDespawnReason.Removed);
            return;
        }
        // A grab never outlives the session it was made in.
        if (Grabbed && !(TrunkEntitySystem.Of(Api).Grabs?.Holds(this) ?? false))
            TrunkGrab.ClearStale(this);
    }

    private void UpdateWeight()
    {
        if (Api is not { Side: EnumAppSide.Server })
            return;
        float weight = TrunkWeight.Weight(Logs, TrunkEntitySystem.Of(Api).Config);
        Properties.Weight = weight;
        if (WatchedAttributes.GetFloat(WeightKey, -1) != weight)
            WatchedAttributes.SetFloat(WeightKey, weight);
    }

    /// <summary>
    /// The trunk's boxes turned with its yaw (<see cref="TrunkBoxes.Turned"/>): the ray picks the
    /// trunk along its length, whichever way it lies. The entity's own square hitbox is only its
    /// width across.
    /// </summary>
    public override bool IntersectsRay(Ray ray, AABBIntersectionTest interesectionTester, out double intersectionDistance, ref int selectionBoxIndex)
    {
        intersectionDistance = 0;
        if (!Alive)
            return false;
        bool hit = false;
        double best = double.MaxValue;
        foreach (var b in TrunkBoxes.Turned(TypeClass, Pos.Yaw))
        {
            var box = new Cuboidf(b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2);
            if (!interesectionTester.RayIntersectsWithCuboid(box, Pos.X, Pos.InternalY, Pos.Z))
                continue;
            hit = true;
            double d = ray.origin.SquareDistanceTo(Pos.X + (b.X1 + b.X2) / 2, Pos.InternalY + (b.Y1 + b.Y2) / 2, Pos.Z + (b.Z1 + b.Z2) / 2);
            best = Math.Min(best, d);
        }
        if (hit)
            intersectionDistance = best;
        return hit;
    }

    /// <summary>The distance from <paramref name="point"/> to the nearest point of the trunk's
    /// turned boxes, blocks.</summary>
    public double DistanceTo(Vec3d point)
    {
        double best = double.MaxValue;
        foreach (var b in TrunkBoxes.Turned(TypeClass, Pos.Yaw))
        {
            double dx = Math.Max(Math.Max(Pos.X + b.X1 - point.X, 0), point.X - (Pos.X + b.X2));
            double dy = Math.Max(Math.Max(Pos.Y + b.Y1 - point.Y, 0), point.Y - (Pos.Y + b.Y2));
            double dz = Math.Max(Math.Max(Pos.Z + b.Z1 - point.Z, 0), point.Z - (Pos.Z + b.Z2));
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        return best;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (mode == EnumInteractMode.Interact && Api.Side == EnumAppSide.Server && TrunkGrab.Wants(byEntity, itemslot, this)
            && TrunkEntitySystem.Of(Api).Grabs is { } grabs && byEntity is EntityPlayer { Player: IServerPlayer player })
        {
            grabs.TryStart(player, this);
            return;
        }
        base.OnInteract(byEntity, itemslot, hitPosition, mode);
    }

    public override string GetName() => Trunk?.GetName() ?? base.GetName();

    public override string GetInfoText()
    {
        var sb = new StringBuilder();
        if (Trunk is { } stack && World != null)
        {
            string wood = Trunks.Wood(stack, World) ?? "unknown";
            sb.AppendLine(Lang.Get("loggingmod:treetrunk-info-wood", Lang.Get("loggingmod:wood-" + wood, wood)));
            sb.AppendLine(Lang.Get("loggingmod:treetrunk-info-logs", Logs));
            int branches = stack.Attributes.GetInt(Trunks.BranchCountKey);
            if (branches > 0)
                sb.AppendLine(Lang.Get("loggingmod:treetrunk-info-branches", branches));
            if (Trunks.IsDebarked(stack))
                sb.AppendLine(Lang.Get("seraphhorizons:trunkentities-info-debarked"));
            sb.AppendLine(Lang.Get("seraphhorizons:trunkentities-info-weight", (int)Math.Round(Properties.Weight)));
        }
        if (Grabbed && World?.GetEntityById(GrabbedBy) is EntityPlayer holder)
            sb.AppendLine(Lang.Get("seraphhorizons:trunkentities-info-grabbed", holder.Player?.PlayerName ?? ""));
        sb.Append(base.GetInfoText());
        return sb.ToString();
    }

    public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
    {
        var help = new List<WorldInteraction>(base.GetInteractionHelp(world, es, player) ?? []);
        var system = TrunkEntitySystem.Of(world.Api);
        if (system.Enabled)
        {
            help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:trunkentities-help-grab", MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
            if (world.GetItem(new AssetLocation("game:rope")) is { } rope)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:trunkentities-help-rope", MouseButton = EnumMouseButton.Right, Itemstacks = [new ItemStack(rope)] });
            if (system.CarryOn)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:trunkentities-help-carry", MouseButton = EnumMouseButton.Right, HotKeyCode = "sneak", RequireFreeHand = true });
            foreach (var provider in HelpProviders)
                help.AddRange(provider(this, player));
        }
        return help.ToArray();
    }
}
