using SeraphHorizons.Mod.Admin;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Admin;

/// <summary>Adds to the trade overlay around an admin at (x, z) within a radius.</summary>
public delegate void TradeOverlayContributor(IServerPlayer admin, int x, int z, int radius, MapOverlay overlay);

/// <summary>
/// The trader admin tools (#459): <see cref="TradeAdminCommands"/>, the state sections of supply,
/// standing and the deposit registry (<see cref="TradeAdminStates"/>), and the trade map overlay.
/// Runs after the trading systems it reads (economy 0.65); switch
/// <see cref="SeraphHorizonsConfig.AdminTools"/>. Server only.
///
/// Hooks for the later waves: <see cref="TradingSystem.AdminState"/> (export sections),
/// <see cref="InspectContributors"/> (lines in <c>/sh trade inspect</c>: open orders, deliveries),
/// <see cref="OverlayContributors"/> (the trade overlay: delivery routes), and
/// <see cref="AdminLogs.Trade"/> (the <c>orders</c>, <c>deliveries</c>, <c>maps</c> and
/// <c>visitors</c> channels).
/// </summary>
public class TradingAdminSystem : ModSystem
{
    public static TradingAdminSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<TradingAdminSystem>();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override double ExecuteOrder() => 0.7;

    public List<Action<TraderInspection>> InspectContributors { get; } = [];

    public List<TradeOverlayContributor> OverlayContributors { get; } = [];

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).AdminTools) return;
        if (TradingSystem.Of(api) is not { } trading) return;
        var commands = new TradeAdminCommands(api, trading, this);
        commands.Register();
        AdminSystem.Of(api)?.MapProviders.TryAdd("trade", (player, item) =>
            commands.Overlay(player, (int)player.Entity.Pos.X, (int)player.Entity.Pos.Z, TradeAdminCommands.DefaultMapRadius,
                string.IsNullOrEmpty(item) ? null : item));
        // The systems register in StartServerSide; the deposit service and standing exist by now,
        // supply's switch is read in Start.
        TradeAdminStates.Register(api, trading.AdminState);
    }
}
