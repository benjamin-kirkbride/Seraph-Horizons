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
/// up as an item; it is driven on foot by a player attached to one end (its <c>seatable</c>'s one
/// seat, <see cref="TrunkDriveSeat"/>, the maths in <see cref="TrunkDrive"/>), roped through the
/// game's <c>ropetieable</c>, shoved, floated, or shouldered through Carry On.
/// </summary>
public class EntityTrunk : Entity, ISeatInstSupplier
{
    /// <summary>The watched attribute holding the trunk's stack.</summary>
    public const string TrunkKey = "trunk";

    /// <summary>The watched attribute holding the server's weight for the trunk.</summary>
    public const string WeightKey = "seraphhorizons:weight";

    /// <summary>The watched attribute holding the entity id of the player driving the trunk (0 or
    /// missing: none). Set just before the player is mounted and removed when they leave the seat;
    /// whatever refuses a trunk someone is moving reads it (<see cref="Grabbed"/>).</summary>
    public const string GrabbedByKey = "seraphhorizons:grabbedBy";

    /// <summary>The watched attribute holding the end (±1, <see cref="TrunkPull"/>) the driver took.</summary>
    public const string DriveEndKey = "seraphhorizons:driveEnd";

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

    /// <summary>Whether a player is driving it (its mark, <see cref="GrabbedByKey"/>).</summary>
    public bool Grabbed => GrabbedBy != 0;

    /// <summary>The entity id of the player driving it, 0 for none.</summary>
    public long GrabbedBy => WatchedAttributes.GetLong(GrabbedByKey);

    /// <summary>The entity id of the player driving it, 0 for none (<see cref="GrabbedBy"/>).</summary>
    public long DriverId => GrabbedBy;

    /// <summary>The trunk's one seat, the driver's.</summary>
    public TrunkDriveSeat? DriveSeat => GetBehavior<EntityBehaviorSeatable>()?.Seats?.FirstOrDefault() as TrunkDriveSeat;

    /// <summary>The trunk's <c>seatable</c>, the game's mount behaviour; its <c>Controller</c> says
    /// who ticks the physics (<see cref="EntityBehaviorTrunkPhysics"/>).</summary>
    public EntityBehaviorSeatable? Seatable => GetBehavior<EntityBehaviorSeatable>();

    /// <summary>How long, ms, the server waits for the driver's client to send the trunk's position
    /// before it ticks the trunk's physics itself (and from the mount, for the first one).</summary>
    public const long ClientPositionTimeoutMs = 500;

    /// <summary>The player standing in the driver's place, or null.</summary>
    public EntityPlayer? Driver => DriveSeat?.Passenger as EntityPlayer;

    /// <summary>Whether a player stands in the driver's place.</summary>
    public bool Driven => Driver != null;

    /// <summary>The end (±1) the driver took.</summary>
    public int DriveEnd => WatchedAttributes.GetInt(DriveEndKey, 1) >= 0 ? 1 : -1;

