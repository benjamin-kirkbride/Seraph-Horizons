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

    /// <summary>The watched attribute that held the cloth id of a grab's rope when the grab was a
    /// game rope; only read now to clean such a rope out of an old save.</summary>
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
    /// entity (removed, nothing dropped). A stack of the other display class (an xl trunk axed down
    /// to lg) goes to a new entity of that type at the same place and yaw, and this one is removed.
    /// Returns the entity that now holds the trunk, or null. Server side.
    /// </summary>
    public EntityTrunk? SetTrunk(ItemStack? stack)
    {
        if (stack == null || World == null || Trunks.StoredLogs(stack, World) <= 0)
        {
            if (Alive)
                Die(EnumDespawnReason.Removed);
            return null;
        }
        if (stack.ResolveBlockOrItem(World) && stack.Block?.Variant["size"] is { } size && TrunkBox.ClassOf(size) is var cls
            && cls != TrunkClass.None && cls != TypeClass && Alive)
        {
            var moved = TrunkSpawns.Spawn(World, stack, new Vec3d(Pos.X, Pos.Y, Pos.Z), Pos.Yaw, Pos.Dimension);
            if (moved != null)
            {
                Die(EnumDespawnReason.Removed);
                return moved;
            }
        }
        WatchedAttributes.SetItemstack(TrunkKey, stack);
        WatchedAttributes.MarkPathDirty(TrunkKey);
        UpdateWeight();
        return this;
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
        FitSelectionBox();
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
    /// trunk along its length, whichever way it lies. The nearest hit wins, and the tester is left
    /// holding that hit's point, which the game reads straight after as the selection's hit position.
    /// </summary>
    public override bool IntersectsRay(Ray ray, AABBIntersectionTest interesectionTester, out double intersectionDistance, ref int selectionBoxIndex)
    {
        intersectionDistance = 0;
        if (!Alive)
            return false;
        Cuboidf? bestBox = null;
        double best = double.MaxValue;
        foreach (var b in TrunkBoxes.Turned(TypeClass, Pos.Yaw))
        {
            var box = new Cuboidf(b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2);
            if (!interesectionTester.RayIntersectsWithCuboid(box, Pos.X, Pos.InternalY, Pos.Z))
                continue;
            double d = ray.origin.SquareDistanceTo(interesectionTester.hitPosition);
            if (d < best)
            {
                best = d;
                bestBox = box;
            }
        }
        if (bestBox == null)
            return false;
        interesectionTester.RayIntersectsWithCuboid(bestBox, Pos.X, Pos.InternalY, Pos.Z);
        intersectionDistance = best;
        return true;
    }

    /// <summary>
    /// Fits <see cref="Entity.SelectionBox"/> around the trunk's turned boxes. The game measures
    /// reach to it (the server's interaction range check, and attack range on both sides), so with
    /// the type's square 2 × 2 or 1 × 1 hitbox only the middle of a long trunk was in reach.
    /// The collision box stays square: shoving and the physics read that.
    /// </summary>
    private void FitSelectionBox()
    {
        if (_selectionYaw == Pos.Yaw && ReferenceEquals(SelectionBox, _selectionBox))
            return;
        float x1 = float.MaxValue, y1 = float.MaxValue, z1 = float.MaxValue;
        float x2 = float.MinValue, y2 = float.MinValue, z2 = float.MinValue;
        foreach (var b in TrunkBoxes.Turned(TypeClass, Pos.Yaw))
        {
            x1 = Math.Min(x1, (float)b.X1); y1 = Math.Min(y1, (float)b.Y1); z1 = Math.Min(z1, (float)b.Z1);
            x2 = Math.Max(x2, (float)b.X2); y2 = Math.Max(y2, (float)b.Y2); z2 = Math.Max(z2, (float)b.Z2);
        }
        if (x1 > x2)
            return;
        _selectionYaw = Pos.Yaw;
        _selectionBox = SelectionBox = new Cuboidf(x1, y1, z1, x2, y2, z2);
    }

    private float _selectionYaw = float.NaN;
    private Cuboidf? _selectionBox;

    /// <summary>The trunk's weight on land, as worked out from its logs and kept in the watched
    /// <see cref="WeightKey"/>. <see cref="Properties"/>' weight is this on land and lighter afloat
    /// on the server (<see cref="TrunkPull.EffectiveWeight"/>), for the rope's pull.</summary>
    public float LandWeight => WatchedAttributes.GetFloat(WeightKey, Properties.Weight);

    /// <summary>Whether the trunk floats in, or lies in, water.</summary>
    public bool Afloat => Swimming || FeetInLiquid;

    public override void OnGameTick(float dt)
    {
        // Motion as the pulls left it (the grab's, or a rope's), before this tick's physics
        // stops it against a rise.
        double mx = Pos.Motion.X, mz = Pos.Motion.Z;
        base.OnGameTick(dt);
        FitSelectionBox();
        TrunkRope.Tick(this, dt);
        if (Api is { Side: EnumAppSide.Server } && Alive)
        {
            Properties.Weight = (float)TrunkPull.EffectiveWeight(LandWeight, Afloat);
            StepUp(mx, mz);
        }
    }

    // Lifts a trunk being pulled against a rise of at most a block onto it (TrunkStep), in one
    // go. Not afloat (water lifts it) and not while falling.
    private void StepUp(double mx, double mz)
    {
        if (Afloat || Pos.Motion.Y < -0.1)
            return;
        var accessor = World.BlockAccessor;
        var cell = new BlockPos(Pos.Dimension);
        double lift = TrunkStep.Lift(TrunkBoxes.Turned(TypeClass, Pos.Yaw), Pos.X, Pos.Y, Pos.Z, mx, mz,
            (x, y, z) =>
            {
                cell.Set(x, y, z);
                var boxes = accessor.GetBlock(cell, BlockLayersAccess.MostSolid).GetCollisionBoxes(accessor, cell);
                return boxes is { Length: > 0 };
            });
        if (lift <= 0)
            return;
        Pos.Y += lift;
        if (Pos.Motion.Y < 0)
            Pos.Motion.Y = 0;
        // Keep the pull's way on, which the collision just took.
        Pos.Motion.X = mx;
        Pos.Motion.Z = mz;
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
        // While a player's grab holds it, another player's empty hand does nothing (not even
        // sneak + click, which would reach ropetieable or Carry On's pick-up under the holder).
        if (mode == EnumInteractMode.Interact && Grabbed && byEntity.EntityId != GrabbedBy && (itemslot == null || itemslot.Empty))
        {
            if (Api.Side == EnumAppSide.Server && byEntity is EntityPlayer { Player: IServerPlayer other })
                other.SendIngameError("trunkentities-grabbed", Lang.GetL(other.LanguageCode, "seraphhorizons:trunkentities-error-grabbed"));
            return;
        }
        // A grab click stops here on both sides, never reaching ropetieable (whose empty hand
        // would otherwise act on the client too). The game repeats the interact while the button
        // is held; TryStart ignores a repeat for the trunk already held.
        if (mode == EnumInteractMode.Interact && TrunkGrab.Wants(byEntity, itemslot, this))
        {
            if (Api.Side == EnumAppSide.Server && TrunkEntitySystem.Of(Api).Grabs is { } grabs
                && byEntity is EntityPlayer { Player: IServerPlayer player })
                grabs.TryStart(player, this, hitPosition);
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
        if (Afloat)
            sb.AppendLine(Lang.Get("seraphhorizons:trunkentities-info-afloat"));
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
