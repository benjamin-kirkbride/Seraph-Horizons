using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The trading scenarios that need nothing but the plain world (no camps, no data files), one
/// partial file per feature: TradingEconomy, TradingOrders, TradingValues, TradingStanding,
/// TradingSchematics, TradingVisitors and TradingWindow (each <c>Trading*Scenarios.cs</c>), on one server boot
/// (Atlas boots one per scenario class, 45 to 55 s each in CI). The trading classes on seeded
/// standard worlds (<see cref="TradingCoreScenarios"/>, <see cref="TradingMapsScenarios"/>,
/// <see cref="TradingAdminScenarios"/>) stay classes of their own.
/// <para>As in <see cref="SharedWorldScenarios"/>, every scenario shares the world with every other,
/// in no set order: its own offsets from <c>World.Spawn</c> (Economy on the diagonals at 30 and 40,
/// Orders on the axes at 25 and 40, Standing at (±15, ∓35) and (±35, ±15), Window at (±20, ±55) and
/// (55, ±20), Visitors 50 up at 70 out),
/// nothing changed world-wide left behind, and <see cref="ReadsBootLogAttribute"/> on a scenario that
/// reads the boot's log. Two things differ from a plain shared class:</para>
/// <list type="bullet">
/// <item>Players come from <see cref="Customer"/> and the other role helpers below, joined once per
/// server and handed out reset (survival, empty inventory, no standing of their own): the features
/// together would join more than the engine's 16.</item>
/// <item>The trading mod's state lives in memory and no rollback restores it: regional supply,
/// standing, the order and delivery books, and the days <c>/sh trade simulate</c> adds (which also
/// restock every loaded trader). So a scenario that reads supply or prices starts with
/// <see cref="FreshSupply"/>, spawns its own traders (fresh ids, so fresh standing, orders and
/// restock clocks) and removes them when done, and puts back supply it set.</item>
/// </list>
/// </summary>
[AtlasWorld]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class TradingScenarios : AtlasScenarioBase
{
    private readonly ITestOutputHelper output;

    public TradingScenarios(ITestOutputHelper output) => this.output = output;

    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private EconomySystem Economy => EconomySystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no EconomySystem");
    private StandingSystem Standing => StandingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no StandingSystem");

    /// <summary>A temperate male trader of <paramref name="type"/> on the ground at that offset from spawn.</summary>
    private async Task<EntitySeraphTrader> SpawnTrader(string type, int dx, int dz)
    {
        var pos = World.Spawn.AddCopy(dx, 0, dz);
        pos.Y = W.BlockAccessor.GetTerrainMapheightAt(pos) + 1;
        var props = W.GetEntityType(new AssetLocation("seraphhorizons", $"trader-male-{type}-temperate"))!;
        var trader = (EntitySeraphTrader)W.ClassRegistry.CreateEntity(props);
        trader.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        W.SpawnEntity(trader);
        await World.Ticks(5);
        return trader;
    }

    private static int Gears(IPlayer player) => InventoryTrader.GetPlayerAssets(player.Entity);

    /// <summary>No supply anywhere: what a fresh world has, and what prices, shelves and orders here
    /// assume. Other scenarios' deals, <c>/sh trade supply set</c> and simulated days all move it.</summary>
    private void FreshSupply() => Economy.Supply.Clear();

    // The players, joined once per server (World.JoinPlayer refuses a name already joined, and the
    // engine takes 16 clients): one per role, not one per scenario.
    private static readonly Dictionary<string, ITestPlayer> _players = new();
    private static object? _playersWorld;

    /// <summary>Trades, sells, takes orders and crafts: every scenario that needs a player at a
    /// trader of its own.</summary>
    private Task<ITestPlayer> Customer() => Role("tradecustomer");

    /// <summary>Carries deliveries.</summary>
    private Task<ITestPlayer> Courier() => Role("tradecourier");

    /// <summary>The company scenario's two players. Only that scenario uses them: it leaves the
    /// newcomer in a group of its own, which would pool any later standing.</summary>
    private Task<ITestPlayer> Veteran() => Role("tradeveteran");

    private Task<ITestPlayer> Newcomer() => Role("tradenewcomer");

    /// <summary>Stands in the inns and calls their visitors.</summary>
    private Task<ITestPlayer> Innkeeper() => Role("innkeeper");

    /// <summary>The role's player, joined on first use, in survival with an empty inventory and no
    /// standing of its own with any trader.</summary>
    private async Task<ITestPlayer> Role(string name)
    {
        if (!ReferenceEquals(_playersWorld, World.Api))
        {
            _players.Clear();
            _playersWorld = World.Api;
        }
        if (!_players.TryGetValue(name, out var player))
            _players[name] = player = await World.JoinPlayer(name);
        var p = player.Player;
        p.WorldData.CurrentGameMode = EnumGameMode.Survival;
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in p.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        Standing.Ledger.Reset(p.PlayerUID, null);
        return player;
    }
}
