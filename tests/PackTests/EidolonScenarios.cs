using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#673): the eidolon entity with the pack's default settings. It
/// spawns owned and charged and wakes; only its owner commands it; its charge runs out over simulated
/// days (the calendar moved on), it slumps without dying and a temporal gear wakes it; at 0 HP it
/// slumps disabled, never dies and drops nothing, and stands up once repaired past its threshold; and
/// the wide pathfinder walks it through a two-wide, four-high gate and finds no way through a narrower
/// one. Its oil and repair (#674): jobs drain the oil and dry stops a job, which waits standing until
/// oiled; repair restores its share, doubled inside a gantry, stands a slumped one up where it lies,
/// and is refused when whole; with MachineOil off it has no reservoir. Its own class: it moves the calendar, which the shared world forbids. Each scenario builds on a
/// granite floor of its own high over the spawn.
/// </summary>
[AtlasWorld]
public class EidolonScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>A clear granite floor 40 above spawn at (<paramref name="dx"/>, <paramref name="dz"/>),
    /// <paramref name="reach"/> each way, with 6 blocks of air over it, its chunks loaded.</summary>
    private async Task<BlockPos> Floor(int dx, int dz, int reach = 8)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                Sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(-reach, 0, -reach)) != null
                                && W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(reach, 0, reach)) != null, 1200);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 6; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        await World.Ticks(5);
        return origin;
    }

    /// <summary>An eidolon spawned at <paramref name="at"/> (a block corner), owned by
    /// <paramref name="owner"/>, awake and free to work.</summary>
    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer? owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e);

    [AtlasScenario]
    public void The_eidolon_is_in_the_game()
    {
        Assert.True(Mod.Enabled);
        Assert.True(W.Config.GetBool(EidolonSystem.RunningKey));
        var type = W.GetEntityType(EidolonSystem.EntityCode);
        Assert.NotNull(type);
        Assert.Equal(1.7f, type!.CollisionBoxSize.X, 3);
        Assert.Equal(3.75f, type.CollisionBoxSize.Y, 3);
        Assert.NotNull(W.GetItem(new AssetLocation("seraphhorizons:creature-eidolon")));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task It_spawns_owned_and_charged_and_only_its_owner_commands_it()
    {
        var owner = await World.JoinPlayer("eidolonowner");
        var other = await World.JoinPlayer("eidolonstranger");
        var at = await Floor(0, 0);
        var e = await Spawn(at, owner.Player);
        Log(State(e));
        Assert.Equal(owner.Player.PlayerUID, e.OwnerUid);
        var charge = e.GetBehavior<EntityBehaviorEidolonCharge>()!;
        Assert.Equal(W.Calendar.DaysPerYear / 4.0, charge.ChargeDays, 2);
        Assert.True(e.MayCommand(owner.Player));
        Assert.False(e.MayCommand(other.Player));
        Assert.Contains(owner.Player.PlayerName, e.GetInfoText());
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Charge_runs_out_over_days_it_slumps_alive_and_a_temporal_gear_wakes_it()
    {
        var p = await World.JoinPlayer("eidolonfeeder");
        var at = await Floor(40, 0);
        await p.TeleportTo(at.AddCopy(3, 0, 0));
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var e = await Spawn(at, player);
        var charge = e.GetBehavior<EntityBehaviorEidolonCharge>()!;
        double days = charge.ChargeDays;

        // Half its charge: still standing, about half left.
        W.Calendar.Add((float)(days / 2 * W.Calendar.HoursPerDay));
        await World.Ticks(20);
        Log("half way: " + State(e));
        Assert.True(e.CanWork, State(e));
        Assert.InRange(charge.ChargeDays, days / 2 - 0.5, days / 2 + 0.5);

        // Past the rest: out of charge, slumped, alive.
        W.Calendar.Add((float)((days / 2 + 1) * W.Calendar.HoursPerDay));
        await World.Until(() => e.Pose.State == EidolonPoseState.Slumped, 100);
        Log("run out: " + State(e));
        Assert.Equal(0, charge.ChargeDays);
        Assert.Equal(EidolonStop.NoCharge, e.Stop);
        Assert.True(e.Alive);
        Assert.True(Sapi.World.LoadedEntities.ContainsKey(e.EntityId));
        Assert.True(e.AnimManager.IsAnimationActive("slump"));

        // Slumped, time drains nothing.
        W.Calendar.Add(5 * W.Calendar.HoursPerDay);
        await World.Ticks(10);
        Assert.Equal(0, charge.ChargeDays);

        // A temporal gear: taken, a gear's worth, and it stands up.
        var gear = W.GetItem(EntityBehaviorEidolonCharge.TemporalGear)!;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(gear, 3);
        slot.MarkDirty();
        e.OnInteract(player.Entity, slot, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        Assert.Equal(2, slot.StackSize);
        Assert.Equal(charge.DaysPerGear, charge.ChargeDays, 6);
        await World.Until(() => e.CanWork, 400);
        Log("recharged: " + State(e));

        // A second gear fills it to its cap; a third is refused and kept.
        e.OnInteract(player.Entity, slot, e.Pos.XYZ, EnumInteractMode.Interact);
        Assert.Equal(1, slot.StackSize);
        e.OnInteract(player.Entity, slot, e.Pos.XYZ, EnumInteractMode.Interact);
        Assert.Equal(1, slot.StackSize);
        Assert.InRange(charge.ChargeDays, charge.DaysPerGear * 2 - 0.01, charge.DaysPerGear * 2);
        slot.Itemstack = null;
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task At_zero_hp_it_slumps_disabled_never_dies_and_stands_once_repaired()
    {
        var at = await Floor(80, 0);
        var e = await Spawn(at, null);
        var health = e.GetBehavior<EntityBehaviorHealth>()!;
        int Drops() => W.GetEntitiesAround(e.Pos.XYZ, 10, 10, x => x is EntityItem).Length;
        int dropsBefore = Drops();

        Assert.True(e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.BluntAttack }, health.MaxHealth * 3));
        await World.Until(() => e.Pose.State == EidolonPoseState.Slumped, 100);
        Log("knocked out: " + State(e));
        Assert.True(e.Alive);
        Assert.True(Sapi.World.LoadedEntities.ContainsKey(e.EntityId));
        Assert.Equal(0, health.Health);
        Assert.Equal(EidolonStop.Damaged, e.Stop);
        Assert.False(e.CanWork);
        Assert.Equal(dropsBefore, Drops());

        // Down, it takes no more harm; and Die(Death) (the kill command, lava) does not kill it.
        Assert.False(e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.BluntAttack }, 10));
        e.Die(EnumDespawnReason.Death);
        await World.Ticks(5);
        Assert.True(e.Alive);

        // Repaired a little: still down. Past the threshold: it stands.
        Assert.True(e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, health.MaxHealth * 0.1f));
        await World.Ticks(20);
        Assert.Equal(EidolonStop.Damaged, e.Stop);
        e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, health.MaxHealth * 0.3f);
        await World.Until(() => e.CanWork, 400);
        Log("repaired: " + State(e));
        e.Die(EnumDespawnReason.Removed);
    }

    /// <summary>A floor with a wall across it at x + 6, the gap in it <paramref name="width"/> wide and
    /// <paramref name="height"/> high; an eidolon on the near side is told to go to the far side. A
    /// player watches from a corner: the game runs a creature's AI only with a player in range.</summary>
    private async Task<(EntityLaborEidolon E, Vec3d Target)> AtTheWall(int dx, int width, int height, string watcher)
    {
        var at = await Floor(dx, 0, reach: 12);
        var p = await World.JoinPlayer(watcher);
        await p.TeleportTo(at.AddCopy(-10, 0, -10));
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int z = -12; z <= 12; z++)
        for (int y = 0; y <= 6; y++)
            if (!(z >= 0 && z < width && y < height))
                W.BlockAccessor.SetBlock(granite, at.AddCopy(6, y, z));
        await World.Ticks(5);
        var e = await Spawn(at.AddCopy(0, 0, 1), null);
        var target = new Vec3d(at.X + 10, at.Y, at.Z + 1);
        e.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(target, run: false));
        return (e, target);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task It_walks_through_a_two_wide_four_high_gate()
    {
        var (e, target) = await AtTheWall(140, 2, 4, "eidolonwatcher");
        await World.Until(() => e.Orders!.OrderCode == null, 2400);
        Log("through the gate: " + State(e));
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(target) < 1.5 * 1.5, State(e));
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task It_finds_no_way_through_a_one_wide_gate_and_says_so()
    {
        var (e, _) = await AtTheWall(200, 1, 4, "eidolonwatcher2");
        await World.Ticks(20);
        Log("at the narrow gate: " + State(e));
        Assert.Equal(GoToOrder.OrderCode, e.Orders!.OrderCode);
        Assert.Contains("no way", e.GetInfoText());
        Assert.True(e.Pos.X < World.Spawn.X + 200 + 6, State(e));
        e.Die(EnumDespawnReason.Removed);
    }

    // ---- Oil and repair (#674) ----

    /// <summary>A stand-in job order: one tree felled (its oil) every half second, counted.</summary>
    private sealed class OilJobOrder(int[] done) : IEidolonOrder
    {
        public const string OrderCode = "test-oiljob";
        private float _since;

        public string Code => OrderCode;

        public void Start(EntityLaborEidolon eidolon) => _since = 0;

        public bool Continue(EntityLaborEidolon eidolon, float dt)
        {
            _since += dt;
            if (_since >= 0.5f)
            {
                _since = 0;
                done[0]++;
                eidolon.SpendOil(EidolonJob.TreeFelled);
            }
            return true;
        }

        public void Stop(EntityLaborEidolon eidolon, bool cancelled)
        {
        }
    }

    private ItemSlot Hold(IServerPlayer player, string code, int count)
    {
        var location = new AssetLocation(code);
        CollectibleObject? what = W.GetItem(location) ?? (CollectibleObject?)W.GetBlock(location);
        Assert.True(what is { Id: > 0 }, $"no {code}");
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(what!, count);
        slot.MarkDirty();
        return slot;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Jobs_drain_its_oil_dry_stops_the_job_standing_and_oil_starts_it_again()
    {
        var p = await World.JoinPlayer("eidolonoiler");
        var at = await Floor(260, 0);
        await p.TeleportTo(at.AddCopy(4, 0, 4));
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var e = await Spawn(at, null);
        var oil = e.Oil!;
        var config = Mod.Config;
        Assert.Equal(config.OilTank * config.InitialOilShare, oil.Tank!.Value.Points, 3);
        Assert.Contains($"Oil: {config.OilTank * config.InitialOilShare:0} of {config.OilTank:0}", e.GetInfoText());

        // Idle and staying cost nothing.
        e.Orders!.SetOrder(StayOrder.OrderCode);
        await World.Ticks(40);
        Assert.Equal(config.OilTank * config.InitialOilShare, oil.Tank!.Value.Points, 3);

        // Enough for three trees: the job does three, then stands dry, its order kept, not slumped.
        var done = new int[1];
        EidolonOrders.Register(OilJobOrder.OrderCode, (_, _) => new OilJobOrder(done));
        oil.SetPoints(config.OilPerTreeFelled * 2.5);
        e.Orders.SetOrder(OilJobOrder.OrderCode);
        await World.Until(() => e.Stop != null, 400);
        Log("dry: " + State(e));
        Assert.Equal(3, done[0]);
        Assert.Equal(EidolonOil.Dry, e.Stop);
        Assert.True(oil.Tank!.Value.Dry);
        Assert.False(e.CanWork);
        Assert.NotEqual(EidolonPoseState.Slumped, e.Pose.State);
        Assert.Contains("Out of oil", e.GetInfoText());
        await World.Ticks(60);
        Assert.Equal(3, done[0]);
        Assert.Equal(OilJobOrder.OrderCode, e.Orders.OrderCode);

        // Five lumps of tallow, half a litre each: 250 points, all taken; it works again.
        var slot = Hold(player, "game:fat-rendered", 5);
        e.OnInteract(player.Entity, slot, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        Assert.Equal(0, slot.StackSize);
        Assert.Equal(250, oil.Tank!.Value.Points, 3);
        await World.Until(() => done[0] > 3, 400);
        Log("oiled: " + State(e));
        Assert.True(e.CanWork, State(e));
        e.Orders.ClearOrder();
        slot.Itemstack = null;
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Repair_restores_its_share_double_in_a_gantry_and_stands_a_slumped_one_up()
    {
        var p = await World.JoinPlayer("eidolonmender");
        var at = await Floor(320, 0, reach: 12);
        await p.TeleportTo(at.AddCopy(-10, 0, -10));
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var config = Mod.Config;

        // In a gantry, where it stands once awake.
        World.SetBlock("seraphhorizons:eidolongantry-oak-south", at);
        await World.Ticks(5);
        var gantry = (BEEidolonGantry)W.BlockAccessor.GetBlockEntity(at)!;
        gantry.PlaceGhosts();
        var body = gantry.WorldPoint(EidolonGantrySystem.Of(World.Api).Rig!.Body);
        var docked = Mod.Spawn(W, body, 0, null, activate: false)!;
        await World.Ticks(5);
        Assert.Same(gantry, EntityBehaviorEidolonRepair.GantryAround(docked));
        var health = docked.GetBehavior<EntityBehaviorHealth>()!;
        health.Health = 100;
        var slot = Hold(player, "game:metalplate-iron", 4);
        docked.OnInteract(player.Entity, slot, docked.Pos.XYZ, EnumInteractMode.Interact);
        Assert.Equal(3, slot.StackSize);
        Assert.Equal(100 + health.MaxHealth * (float)(config.RepairShare * config.GantryRepairMultiplier), health.Health, 2);

        // Out in the open: a third of that; down at 0 HP it takes three to stand.
        var open = await Spawn(at.AddCopy(-8, 0, -8), null);
        Assert.Null(EntityBehaviorEidolonRepair.GantryAround(open));
        var openHealth = open.GetBehavior<EntityBehaviorHealth>()!;
        open.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.BluntAttack }, openHealth.MaxHealth * 3);
        await World.Until(() => open.Pose.State == EidolonPoseState.Slumped, 100);
        slot = Hold(player, "game:metal-parts", 5);
        open.OnInteract(player.Entity, slot, open.Pos.XYZ, EnumInteractMode.Interact);
        Assert.Equal(openHealth.MaxHealth * (float)config.RepairShare, openHealth.Health, 2);
        open.OnInteract(player.Entity, slot, open.Pos.XYZ, EnumInteractMode.Interact);
        await World.Ticks(10);
        Assert.Equal(EidolonStop.Damaged, open.Stop);
        open.OnInteract(player.Entity, slot, open.Pos.XYZ, EnumInteractMode.Interact);
        Assert.Equal(2, slot.StackSize);
        await World.Until(() => open.CanWork, 400);
        Log("stood up: " + State(open));

        // Whole: the item is refused and kept.
        openHealth.Health = openHealth.MaxHealth;
        open.OnInteract(player.Entity, slot, open.Pos.XYZ, EnumInteractMode.Interact);
        Assert.Equal(2, slot.StackSize);
        slot.Itemstack = null;
        docked.Die(EnumDespawnReason.Removed);
        open.Die(EnumDespawnReason.Removed);
    }

    /// <summary>MachineOil off (the setting flipped in this world for the scenario, as the server reads
    /// it at each check): no reservoir, never dry, oil not taken; back on, a new eidolon's.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task With_machine_oil_off_it_has_no_reservoir_and_never_runs_dry()
    {
        var p = await World.JoinPlayer("eidolonnooil");
        var at = await Floor(380, 0);
        await p.TeleportTo(at.AddCopy(4, 0, 4));
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var e = await Spawn(at, null);
        var settings = SeraphHorizonsSystem.ConfigFor(World.Api);
        try
        {
            settings.MachineOil = false;
            await World.Until(() => e.Oil!.Tank == null, 100);
            for (int i = 0; i < 300; i++)
                e.SpendOil(EidolonJob.TreeFelled);
            await World.Ticks(5);
            Assert.Null(e.Oil!.Tank);
            Assert.True(e.CanWork, State(e));
            Assert.DoesNotContain("Oil:", e.GetInfoText());
            var slot = Hold(player, "game:fat-rendered", 2);
            e.OnInteract(player.Entity, slot, e.Pos.XYZ, EnumInteractMode.Interact);
            Assert.Equal(2, slot.StackSize);
            slot.Itemstack = null;
        }
        finally
        {
            settings.MachineOil = true;
        }
        await World.Until(() => e.Oil!.Tank != null, 100);
        Assert.Equal(Mod.Config.OilTank * Mod.Config.InitialOilShare, e.Oil!.Tank!.Value.Points, 3);
        e.Die(EnumDespawnReason.Removed);
    }
}
