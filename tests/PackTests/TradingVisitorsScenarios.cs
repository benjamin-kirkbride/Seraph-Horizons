using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Visitors;
using SeraphHorizons.Mod.Trading.Visitors.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Visitors: travelling merchants at a player-built inn (#456)
/// against the pinned mods. Each scenario builds a small inn of its own high above spawn (granite
/// floor, walls and roof; an inn sign or a Cartwright's Caravan market stall; a bed; a table with a
/// crock of fruit on it; a torch) with the world's block accessor, raises the flag with
/// <c>World.SetBlock</c> (an inn without an owner), and drives it through <c>/sh trade inn</c> and
/// <c>/sh trade simulate</c> as a player standing in it. Atlas' world has no camps near spawn and
/// no supply, so the standing and supply conditions fail here; <c>inn call</c> skips them.
/// </summary>
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public class TradingVisitorsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private InnSystem Inns => InnSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no InnSystem");

    [AtlasScenario]
    [ReadsBootLog]
    public void Travelling_merchants_are_on_their_types_register_and_the_boot_logs_nothing_about_them()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("travelling", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("visitor", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("innflag", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("innsign", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.True(Inns.Active);
        foreach (string type in TraderTypes.Visitors)
            foreach (string gender in new[] { "male", "female" })
                foreach (string climate in new[] { "cold", "temperate", "desert" })
                {
                    var props = W.GetEntityType(new AssetLocation("seraphhorizons", $"visitor-{gender}-{type}-{climate}"));
                    Assert.True(props != null, $"no visitor entity for {type} {gender} {climate}");
                    Assert.Equal(EntityVisitingTrader.ClassName, props!.Class);
                }
        var lists = TradingSystem.Of(Api)!.Lists!;
        foreach (string type in TraderTypes.Visitors)
            Assert.NotNull(lists.For(type));
        Assert.DoesNotContain(lists.Unresolved, u => TraderTypes.Visitors.Any(t => u.StartsWith(t + ":")));
        Assert.Equal(TraderTypes.All.OrderBy(t => t), lists.CampWeights.Keys.OrderBy(t => t));
        Assert.NotNull(W.GetBlock(new AssetLocation("seraphhorizons:innflag")));
        Assert.NotNull(W.GetBlock(new AssetLocation("seraphhorizons:innsign-north")));
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:innflag");
    }

    /// <summary>A cleared spot 50 above spawn with its chunk columns loaded and a granite floor one below.</summary>
    private async Task<BlockPos> Sky(int dx, int dz)
    {
        var origin = World.Spawn.AddCopy(dx, 50, dz);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        const int reach = 10;
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        foreach (var c in columns) Api.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 30000);
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
            for (int y = -1; y <= 6; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        return origin;
    }

    private int Id(string code) => (W.GetBlock(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no block {code}")).Id;

    /// <summary>A 5×5 room around origin (floor at -1, walls to 2, roof at 3); the stall's spot is origin.
    /// Returns the flag's position (inside, by the east wall).</summary>
    private async Task<BlockPos> BuildInn(BlockPos o, bool sign = true, bool bed = true, bool food = true, bool torch = true, bool roof = true)
    {
        int granite = Id("game:rock-granite");
        for (int x = -3; x <= 3; x++)
        for (int z = -3; z <= 3; z++)
        {
            W.BlockAccessor.SetBlock(granite, o.AddCopy(x, -1, z));
            if (roof) W.BlockAccessor.SetBlock(granite, o.AddCopy(x, 3, z));
            if (Math.Abs(x) == 3 || Math.Abs(z) == 3)
                for (int y = 0; y <= 2; y++) W.BlockAccessor.SetBlock(granite, o.AddCopy(x, y, z));
        }
        if (sign) W.BlockAccessor.SetBlock(Id("seraphhorizons:innsign-north"), o);
        if (bed)
        {
            W.BlockAccessor.SetBlock(Id("game:bed-wood-head-north"), o.AddCopy(2, 0, 1));
            W.BlockAccessor.SetBlock(Id("game:bed-wood-feet-north"), o.AddCopy(2, 0, 2));
        }
        var table = o.AddCopy(-2, 0, 2);
        W.BlockAccessor.SetBlock(Id("game:table-normal"), table);
        if (food)
        {
            var crock = table.UpCopy();
            W.BlockAccessor.SetBlock(Id("game:crock-blue-fired"), crock);
            await World.Ticks(2);
            var be = W.BlockAccessor.GetBlockEntity(crock) as IBlockEntityContainer
                     ?? throw new Xunit.Sdk.XunitException("the crock has no inventory");
            be.Inventory[0].Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:fruit-redapple")), 4);
            be.Inventory[0].MarkDirty();
        }
        if (torch) W.BlockAccessor.SetBlock(Id("game:torch-basic-lit-up"), o.AddCopy(1, 0, -2));
        var flag = o.AddCopy(2, 0, -1);
        World.SetBlock("seraphhorizons:innflag", flag);
        await World.Ticks(5);
        return flag;
    }

    private async Task<ITestPlayer> PlayerIn(string name, BlockPos o)
    {
        var player = await World.JoinPlayer(name);
        await player.TeleportTo(o.AddCopy(-1, 0, -1));
        await World.Ticks(5);
        return player;
    }

    private InnRecord Record(BlockPos flag) =>
        Inns.Book.Get(new InnPos(flag.X, flag.Y, flag.Z)) ?? throw new Xunit.Sdk.XunitException($"no inn at the flag {flag}");

    private EntityVisitingTrader Visitor(InnRecord r) =>
        W.GetEntityById(r.EntityId) as EntityVisitingTrader ?? throw new Xunit.Sdk.XunitException($"no visitor {r.EntityId} loaded");

    private static List<string> Shelf(EntitySeraphTrader t) =>
        t.Inventory.SellingSlots.Where(s => s.Itemstack != null).Select(s => s.Itemstack!.Collectible.Code.ToString()).ToList();

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_built_inn_passes_its_check_and_a_called_visitor_sells_its_specials_takes_no_harm_and_leaves_after_its_stay()
    {
        var o = await Sky(0, 70);
        var flag = await BuildInn(o);
        var record = Record(flag);
        Assert.Equal("", record.Owner);
        var p = await PlayerIn("innkeeper", o);

        var check = await p.ExecuteCommand("/sh trade inn check");
        output.WriteLine(check.Message);
        Assert.True(check.Ok, check.Message);
        var eval = Inns.Evaluate(flag, "");
        Assert.True(eval.Report.Passed, check.Message);
        Assert.Equal(new InnPos(o.X, o.Y, o.Z), eval.Report.Stall);

        var call = await p.ExecuteCommand("/sh trade inn call general --now");
        output.WriteLine(call.Message);
        Assert.True(call.Ok, call.Message);
        Assert.Equal(InnPhase.Visiting, record.Phase);
        var visitor = Visitor(record);
        Assert.Equal(TraderTypes.TravellingMerchant, visitor.TraderType);
        Assert.True(visitor.Pos.DistanceTo(o.ToVec3d()) < 4, $"the visitor is at {visitor.Pos}, not by the stall at {o}");
        Assert.Equal("visitor:general", StandingSystem.Of(Api)!.TraderIdOf(visitor));
        Assert.InRange(visitor.LeaveDay - Inns.Now, 2.9, 5.1);

        // Its shelves hold its own specials, which no camp sells.
        var def = TradingSystem.Of(Api)!.Lists!.For(TraderTypes.TravellingMerchant)!;
        var listed = def.Selling.Core.Concat(def.Selling.Rotating.List).Select(e => e.Code.Contains(':') ? e.Code : "game:" + e.Code).ToHashSet();
        var shelf = Shelf(visitor);
        output.WriteLine("shelf: " + string.Join(", ", shelf));
        Assert.True(shelf.Count >= def.Selling.Core.Count, "the core is not all on the shelf");
        Assert.All(shelf, code => Assert.Contains(code, listed));

        // Invulnerable.
        float health = visitor.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth") ?? -1;
        Assert.False(visitor.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Player, SourceEntity = p.Player.Entity, Type = EnumDamageType.SlashingAttack }, 100));
        Assert.False(visitor.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Fall, Type = EnumDamageType.Gravity }, 1000));
        await World.Ticks(5);
        Assert.True(visitor.Alive);
        Assert.Equal(health, visitor.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth") ?? -1);

        // Five days on it has gone, and the inn cools down.
        var sim = await World.ExecuteCommand("/sh trade simulate 5");
        Assert.True(sim.Ok, sim.Message);
        await World.Until(() => !visitor.Alive, 10000);
        Assert.Equal(InnPhase.Cooldown, record.Phase);
        Assert.InRange(record.CooldownUntil - Inns.Now, 8.9, 10.1);

        // A curio dealer called the slow way arrives on its day, and can be sent off.
        var curio = await p.ExecuteCommand("/sh trade inn call curio");
        output.WriteLine(curio.Message);
        Assert.True(curio.Ok, curio.Message);
        Assert.Equal(InnPhase.Pending, record.Phase);
        Assert.True((await World.ExecuteCommand("/sh trade simulate 4")).Ok);
        Assert.Equal(InnPhase.Visiting, record.Phase);
        var dealer = Visitor(record);
        Assert.Equal(TraderTypes.TravellingCurio, dealer.TraderType);
        var dismiss = await p.ExecuteCommand("/sh trade inn dismiss");
        Assert.True(dismiss.Ok, dismiss.Message);
        await World.Until(() => !dealer.Alive, 10000);
        Assert.Equal(InnPhase.Cooldown, record.Phase);

        // Taking the flag down ends the inn.
        W.BlockAccessor.SetBlock(0, flag);
        await World.Ticks(2);
        Assert.Null(Inns.Book.Get(new InnPos(flag.X, flag.Y, flag.Z)));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_inn_without_a_bed_or_light_reports_what_it_lacks_and_is_never_visited()
    {
        var o = await Sky(-70, 0);
        var flag = await BuildInn(o, bed: false, torch: false);
        var p = await PlayerIn("innbuilder", o);
        var check = await p.ExecuteCommand("/sh trade inn check");
        output.WriteLine(check.Message);
        Assert.True(check.Ok, check.Message);
        Assert.Contains("no bed in the room", check.Message);
        Assert.Contains("lamplight", check.Message);
        var eval = Inns.Evaluate(flag, "");
        Assert.Equal([InnRule.Bed, InnRule.Light], eval.Report.Failed);
        Assert.Empty(eval.PassingKinds);

        // Days of checks call nobody.
        Assert.True((await World.ExecuteCommand("/sh trade simulate 6")).Ok);
        var record = Record(flag);
        Assert.Equal(InnPhase.Idle, record.Phase);
        Assert.Contains("bed", record.LastMissing);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_open_roof_fails_and_a_market_stall_stands_in_for_the_inn_sign()
    {
        var o = await Sky(70, 0);
        var flag = await BuildInn(o, sign: false, roof: false);
        var stallType = W.GetEntityType(new AssetLocation("cartwrightscaravan:marketstall-smallstall-classic-aged"))
                        ?? throw new Xunit.Sdk.XunitException("no Cartwright's Caravan market stall entity");
        var stall = W.ClassRegistry.CreateEntity(stallType);
        stall.Pos.SetPos(o.X + 0.5, o.Y, o.Z + 0.5);
        W.SpawnEntity(stall);
        await World.Ticks(5);
        var eval = Inns.Evaluate(flag, "");
        output.WriteLine(string.Join("; ", eval.Report.Checks));
        Assert.True(eval.Report[InnRule.Stall].Passed);
        Assert.Contains(InnRule.Roof, eval.Report.Failed);
        Assert.Contains(InnRule.Walls, eval.Report.Failed);
        stall.Die(EnumDespawnReason.Removed);
    }
}
