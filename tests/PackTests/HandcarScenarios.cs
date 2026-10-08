using System.Text.Json;
using Atlas.XUnit;
using SeraphHorizons.Mod.Handcar;
using SeraphHorizons.Mod.Handcar.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Handcar against the pinned Yang's Transport Tycoon: the car is registered
/// with its item, recipe and the riders' animations; placed on standard-gauge track by its item, it
/// takes two riders standing on its deck, the rear one facing its front and the front one its back;
/// pumping drives it to the configured top speeds, one rider and two, pumping back brakes it and
/// takes it back the other way, two pumping against each other hold it, left and right step the
/// branch selector, and pumping costs a survival rider what sprinting does; Yang's wrench takes it
/// apart into its item, and a sneak click picks it up. And the riders' hands: the game's own animator,
/// run on the patched seraph and on the car's shape at the same frames, puts each hand on its handle.
/// Each scenario builds on a granite floor of its own, high over the spawn.
/// <para>Its own class on the plain world rather than a part of <see cref="SharedWorldScenarios"/>:
/// it joins four players (two riders, one with the wrench and the locomotive's driver of
/// <c>LocomotiveSeatsScenarios.cs</c>, a part of this class), and the shared world has none of
/// the server's sixteen left.</para>
/// </summary>
[AtlasWorld]
public partial class HandcarScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private static readonly AssetLocation HandcarItem = new("seraphhorizons", "handcar");
    private const string SgRail = "yangtransport:widerails_straight-we-metal";   // runs along z

    private HandcarSystem Handcars => HandcarSystem.Of(World.Api);

    // Progress in the server log, live, and in the test's output (which only shows at the end).
    private void HandcarLog(string what)
    {
        W.Logger.Notification("[handcar-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>A straight standard-gauge track running north to south (+z) from <paramref name="origin"/>,
    /// <paramref name="length"/> long, on granite, with air above; its chunks loaded first (near the
    /// spawn, which the server keeps loaded).</summary>
    private async Task<BlockPos> HandcarTrack(BlockPos origin, int length)
    {
        int size = GlobalConstants.ChunkSize;
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - 3) / size; cx <= (origin.X + 4) / size; cx++)
            for (int cz = (origin.Z - 4) / size; cz <= (origin.Z + length + 4) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        foreach (var c in columns)
            ((ICoreServerAPI)World.Api).WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 1200);
        for (int dz = -3; dz < length + 3; dz++)
        for (int dx = -2; dx <= 3; dx++)
        {
            World.SetBlock("game:rock-granite", origin.AddCopy(dx, -1, dz));
            for (int dy = 0; dy <= 4; dy++)
                World.SetBlock("game:air", origin.AddCopy(dx, dy, dz));
        }
        for (int dz = 0; dz < length; dz++)
            World.SetBlock(SgRail, origin.AddCopy(0, 0, dz));
        await World.Ticks(10);
        HandcarLog($"track of {length} at {origin}");
        return origin;
    }

    /// <summary>A player in survival, empty handed, at <paramref name="at"/>, looking south (+z).</summary>
    private async Task<IServerPlayer> HandcarPlayer(string name, BlockPos at)
    {
        var p = await World.JoinPlayer(name);
        await p.TeleportTo(at);
        await World.Ticks(5);
        var player = p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
        player.Entity.Controls.ShiftKey = player.Entity.Controls.Sneak = false;
        player.Entity.Pos.Yaw = 0f;                                 // VS yaw 0: facing +z
        return player;
    }

    /// <summary>Places a handcar on the track at <paramref name="rail"/> with its item, as a player does.</summary>
    private async Task<Entity> PlaceHandcar(IServerPlayer by, BlockPos rail)
    {
        var item = W.GetItem(HandcarItem);
        Assert.NotNull(item);
        var before = LoadedHandcars().Select(e => e.EntityId).ToHashSet();
        var slot = new DummySlot(new ItemStack(item));
        var handling = EnumHandHandling.NotHandled;
        item.OnHeldInteractStart(slot, by.Entity, new BlockSelection { Position = rail.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.1, 0.5) },
                                 null, true, ref handling);
        await World.Ticks(5);
        var placed = LoadedHandcars().Where(e => !before.Contains(e.EntityId)).ToList();
        Assert.True(placed.Count == 1, $"the handcar's item placed {placed.Count} handcars on the track");
        var car = placed[0];
        Assert.True(car.Pos.DistanceTo(rail.ToVec3d().Add(0.5, 0, 0.5)) < 3, $"the handcar is at {car.Pos.XYZ}, not on the rail at {rail}");
        Assert.True(slot.Empty, "a survival player's handcar item was not used up");
        return car!;
    }

    private bool Loaded(Entity e) => ((ICoreServerAPI)World.Api).World.LoadedEntities.ContainsKey(e.EntityId);

    private List<Entity> LoadedHandcars() =>
        ((ICoreServerAPI)World.Api).World.LoadedEntities.Values.Where(e => e.Alive && e.Code.Equals(HandcarSystem.EntityCode)).ToList();

    /// <summary>Every handcar left from an earlier scenario (one that failed half way) gone, its riders off.</summary>
    private async Task ClearHandcars()
    {
        foreach (var car in LoadedHandcars())
        {
            foreach (var seat in car.GetBehavior<EntityBehaviorSeatable>()?.Seats ?? [])
                (seat.Passenger as EntityAgent)?.TryUnmount();
            car.Die(EnumDespawnReason.Removed);
        }
        await World.Ticks(2);
    }

    /// <summary>The bit of the car's pumping mask (<see cref="EntityBehaviorHumanPowered.PumpingKey"/>) for a seat: its place in the seatable's list.</summary>
    private static int PumpBit(Entity car, string id) =>
        1 << car.GetBehavior<EntityBehaviorSeatable>()!.Seats.ToList().FindIndex(s => s.SeatId == id);

    /// <summary>What Yang's removal checks read, for a failure's message.</summary>
    private static string YangState(Entity car, CollectibleBehavior tool, IServerPlayer player)
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                                                   | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        object? Field(object o, string name) => o.GetType().GetField(name, Any)?.GetValue(o);
        var canTarget = tool.GetType().GetMethod("CanTargetBeDeconstructed", Any)?.Invoke(null, [car]);
        var hasTools = tool.GetType().GetMethod("HasRequiredTools", Any)?.Invoke(null, [player.InventoryManager.ActiveHotbarSlot, player.Entity]);
        var passengers = car.GetBehavior<EntityBehaviorSeatable>()?.Seats.Count(s => s.Passenger != null);
        object?[] removeArgs = [player.Entity, null];
        object? canRemove;
        try { canRemove = car.GetType().GetMethod("ServerCanManuallyRemove", Any)?.Invoke(car, removeArgs); }
        catch (Exception e) { canRemove = e.InnerException?.ToString() ?? e.ToString(); }
        return $"canTarget {canTarget}, hasTools {hasTools}, canRemove {canRemove} ({removeArgs[1]}), steam {Field(car, "SteamEngineBehaviour")}, convoyHead {Field(car, "ConvoyHeadID")} (own {car.EntityId}), derailed {Field(car, "Derailed")}, "
               + $"speed {Field(car, "Speed")}, passengers {passengers}, storage {Field(car, "StorageBehaviour")}, alive {car.Alive}";
    }

    private static IMountableSeat Seat(Entity car, string id) =>
        car.GetBehavior<EntityBehaviorSeatable>()!.Seats.Single(s => s.SeatId == id);

    private double Speed(Entity car) => Handcars.Bridge!.Speed(car);

    private int Motion(Entity car) => Handcars.Bridge!.Motion(car);

    private static Vec3d Forward(Entity car) => new(Math.Sin(car.Pos.Yaw), 0, Math.Cos(car.Pos.Yaw));

    [AtlasScenario]
    public void Handcar_is_in_the_game_with_its_item_recipe_animations_and_deconstruction()
    {
        Assert.True(Handcars.Enabled);
        Assert.NotNull(Handcars.Bridge);
        Assert.True(World.Api.World.Config.GetBool(HandcarSystem.RunningKey));
        var type = W.GetEntityType(HandcarSystem.EntityCode);
        Assert.NotNull(type);
        Assert.Equal(YangBridge.EntityClass, type!.Class);
        Assert.NotNull(W.GetItem(HandcarItem));
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.Equals(HandcarItem) == true);
        Assert.NotNull(Handcars.Rig);
        // the riders' animations: on the seraph, and the player's metadata for them
        var seraph = World.Api.Assets.Get(new AssetLocation("game:shapes/entity/humanoid/seraph-faceless.json")).ToObject<Shape>();
        var player = W.GetEntityType(new AssetLocation("game:player"))!;
        foreach (var seat in Handcars.Rig!.Seats.Values)
            foreach (var code in new[] { seat.Grip, seat.Pump })
            {
                Assert.Contains(seraph.Animations, a => a.Code == code);
                var meta = player.Client.Animations.Single(m => m.Code == code);
                Assert.Equal(EnumAnimationBlendMode.Average, meta.BlendMode);
                Assert.True(meta.AnimationSpeed < 0.001f, $"{code} would run on its own");
            }
        // Yang's wrench patch: the wrench carries the deconstruction behaviour, and the car is deconstructible into itself
        var wrench = W.GetItem(new AssetLocation("game:wrench-iron"));
        Assert.NotNull(wrench);
        Assert.Contains(wrench!.CollectibleBehaviors, b => b.GetType().FullName == "YangTransport.CollectibleBehaviorRailVehicleDeconstructTool");
        Assert.True(type.Attributes["deconstructible"].AsBool());
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("handcar", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Handcar_is_pumped_braked_reversed_held_and_steered_by_its_riders()
    {
        await ClearHandcars();
        var track = await HandcarTrack(World.Spawn.AddCopy(-130, 45, -45), 90);
        var rear = await HandcarPlayer("handcarrear", track.AddCopy(2, 0, 8));
        var front = await HandcarPlayer("handcarfront", track.AddCopy(2, 0, 10));
        var car = await PlaceHandcar(rear, track.AddCopy(0, 0, 12));
        await World.Ticks(10);
        var fwd = Forward(car);
        HandcarLog($"handcar at {car.Pos.XYZ}, yaw {car.Pos.Yaw}");

        // the riders: on the deck at each end, the front one turned round to face the beam
        Assert.True(rear.Entity.TryMount(Seat(car, "rear")));
        Assert.True(front.Entity.TryMount(Seat(car, "front")));
        await World.Ticks(5);
        var rearSeat = Seat(car, "rear").SeatPosition;
        var frontSeat = Seat(car, "front").SeatPosition;
        HandcarLog($"rear seat {rearSeat.XYZ} yaw {rearSeat.Yaw}, front seat {frontSeat.XYZ} yaw {frontSeat.Yaw}");
        Assert.InRange(rearSeat.Y - track.Y, 0.95, 1.05);                    // the deck's top, 0.75 over the rails' top at 0.25
        Assert.InRange(frontSeat.Y - track.Y, 0.95, 1.05);
        double apart = (rearSeat.XYZ - frontSeat.XYZ).Dot(fwd);
        Assert.InRange(-apart, 1.9, 2.1);                                     // 2 blocks apart, the front seat ahead
        Assert.True(Math.Abs(GameMath.AngleRadDistance(rearSeat.Yaw, car.Pos.Yaw)) < 0.1, "the rear rider does not face the car's front");
        Assert.True(Math.Abs(GameMath.AngleRadDistance(frontSeat.Yaw, car.Pos.Yaw + GameMath.PI)) < 0.1, "the front rider does not face the car's back");

        var config = Handcars.Config;
        var rearKeys = Seat(car, "rear").Controls;
        var frontKeys = Seat(car, "front").Controls;
        float rearSat = rear.Entity.GetBehavior<EntityBehaviorHunger>()!.Saturation;
        float frontSat = front.Entity.GetBehavior<EntityBehaviorHunger>()!.Saturation;
        var start = car.Pos.XYZ;

        // one rider pumping forward: the solo top speed
        HandcarLog("mounted; the rear rider pumps");
        rearKeys.Forward = true;
        await World.Ticks(5);
        Assert.Equal(PumpBit(car, "rear"), car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.PumpingKey));
        await World.Until(() => Math.Abs(Speed(car) - config.TopSpeedOne) < 0.05, 400);
        HandcarLog($"solo: speed {Speed(car):F3}, motion {Motion(car)}, rolled {(car.Pos.XYZ - start).Dot(fwd):F2}");
        Assert.InRange(Speed(car), config.TopSpeedOne - 0.05, config.TopSpeedOne + 0.05);
        Assert.Equal(1, Motion(car));
        Assert.True((car.Pos.XYZ - start).Dot(fwd) > 3, "the car did not roll forward");
        await World.Ticks(40);
        Assert.InRange(Speed(car), config.TopSpeedOne - 0.05, config.TopSpeedOne + 0.05);
        // pumping costs the rider what sprinting does (the hunger behaviour charges every 10 s)
        await World.Until(() => rear.Entity.GetBehavior<EntityBehaviorHunger>()!.Saturation < rearSat - 10, 300);
        HandcarLog($"satiety {rear.Entity.GetBehavior<EntityBehaviorHunger>()!.Saturation} from {rearSat}");
        double pumperLoss = rearSat - rear.Entity.GetBehavior<EntityBehaviorHunger>()!.Saturation;
        double riderLoss = frontSat - front.Entity.GetBehavior<EntityBehaviorHunger>()!.Saturation;
        HandcarLog($"satiety: the pumper lost {pumperLoss:F1}, the rider {riderLoss:F1}");
        Assert.True(pumperLoss - riderLoss > 8, $"pumping cost {pumperLoss - riderLoss:F1} satiety over the rider's");

        // both pumping the same way (the front rider pumps back, towards the car's front): the pair speed
        frontKeys.Backward = true;
        await World.Ticks(5);
        Assert.Equal(PumpBit(car, "rear") | PumpBit(car, "front"), car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.PumpingKey));
        await World.Until(() => Math.Abs(Speed(car) - config.TopSpeedTwo) < 0.05, 300);
        HandcarLog($"pair: speed {Speed(car):F3}, motion {Motion(car)}");
        Assert.InRange(Speed(car), config.TopSpeedTwo - 0.05, config.TopSpeedTwo + 0.05);
        Assert.Equal(1, Motion(car));

        // the rear rider pumps back alone: the car brakes to a stop, then goes back the other way
        frontKeys.Backward = false;
        rearKeys.Forward = false;
        rearKeys.Backward = true;
        await World.Until(() => Motion(car) == -1 && Speed(car) > 1, 400);
        HandcarLog($"braked and reversed: speed {Speed(car):F3}, motion {Motion(car)}");
        Assert.Equal(-1, Motion(car));
        // the front rider pumps forward (towards its face: the car's back) too: both drive it back
        frontKeys.Forward = true;
        await World.Until(() => Math.Abs(Speed(car) - config.TopSpeedTwo) < 0.05, 400);
        HandcarLog($"pair back: speed {Speed(car):F3}, motion {Motion(car)}");
        Assert.InRange(Speed(car), config.TopSpeedTwo - 0.05, config.TopSpeedTwo + 0.05);
        Assert.Equal(-1, Motion(car));

        // against each other: the beam locks, the car stops and stays
        frontKeys.Forward = false;
        frontKeys.Backward = true;
        rearKeys.Backward = true;
        await World.Until(() => Speed(car) < 0.03, 400);
        HandcarLog($"locked: speed {Speed(car):F3}");
        await World.Ticks(40);
        Assert.True(Speed(car) < 0.03, $"two riders pumping against each other moved the car at {Speed(car)}");

        // nobody pumping: it coasts down by its drag
        frontKeys.Backward = false;
        rearKeys.Backward = false;
        rearKeys.Forward = true;
        await World.Until(() => Speed(car) > 2, 400);
        Assert.True(Speed(car) > 2, $"the rear rider alone did not get the car going again: {Speed(car)}");
        rearKeys.Forward = false;
        double coasting = Speed(car);
        await World.Ticks(20);
        double drop = coasting - Speed(car);
        HandcarLog($"coasting: {coasting:F2} to {Speed(car):F2} in a second");
        Assert.InRange(drop, config.CoastDrag * 0.5, config.CoastDrag * 2 + 0.2);
        Assert.Equal(0, car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.PumpingKey));

        // the branch selector: one place per press, as each rider faces
        Assert.Equal(HandcarTurn.Straight, car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.TurnKey));
        async Task Press(EntityControls keys, bool left)
        {
            if (left) keys.Left = true; else keys.Right = true;
            await World.Ticks(3);
            keys.Left = keys.Right = false;
            await World.Ticks(3);
        }
        await Press(rearKeys, left: true);
        Assert.Equal(HandcarTurn.Left, car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.TurnKey));
        await Press(rearKeys, left: true);
        Assert.Equal(HandcarTurn.Left, car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.TurnKey));
        await Press(frontKeys, left: true);                                   // the front rider's left is the car's right
        Assert.Equal(HandcarTurn.Straight, car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.TurnKey));
        await Press(frontKeys, left: true);
        Assert.Equal(HandcarTurn.Right, car.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.TurnKey));
        Assert.Equal(HandcarTurn.Right, car.Attributes.GetInt(EntityBehaviorHumanPowered.TurnKey));

        // sneak gets a rider off
        rearKeys.UpdateFromPacket(true, (int)EnumEntityAction.Sneak);
        frontKeys.UpdateFromPacket(true, (int)EnumEntityAction.Sneak);
        await World.Ticks(5);
        Assert.Null(rear.Entity.MountedOn);
        Assert.Null(front.Entity.MountedOn);
        car.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Handcar_comes_apart_with_Yangs_wrench_and_is_picked_up_whole()
    {
        await ClearHandcars();
        var track = await HandcarTrack(World.Spawn.AddCopy(-170, 45, -45), 30);
        var player = await HandcarPlayer("handcarwrench", track.AddCopy(2, 0, 8));
        var car = await PlaceHandcar(player, track.AddCopy(0, 0, 12));
        await World.Ticks(10);
        // the wrench in hand, a hammer in the other, held the full six seconds (Yang's behaviour, which its patch puts on the wrench)
        var wrench = W.GetItem(new AssetLocation("game:wrench-iron"))!;
        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(wrench);
        player.Entity.LeftHandItemSlot.Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:hammer-iron"))!);
        var tool = wrench.CollectibleBehaviors.Single(b => b.GetType().FullName == "YangTransport.CollectibleBehaviorRailVehicleDeconstructTool");
        var handling = EnumHandling.PassThrough;
        tool.OnHeldInteractStop(6.5f, player.InventoryManager.ActiveHotbarSlot, player.Entity, null, new EntitySelection { Entity = car }, ref handling);
        await World.Ticks(5);
        // Yang removes a vehicle by despawning it (RailwayVehicleShared.TryServerHardDespawn), which leaves Alive true
        if (Loaded(car))
            Assert.Fail("the wrench did not take the handcar apart: " + YangState(car, tool, player));
        var drops = W.GetEntitiesAround(car.Pos.XYZ, 4, 3, e => e is EntityItem item && item.Itemstack?.Collectible?.Code.Equals(HandcarItem) == true);
        Assert.Single(drops);
        drops[0].Die(EnumDespawnReason.Removed);

        // a sneak click with an empty hand picks a stopped, empty car up into the inventory
        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
        player.Entity.LeftHandItemSlot.Itemstack = null;
        var again = await PlaceHandcar(player, track.AddCopy(0, 0, 12));
        await World.Ticks(10);
        player.Entity.Controls.Sneak = true;
        again.OnInteract(player.Entity, player.InventoryManager.ActiveHotbarSlot, again.Pos.XYZ, EnumInteractMode.Interact);
        player.Entity.Controls.Sneak = false;
        await World.Ticks(5);
        Assert.False(Loaded(again), "a sneak click did not pick the handcar up");
        int held = player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName != GlobalConstants.creativeInvClassName)   // the creative inventory does not enumerate in survival
            .SelectMany(inv => inv)
            .Where(slot => slot.Itemstack?.Collectible?.Code.Equals(HandcarItem) == true)
            .Sum(slot => slot.StackSize);
        Assert.Equal(1, held);
    }

    // ---------------------------------------------------------------- the riders' hands, in the game's own animator

    private sealed class Posed(ClientAnimator animator, Dictionary<string, AnimationMetaData> active)
    {
        public ClientAnimator Animator { get; } = animator;
        public Dictionary<string, AnimationMetaData> Active { get; } = active;
    }

    /// <summary>The game's client animator on a shape (as the client builds one for an entity), with the
    /// given animations running.</summary>
    private Posed Animate(AssetLocation shapeAsset, params AnimationMetaData[] metas)
    {
        var shape = World.Api.Assets.Get(shapeAsset).ToObject<Shape>();
        shape.ResolveReferences(World.Api.Logger, shapeAsset.ToString());
        shape.InitForAnimations(World.Api.Logger, shapeAsset.ToString());
        var animator = new ClientAnimator(() => 1.0, shape.Animations, shape.Elements, shape.JointsById);
        var active = new Dictionary<string, AnimationMetaData>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in metas)
            active[m.Animation] = m;
        animator.OnFrame(active, 0);
        return new Posed(animator, active);
    }

    /// <summary>Every running animation at <paramref name="frame"/> with its easing, then the matrices.</summary>
    private static void PoseAt(Posed p, float frame, IReadOnlyDictionary<string, float> easing)
    {
        foreach (var (code, e) in easing)
        {
            var st = p.Animator.GetAnimationState(code)!;
            st.CurrentFrame = frame;
            st.EasingFactor = e;
            st.Active = st.Running = true;
            st.Iterations = 0;
        }
        p.Animator.OnFrame(p.Active, 0);
    }

    /// <summary>An attachment point where the animator has it, voxels in the model frame.</summary>
    private static Vec3d ApPos(ClientAnimator animator, string code, bool yangCentred = false)
    {
        var ap = animator.GetAttachmentPointPose(code);
        Assert.True(ap != null, $"no attachment point {code}");
        var m = ap!.AnimModelMatrix;
        double x = ap.AttachPoint.PosX / 16, y = ap.AttachPoint.PosY / 16, z = ap.AttachPoint.PosZ / 16;
        if (yangCentred && ap.AttachPoint.ParentElement is { } parent)
        {
            x += (parent.To![0] - parent.From![0]) / 32;
            y += (parent.To![1] - parent.From![1]) / 32;
            z += (parent.To![2] - parent.From![2]) / 32;
        }
        return new Vec3d((m[0] * x + m[4] * y + m[8] * z + m[12]) * 16, (m[1] * x + m[5] * y + m[9] * z + m[13]) * 16, (m[2] * x + m[6] * y + m[10] * z + m[14]) * 16);
    }

    private static AnimationMetaData Meta(string code) =>
        new AnimationMetaData { Code = code, Animation = code, AnimationSpeed = 1, EaseInSpeed = 1, EaseOutSpeed = 1, Weight = 1000, BlendMode = EnumAnimationBlendMode.Average }.Init();

    /// <summary>A point of the rider's model frame (voxels) in the car's, for a seat at <paramref name="seat"/>
    /// (voxels) turned by <paramref name="turn"/> degrees about the vertical.</summary>
    private static Vec3d RiderToCar(Vec3d p, Vec3d seat, double turn)
    {
        double dx = p.X - 8, dz = p.Z - 8;
        if (Math.Abs(turn - 180) < 1e-6)
            (dx, dz) = (-dx, -dz);
        return new Vec3d(seat.X + dx, seat.Y + p.Y, seat.Z + dz);
    }

    [AtlasScenario]
    public void Handcar_riders_hands_stay_on_the_handles_in_the_games_animator()
    {
        var rig = Handcars.Rig!;
        var seraph = new AssetLocation("game:shapes/entity/humanoid/seraph-faceless.json");
        var car = Animate(new AssetLocation("seraphhorizons:shapes/entity/handcar.json"), Meta(rig.BodyAnimation));
        // the seats where the rig puts them (Yang centres a seat on its attachment point's element)
        using var rigDoc = JsonDocument.Parse(World.Api.Assets.Get(HandcarSystem.RigAsset).ToText());
        foreach (var (id, seat) in rig.Seats)
        {
            var at = ApPos(car.Animator, $"SEAT_{id.ToUpperInvariant()}_AP", yangCentred: true);
            var want = rigDoc.RootElement.GetProperty("seat" + char.ToUpperInvariant(id[0]) + id[1..]).GetProperty("pos").EnumerateArray().Select(v => v.GetDouble() * 16).ToArray();
            Assert.True(at.DistanceTo(new Vec3d(want[0], want[1], want[2])) < 0.01, $"the {id} seat is at {at}, the rig says {string.Join(", ", want)}");
        }
        double worst = 0;
        int checks = 0;
        foreach (var (id, seat) in rig.Seats)
        {
            var seatAt = ApPos(car.Animator, $"SEAT_{id.ToUpperInvariant()}_AP", yangCentred: true);
            var rider = Animate(seraph, Meta(seat.Grip), Meta(seat.Pump));
            foreach (float frame in new[] { 0f, 7.5f, 15f, 22.25f, 30f, 37.5f, 45f, 52.75f, 59.5f })
            {
                PoseAt(car, frame, new Dictionary<string, float> { [rig.BodyAnimation] = 1f });
                foreach (var (grip, pump) in new[] { (1f, 0f), (0f, 1f), (0.5f, 0.5f) })
                {
                    PoseAt(rider, frame, new Dictionary<string, float> { [seat.Grip] = grip, [seat.Pump] = pump });
                    foreach (var (hand, side) in new[] { ("RightHand", "R"), ("LeftHand", "L") })
                    {
                        var handAt = RiderToCar(ApPos(rider.Animator, hand), seatAt, seat.Turn);
                        var gripAt = ApPos(car.Animator, $"GRIP_{id.ToUpperInvariant()}_{side}");
                        double miss = handAt.DistanceTo(gripAt);
                        worst = Math.Max(worst, miss);
                        checks++;
                        // a blend of the two is between two right answers: allowed a little more
                        Assert.True(miss < (grip * pump > 0 ? 0.6 : 0.25),
                            $"{id} rider, frame {frame}, grip {grip} pump {pump}: the {hand} is {miss:F3} voxels off its handle ({handAt} against {gripAt})");
                    }
                }
            }
        }
        HandcarLog($"{checks} hands checked, the worst {worst:F4} voxels off its handle");
    }
}
