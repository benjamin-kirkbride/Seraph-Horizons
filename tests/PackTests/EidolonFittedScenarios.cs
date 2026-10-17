using System.Reflection;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, fittings on a woken eidolon (#668): it is its build. Spawned in
/// a brass, iron or stainless build it has the health, move speed and blows
/// <see cref="EidolonStats.Of"/> gives that build with the pack's settings, its plates' armour takes
/// off a blow what <see cref="EidolonStats.Reduce"/> says (and nothing off internal harm), an eidolon
/// with no record is the old all-steel one at 300 HP, and a changed record changes its figures at
/// once, keeping its share of health. Each scenario builds on a granite floor of its own high over
/// the spawn.
/// </summary>
[AtlasWorld]
public class EidolonFittedScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const string Stainless = EidolonFittings.Stainless;

    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonConfig Settings => Mod.Config;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-fitted-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>A clear granite floor 40 above spawn at (<paramref name="dx"/>, <paramref name="dz"/>),
    /// <paramref name="reach"/> each way, with 6 blocks of air over it, its chunks loaded, and a player
    /// <paramref name="watcher"/> at a corner: the game runs a creature's AI only with a player in range.</summary>
    private async Task<BlockPos> Floor(int dx, int dz, string watcher, int reach = 8)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = GlobalConstants.ChunkSize;
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
        var p = await World.JoinRetrying(watcher);
        ((IServerPlayer)p.Player).WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(origin.AddCopy(-reach + 1, 0, -reach + 1));
        return origin;
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, EidolonBuild? build)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, null, activate: true, build: build);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private static EntityBehaviorHealth Health(Entity e) => e.GetBehavior<EntityBehaviorHealth>()!;

    // The traverser keeps the speed it was told in a protected field.
    private static readonly FieldInfo MovingSpeed =
        typeof(PathTraverserBase).GetField("movingSpeed", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>The speed it walks (or runs) to a point 6 blocks off at, as its traverser was told.</summary>
    private async Task<float> MoveSpeed(EntityLaborEidolon e, bool run)
    {
        var traverser = e.TaskAi!.PathTraverser!;
        e.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(e.Pos.XYZ.AddCopy(6, 0, 0), run));
        await World.Until(() => traverser.Active, 200);
        float speed = (float)MovingSpeed.GetValue(traverser)!;
        e.Orders.ClearOrder();
        await World.Until(() => !traverser.Active, 200);
        return speed;
    }

    private static DamageSource Blow(int tier) =>
        new() { Source = EnumDamageSource.Entity, Type = EnumDamageType.BluntAttack, DamageTier = tier, IgnoreInvFrames = true };

    public static TheoryData<string, string, string, string, string> Builds => new()
    {
        { "oak", "steel", "brass", "leather-red", "iron" },
        { "pine", "iron", "iron", "cloth-blue", "iron" },
        { "aged", Stainless, Stainless, "cloth-plain", Stainless },
    };

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Each_build_has_its_health_speed_blows_and_armour()
    {
        var at = await Floor(0, 0, "fittedwatcher1", reach: 12);
        int i = 0;
        foreach (var row in Builds)
        {
            var build = EidolonBuild.Of((string)row[0], (string)row[1], (string)row[2], (string)row[3], (string)row[4]);
            var want = EidolonStats.Of(build, Settings);
            var e = await Spawn(at.AddCopy(-8, 0, -8 + 8 * i++), build);
            Log(EidolonCommands.Describe(e));
            Assert.Equal(build, e.Build);
            Assert.Equal(want, e.FittedStats);
            Assert.Equal((float)want.MaxHealth, Health(e).MaxHealth, 3);
            Assert.Equal(Health(e).MaxHealth, Health(e).Health, 3);
            Assert.Equal(want.WalkSpeed, await MoveSpeed(e, run: false), 5);
            Assert.Equal(want.RunSpeed, await MoveSpeed(e, run: true), 5);
            var defend = e.TaskAi!.TaskManager.GetTask<AiTaskEidolonDefend>()!;
            Assert.Equal(want.DefenceDamage, defend.StrikeDamage(0), 4);
            Assert.Equal(want.SlamDamage, defend.StrikeDamage(2), 4);
            Assert.Contains(Vintagestory.API.Config.Lang.Get("game:material-" + build[FittingLayer.Plates]), e.GetInfoText());
            e.Die(EnumDespawnReason.Removed);
        }

        // The figures the brief sets: brass plates are 0.8 of the health and the blows and 1.25 of the
        // pace; stainless everywhere is 2 × 2 × 1.5 the iron base.
        double baseHealth = EidolonStats.BaseHealth(Settings);
        var brass = EidolonStats.Of(EidolonBuild.Of("oak", "steel", "brass", "leather-red", "iron"), Settings);
        Assert.Equal(baseHealth * 0.8 * 1.5, brass.MaxHealth, 3);
        Assert.Equal(Settings.WalkSpeed * 1.25f, brass.WalkSpeed, 5);
        Assert.Equal(Settings.DefenceDamage * 0.8f, brass.DefenceDamage, 4);
        Assert.Equal(baseHealth * 2 * 2 * 1.5,
            EidolonStats.Of(EidolonBuild.Of("aged", Stainless, Stainless, "cloth-plain", Stainless), Settings).MaxHealth, 3);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_blow_lands_for_its_builds_damage()
    {
        var at = await Floor(60, 0, "fittedwatcher2", reach: 10);
        var build = EidolonBuild.Of("oak", "steel", "brass", "leather-plain", "steel");
        var e = await Spawn(at, build);
        e.Orders!.SetOrder(StayOrder.OrderCode, StayOrder.Args(e.Pos.XYZ));
        var defend = e.TaskAi!.TaskManager.GetTask<AiTaskEidolonDefend>()!;

        // A wolf, its own AI taken away so it stands, hurts it from 4 blocks off; its first blow (a
        // punch, not a slam) takes the brass build's damage off the wolf.
        var wolf = (EntityAgent)W.ClassRegistry.CreateEntity(W.GetEntityType(new AssetLocation("game:wolf-eurasian-adult-male"))!);
        wolf.Pos.SetPos(new Vec3d(at.X + 5.5, at.Y, at.Z + 0.5));
        W.SpawnEntity(wolf);
        await World.Ticks(2);
        if (wolf.GetBehavior<EntityBehaviorTaskAI>() is { } wolfAi)
            wolf.SidedProperties.Behaviors.Remove(wolfAi);
        float before = Health(wolf).Health;
        e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Entity, SourceEntity = wolf, Type = EnumDamageType.PiercingAttack }, 1);
        await World.Until(() => defend.BlowsLanded > 0, 600);
        float took = before - Health(wolf).Health;
        Log($"first blow took {took} off the wolf; " + EidolonCommands.Describe(e));
        Assert.False(EidolonStrikes.Heavy(0));
        float blow = Settings.DefenceDamage * 0.8f * GlobalConstants.CreatureDamageModifier;
        Assert.Equal(Math.Min(blow, before), took, 3);
        foreach (var c in new Entity[] { wolf, e })
            c.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Its_plates_take_off_a_blow_what_the_armour_maths_says()
    {
        var at = await Floor(120, 0, "fittedwatcher3", reach: 12);
        int i = 0;
        foreach (var plates in new[] { "brass", "iron", "steel", Stainless })
        {
            var build = EidolonBuild.Of("oak", "steel", plates, "leather-plain", "steel");
            var e = await Spawn(at.AddCopy(-6 + 4 * i++, 0, 4), build);
            var health = Health(e);
            foreach (var (damage, tier) in new[] { (20f, 1), (20f, 3), (30f, 5), (8f, 6) })
            {
                float was = health.Health;
                Assert.True(e.ReceiveDamage(Blow(tier), damage));
                float want = e.FittedStats.Reduce(damage, tier, Settings.ArmourLoss);
                Log($"{plates}: {damage} at tier {tier} took {was - health.Health}, the maths {want}");
                Assert.Equal(want, was - health.Health, 3);
                Assert.True(want < damage);
                await World.Ticks(15);
            }

            // Internal harm and fire pass the armour.
            float before = health.Health;
            e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.BluntAttack, DamageTier = 3, IgnoreInvFrames = true }, 10);
            Assert.Equal(10, before - health.Health, 3);
            await World.Ticks(15);
            before = health.Health;
            e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Block, Type = EnumDamageType.Fire, DamageTier = 3, IgnoreInvFrames = true }, 5);
            Assert.Equal(5, before - health.Health, 3);
            e.Die(EnumDespawnReason.Removed);
            await World.Ticks(5);
        }
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task One_with_no_record_is_the_old_all_steel_eidolon_at_300()
    {
        var at = await Floor(180, 0, "fittedwatcher4");
        var e = await Spawn(at, null);
        Log(EidolonCommands.Describe(e));
        Assert.Null(e.WatchedAttributes.GetTreeAttribute(EidolonBuild.AttributeKey));
        Assert.Equal(EidolonBuild.OldBuild, e.Build);
        Assert.Equal(300, Health(e).MaxHealth, 3);
        Assert.Equal(300, Health(e).Health, 3);
        Assert.Equal(Settings.WalkSpeed, e.FittedStats.WalkSpeed, 6);
        Assert.Equal(Settings.DefenceDamage, e.FittedStats.DefenceDamage, 6);
        Assert.Equal(4, e.FittedStats.Armour!.ProtectionTier);
        Assert.NotNull(e.WatchedAttributes.GetTreeAttribute(EntityLaborEidolon.StatsKey));
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_changed_record_changes_its_figures_at_once_keeping_its_share_of_health()
    {
        var at = await Floor(240, 0, "fittedwatcher5", reach: 12);
        var e = await Spawn(at, null);
        var health = Health(e);
        e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.BluntAttack, IgnoreInvFrames = true }, 150);
        Assert.Equal(150, health.Health, 3);

        // Stainless plates through the property: 1.5 → 2 on the plates, still half its health.
        var stainlessPlates = EidolonBuild.Of("oak", "steel", Stainless, "leather-plain", "steel");
        e.Build = stainlessPlates;
        var want = EidolonStats.Of(stainlessPlates, Settings);
        Assert.Equal(want, e.FittedStats);
        Assert.Equal(400, health.MaxHealth, 3);
        Assert.Equal(200, health.Health, 3);
        Assert.Equal(5, e.FittedStats.Armour!.ProtectionTier);

        // Brass plates written straight into the attribute as the gantry may: the same.
        var brass = EidolonBuild.Of("oak", "steel", "brass", "leather-plain", "steel");
        EntityLaborEidolon.WriteBuild(e.WatchedAttributes, brass);
        Assert.Equal(EidolonStats.Of(brass, Settings), e.FittedStats);
        Assert.Equal(EidolonStats.Of(brass, Settings).MaxHealth, health.MaxHealth, 3);
        Assert.Equal(health.MaxHealth / 2, health.Health, 3);
        Assert.Equal(Settings.WalkSpeed * 1.25f, await MoveSpeed(e, run: false), 5);

        // The record taken away: the old build again, picked up by its check.
        e.WatchedAttributes.RemoveAttribute(EidolonBuild.AttributeKey);
        await World.Ticks(20);
        Assert.Equal(EidolonStats.Of(EidolonBuild.OldBuild, Settings), e.FittedStats);
        Assert.Equal(300, health.MaxHealth, 3);
        Assert.Equal(150, health.Health, 3);
        e.Die(EnumDespawnReason.Removed);
    }
}
