using System.Text.Json.Nodes;
using Newtonsoft.Json;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Admin;

/// <summary>
/// The admin state sections of the systems that existed when the admin tools came (#459): each is
/// the system's own save format, so an export is what the savegame holds. Orders, deliveries and
/// visitors register theirs on <see cref="TradingSystem.AdminState"/> in their own systems.
/// </summary>
public static class TradeAdminStates
{
    public static void Register(ICoreServerAPI api, AdminStateBook book)
    {
        if (EconomySystem.Of(api) is { RegionalSupply: true } economy)
            book.Register(new DelegateAdminState("supply",
                () => JsonNode.Parse(economy.Supply.ToJson())!,
                data =>
                {
                    economy.Supply.ReplaceWith(SupplyBook.FromJson(data.ToJsonString(), economy.Supply.Settings));
                    economy.RefreshTraders();
                }));
        if (StandingSystem.Of(api) is { Enabled: true } standing)
            book.Register(new DelegateAdminState("standing",
                () => JsonNode.Parse(JsonConvert.SerializeObject(standing.Ledger.State))!,
                data =>
                {
                    var state = JsonConvert.DeserializeObject<StandingState>(data.ToJsonString()) ?? throw new FormatException("empty standing");
                    standing.Ledger.State.Players = state.Players;
                    standing.Ledger.State.Companies = state.Companies;
                    standing.Ledger.State.Designated = state.Designated;
                }));
        if (api.ModLoader.GetModSystem<OreSystem>()?.Deposits is { } deposits)
            book.Register(new DelegateAdminState("deposits",
                () => JsonNode.Parse(deposits.Registry.Serialize())!,
                data => deposits.Registry.ReplaceWith(DepositRegistry.Parse(data.ToJsonString()))));
    }
}
