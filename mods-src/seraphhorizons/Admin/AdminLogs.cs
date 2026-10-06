using SeraphHorizons.Mod.Core;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Admin;

/// <summary>
/// The two admin logs, in the server's <c>Logs</c> folder, every channel off until an admin switches
/// it on (<c>/sh ore log on</c>, <c>/sh trade log on [channel]</c>); they stay as set until the server
/// stops. Features write to them with one line each where something is decided:
/// <c>AdminLogs.Ore?.Write("placement", ...)</c>. Null with the admin tools off.
/// </summary>
public static class AdminLogs
{
    public const string OreFile = "seraphhorizons-ore.log";
    public const string TradeFile = "seraphhorizons-trade.log";

    /// <summary>Ore cells' and placer fields' decisions, and deposit verifications.</summary>
    public static readonly string[] OreChannels = ["placement", "verify"];

    public static readonly string[] TradeChannels = ["supply", "standing", "orders", "deliveries", "maps", "visitors"];

    public static ChannelLog? Ore { get; private set; }

    public static ChannelLog? Trade { get; private set; }

    internal static void Open()
    {
        Close();
        Ore = new ChannelLog(Path.Combine(GamePaths.Logs, OreFile), OreChannels);
        Trade = new ChannelLog(Path.Combine(GamePaths.Logs, TradeFile), TradeChannels);
    }

    internal static void Close()
    {
        Ore?.Dispose();
        Ore = null;
        Trade?.Dispose();
        Trade = null;
    }
}