    public IMountableSeat CreateSeat(IMountable mountable, string seatId, SeatConfig config) =>
        new TrunkDriveSeat(mountable, seatId, config);

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
        // A drive never outlives the session it was made in: a trunk saved while driven forgets
        // its driver before the seatable (in base.Initialize and AfterInitialized) reads its seat
        // data, so the seat's own re-mount is refused (TrunkDriveSeat.CanMount).
        if (api.Side == EnumAppSide.Server)
            WatchedAttributes.RemoveAttribute(GrabbedByKey);
        base.Initialize(properties.Clone(), api, InChunkIndex3d);
        if (GetBehavior<EntityBehaviorSeatable>() is { } seatable)
            // Only the drive's own click mounts (OnInteract), never the seatable's.
            seatable.CanSit += (EntityAgent _, out string error) => { error = null!; return false; };
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
        ClearLegacyRope();
    }

    /// <summary>A save from when the grab was a game rope: the rope's cloth id (and the rope
    /// itself, if the game still has it) goes.</summary>
    private void ClearLegacyRope()
    {
        int id = WatchedAttributes.GetInt(GrabClothKey);
        if (id == 0)
            return;
        var tieable = GetBehavior<EntityBehaviorRopeTieable>();
        var cloth = Api.ModLoader.GetModSystem<ClothManager>();
        if (cloth?.GetClothSystem(id) is { } sys)
        {
            tieable?.Detach(sys);
            cloth.UnregisterCloth(id);
        }
        else if (tieable?.ClothIds is { } ids)
        {
            ids.RemoveInt(id);
            if (ids.value.Length == 0)
                WatchedAttributes.RemoveAttribute("clothIds");
        }
        WatchedAttributes.RemoveAttribute(GrabClothKey);
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

    // The drive's eased speed along the axis (blocks per second, + away from the driver) and turn
    // (radians per second), on the side that ticks the physics.
    private double _along, _turn;
    private int _logs = -1;

    /// <summary>The drive's eased speed along the axis now, blocks per second, positive away from
    /// the driver, on this side (0 on a side that does not tick the physics). For the probe.</summary>
    public double DriveAlong => _along;

    /// <summary>The drive's eased turn now, radians per second, on this side. For the probe.</summary>
    public double DriveTurn => _turn;

    // When the server last heard the trunk's position from the driver's client (or the drive began),
    // the world's ElapsedMilliseconds; long.MinValue: never.
    private long _clientPositionAt = long.MinValue;

    /// <summary>Whether the server has had the trunk's position from its driver's client within
    /// <see cref="ClientPositionTimeoutMs"/> (counting from the mount), so that client ticks it.</summary>
    public bool ClientPredicting =>
        Api?.Side == EnumAppSide.Server && Driver is { Alive: true }
        && World.ElapsedMilliseconds - _clientPositionAt <= ClientPositionTimeoutMs;

    /// <summary>The trunk physics' word that the driver's client sent its position: the client
    /// goes on ticking it (the seatable's <c>Controller</c>, at once). Server side.</summary>
    public void ClientPositionReceived()
    {
        if (Api?.Side != EnumAppSide.Server || Driver is not { Alive: true } driver)
            return;
        _clientPositionAt = World.ElapsedMilliseconds;
        if (Seatable is { } seatable && seatable.Controller != driver)
            seatable.Controller = driver;
    }

    /// <summary>
    /// Who ticks the trunk's physics, by the seatable's <c>Controller</c>, which the game's
    /// physics manager and the driver's player physics read. On a client, the driver, so theirs
    /// predicts. On the server, the driver while their client has sent the trunk's position in the
    /// last <see cref="ClientPositionTimeoutMs"/>, else none, so the server ticks it (a player with
    /// no client, as in Atlas, or a client that has gone quiet).
    /// </summary>
    private void UpdateController()
    {
        if (Seatable is not { } seatable)
            return;
        Entity? want = Api.Side == EnumAppSide.Server ? (ClientPredicting ? Driver : null) : Driver;
        if (seatable.Controller != want)
            seatable.Controller = want;
        NoteLocalControl();
    }

    // Client side: whether this client's player ticked the trunk's physics when last looked.
    private bool _localControl;

    /// <summary>
    /// Client side, whenever the controller may have changed: when this client's player stops
    /// ticking the trunk (letting go, or anything else that takes the controller away), its
    /// <c>interpolateposition</c> is reset to the trunk's pose now, the predicted one. While the
    /// player predicted, the game sent this client none of the trunk's positions (the server
    /// relays a driver's mount positions to the other players only) and the interpolation left a
    /// controlled mount's position alone, so its last two snapshots are where the drive began; the
    /// first position from the server after letting go would otherwise be eased in from there, a
    /// dash from the drive's start (the let-go snap). The reset is the interpolation's own
    /// teleport (<c>OnReceivedServerPos(isTeleport: true)</c>: the queue emptied, both snapshots
    /// the pose now), so the server's next position eases in from where the trunk is.
    /// </summary>
    private void NoteLocalControl()
    {
        if (Api is not ICoreClientAPI capi)
            return;
        var me = capi.World.Player?.Entity;
        bool now = me != null && Seatable?.Controller == me;
        if (_localControl && !now)
            ResetInterpolation();
        _localControl = now;
    }

    private void ResetInterpolation()
    {
        if (GetBehavior<EntityBehaviorInterpolatePosition>() is not { } interpolation)
            return;
        // The snapshots' interval comes from the last packet's tick gap; one packet's worth.
        Attributes.SetInt("tickDiff", 1);
        var handled = EnumHandling.PassThrough;
        interpolation.OnReceivedServerPos(true, ref handled);
    }

    /// <summary>The seat's word that <paramref name="agent"/> took the driver's place: the server
    /// gives their client <see cref="ClientPositionTimeoutMs"/> to start sending positions.</summary>
    public void DriverMounted(EntityAgent agent)
    {
        _along = _turn = 0;
        if (Api?.Side == EnumAppSide.Server)
            _clientPositionAt = World.ElapsedMilliseconds;
        UpdateController();
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        FitSelectionBox();
        TrunkRope.Tick(this, dt);
        var seat = DriveSeat;
        if (seat?.Passenger is EntityAgent driver)
        {
            bool server = Api.Side == EnumAppSide.Server;
            if (server && (!driver.Alive || driver.State == EnumEntityState.Despawned || !Alive
                           || driver is EntityPlayer { Player: IServerPlayer { ConnectionState: not EnumClientState.Playing } }))
                driver.TryUnmount();
            else
            {
                HoldDriver(driver, seat);
                // The server's copy of the driver stands in their place (the driver's client
                // reports its own, from the same seat; a player without one, as in Atlas, has only this).
                if (server)
                {
                    var at = seat.SeatPosition;
                    driver.Pos.SetPos(at.X, at.Y, at.Z);
                }
            }
        }
        UpdateController();
        if (Alive)
            _logs = Logs;
        if (Api is { Side: EnumAppSide.Server } && Alive)
        {
            Properties.Weight = (float)TrunkPull.EffectiveWeight(LandWeight, Afloat);
            if (Grabbed && seat?.Passenger == null)
                ClearDrive();
            // As good as solid: agents in its boxes are moved out (the client moves its own player).
            TrunkSolid.PushAll(this);
        }
    }

    // The driver's body faces along the trunk, over it, within a little (Cartwright's sled's rider
    // has 0.3); the head may look a quarter turn either way.
    private void HoldDriver(EntityAgent driver, TrunkDriveSeat seat)
    {
        if (driver is not EntityPlayer player)
            return;
        float face = seat.SeatPosition.Yaw;
        if (player.BodyYawLimits == null)
            player.BodyYawLimits = new AngleConstraint(face, BodyYawRange);
        else
            (player.BodyYawLimits.X, player.BodyYawLimits.Y) = (face, BodyYawRange);
        if (player.HeadYawLimits == null)
            player.HeadYawLimits = new AngleConstraint(face, GameMath.PIHALF);
        else
            (player.HeadYawLimits.X, player.HeadYawLimits.Y) = (face, GameMath.PIHALF);
    }

    private const float BodyYawRange = 0.3f;

    /// <summary>
    /// One physics tick of the drive, from <see cref="EntityBehaviorTrunkPhysics"/> on whichever
    /// side ticks it (the driver's client, or the server): after the game's drag and gravity,
    /// before its collision. Everything it reads is on both sides: the seat's controls, the pose,
    /// the stored logs and the water flags. The driver's keys (the seat's
    /// controls) ease the speed along the axis and the turn (<see cref="TrunkDrive"/>); the turn
    /// goes through the multi-box physics' own yaw adjustment with a push, as the game's boat
    /// turns, so the swinging boxes shove the trunk off what they swing into and a turn with no way
    /// out is refused; the speed sets the horizontal motion outright. Then the step-up, from that
    /// motion (or a rope's, undriven).
    /// </summary>
    public void BeforeCollision(EntityBehaviorPassivePhysicsMultiBox physics, EntityPos pos, float dtFactor)
    {
        float dt = dtFactor / 60f;
        var controls = DriveSeat is { Passenger: not null } seat ? seat.Controls : null;
        if (controls != null)
        {
            int logs = _logs >= 0 ? _logs : Logs;
            bool afloat = Afloat;
            _along = TrunkDrive.Ease(_along, TrunkDrive.Along(controls.Forward, controls.Backward, TrunkDrive.Speed(logs, afloat)), dt);
            _turn = TrunkDrive.Ease(_turn, TrunkDrive.Turning(controls.Left, controls.Right, TrunkDrive.Turn(logs, afloat)), dt);
            if (_turn != 0)
            {
                float yaw = GameMath.Mod(pos.Yaw + (float)(_turn * dt), GameMath.TWOPI);
                if (physics.AdjustCollisionBoxesToYaw(dtFactor, true, yaw))
                    pos.Yaw = yaw;
                else
                    _turn = 0;
            }
            var (mx, mz) = TrunkDrive.DriveMotion(pos.Yaw, DriveEnd, _along);
            pos.Motion.X = mx;
            pos.Motion.Z = mz;
        }
        else
            _along = _turn = 0;
        StepUp(pos);
    }

    // Lifts a trunk moving against a rise of at most a block onto it (TrunkStep), in one go. Not
    // afloat (water lifts it) and not while falling.
    private void StepUp(EntityPos pos)
    {
        if (Afloat || pos.Motion.Y < -0.1)
            return;
        double mx = pos.Motion.X, mz = pos.Motion.Z;
        var accessor = World.BlockAccessor;
        var cell = new BlockPos(pos.Dimension);
        double lift = TrunkStep.Lift(TrunkBoxes.Turned(TypeClass, pos.Yaw), pos.X, pos.Y, pos.Z, mx, mz,
            (x, y, z) =>
            {
                cell.Set(x, y, z);
                var boxes = accessor.GetBlock(cell, BlockLayersAccess.MostSolid).GetCollisionBoxes(accessor, cell);
                return boxes is { Length: > 0 };
            });
        if (lift <= 0)
            return;
        pos.Y += lift;
        if (pos.Motion.Y < 0)
            pos.Motion.Y = 0;
    }

    /// <summary>Takes <paramref name="player"/> onto the end of the trunk nearer
    /// <paramref name="hit"/> (the clicked point, or the player when null) as its driver; false,
    /// with an in-game error, when someone else drives it. Server side.</summary>
    public bool TryDrive(IServerPlayer player, Vec3d? hit = null)
    {
        if (player.Entity is not { Alive: true } agent || !Alive || DriveSeat is not { } seat)
            return false;
        if (Grabbed && GrabbedBy != agent.EntityId)
        {
            Refuse(player);
            return false;
        }
        if (seat.Passenger == agent)
            return true;
        // The hit is a world position; should a caller hand over one relative to the trunk, it
        // is far from the trunk, so it is taken as relative.
        var at = hit ?? agent.Pos.XYZ;
        if (hit != null && hit.SquareDistanceTo(Pos.XYZ) > 64)
            at = Pos.XYZ.Add(hit);
        int end = TrunkPull.NearerEnd(Pos.X, Pos.Z, Pos.Yaw, at.X, at.Z);
        WatchedAttributes.SetInt(DriveEndKey, end);
        WatchedAttributes.SetLong(GrabbedByKey, agent.EntityId);
        _along = _turn = 0;
        if (!agent.TryMount(seat))
        {
            ClearDrive();
            return false;
        }
        return true;
    }

    private static void Refuse(IServerPlayer player) =>
        player.SendIngameError("trunkentities-grabbed", Lang.GetL(player.LanguageCode, "seraphhorizons:trunkentities-error-grabbed"));

    /// <summary>The seat's word that <paramref name="agent"/> left it (sneak, death, leaving the
    /// game, another mount, the trunk going): the mark goes.</summary>
    public void DriverLeft(EntityAgent agent)
    {
        // The physics goes back to the server (the controller goes), and the trunk stops where it
        // is rather than gliding on the drive's last motion.
        _along = _turn = 0;
        _clientPositionAt = long.MinValue;
        if (Seatable is { } seatable && seatable.Controller == agent)
            seatable.Controller = null;
        Pos.Motion.X = Pos.Motion.Z = 0;
        // On the driver's client the interpolation starts again from the predicted pose.
        NoteLocalControl();
        if (Api?.Side == EnumAppSide.Server && GrabbedBy == agent.EntityId)
            ClearDrive();
    }

    private void ClearDrive() => WatchedAttributes.RemoveAttribute(GrabbedByKey);

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        // A trunk that goes (taken by a machine or Carry On, cut to the other class, unloaded)
        // lets its driver go.
        if (Api?.Side == EnumAppSide.Server && Driver is { } driver)
            driver.TryUnmount();
        base.OnEntityDespawn(despawn);
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

    /// <summary>Whether a click is a drive: an empty hand, not sneaking (Carry On's), and the
    /// trunk has no rope of the game's own tied to it (an empty hand takes that rope off, as the
    /// game does). Side-independent, so the client also keeps a drive click from <c>ropetieable</c>.</summary>
    public static bool WantsDrive(EntityAgent byEntity, ItemSlot? slot, EntityTrunk trunk) =>
        byEntity is EntityPlayer && (slot == null || slot.Empty)
        && !byEntity.Controls.ShiftKey && !byEntity.Controls.Sneak
        && !trunk.HasOtherRope();

    private bool HasOtherRope()
    {
        var ids = GetBehavior<EntityBehaviorRopeTieable>()?.ClothIds?.value;
        if (ids == null || ids.Length == 0)
            return false;
        // An old save's grab rope (GrabClothKey) is not a real one; AfterInitialized removes it.
        int legacy = WatchedAttributes.GetInt(GrabClothKey);
        return ids.Any(id => id != legacy);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (mode == EnumInteractMode.Interact && Grabbed)
        {
            // While a player drives it nothing else is done to it: another player's click (an
            // empty hand, sneaking too, which would reach Carry On's pick-up, or a rope) stops
            // here, with the error; the driver's own click changes nothing.
            if (byEntity.EntityId != GrabbedBy && Api.Side == EnumAppSide.Server && byEntity is EntityPlayer { Player: IServerPlayer other })
                Refuse(other);
            return;
        }
        // A drive click stops here on both sides, never reaching ropetieable (whose empty hand
        // would otherwise act on the client too) or the seatable (which refuses: CanSit).
        if (mode == EnumInteractMode.Interact && WantsDrive(byEntity, itemslot, this))
        {
            if (Api.Side == EnumAppSide.Server && byEntity is EntityPlayer { Player: IServerPlayer player })
                TryDrive(player, hitPosition);
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
            if (!Grabbed)
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
