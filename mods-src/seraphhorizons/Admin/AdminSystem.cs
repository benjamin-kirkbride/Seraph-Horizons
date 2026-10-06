using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Admin;

/// <summary>
/// The admin tools' shared parts (#458, #459; docs/admin-tools.md), switch
/// <see cref="SeraphHorizonsConfig.AdminTools"/>:
/// <list type="bullet">
/// <item>The <c>/sh ore</c> and <c>/sh trade</c> conventions (<see cref="AdminCommands"/>): <c>--json</c>
/// and privilege <c>controlserver</c> on every subcommand, installed after every feature has
/// registered its commands (ExecuteOrder 1).</item>
/// <item>The admin map layer (<see cref="AdminMapLayer"/>, <see cref="AdminMapServer"/>) and its
/// network channel. The channel and the layer are registered on both sides whatever the switch, so a
/// client and a server with different settings still agree on them; with it off the server sends
/// nothing.</item>
/// </list>
/// The ore and trade commands themselves are in <c>Ore/Game/Commands/OreAdminCommands.cs</c> and
/// <c>Trading/Admin/</c>.
/// </summary>
public class AdminSystem : ModSystem
{
    public const string ChannelName = "seraphhorizons-admin";

    public static AdminSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<AdminSystem>();

    public override double ExecuteOrder() => 1.0;

    /// <summary>The map layer's server half; null with the switch off, or on the client.</summary>
    public AdminMapServer? Map { get; private set; }

    public bool Enabled { get; private set; }

    /// <summary>The admin map's overlay builders by key (<c>ore</c>, <c>trade</c>): (admin, the
    /// argument it was switched on with) → the overlay around them. Features add theirs in
    /// StartServerSide, before or after this system starts.</summary>
    public Dictionary<string, MapOverlayProvider> MapProviders { get; } = new(StringComparer.Ordinal);

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(ChannelName).RegisterMessageType<AdminMapPacket>();
        api.ModLoader.GetModSystem<WorldMapManager>()?.RegisterMapLayer<AdminMapLayer>(AdminMapLayer.Code, 2.0);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Enabled = SeraphHorizonsSystem.ConfigFor(api).AdminTools;
        if (!Enabled)
        {
            api.Logger.Notification("[seraphhorizons] Admin tools are off (AdminTools in ModConfig/{0}): no admin map, no --json",
                SeraphHorizonsSystem.ConfigFile);
            return;
        }
        Map = new AdminMapServer(api, api.Network.GetChannel(ChannelName), MapProviders);
        AdminLogs.Open();
        var sh = api.ChatCommands.GetOrCreate("sh");
        foreach (var node in new[] { "ore", "trade" })
            if (sh.AllSubcommands.TryGetValue(node, out var command))
                AdminCommands.Install(command);
        if (AdminCommands.Unsupported is { } why)
            api.Logger.Warning("[seraphhorizons] Admin tools: --json answers are off: {0}", why);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Network.GetChannel(ChannelName).SetMessageHandler<AdminMapPacket>(packet =>
        {
            if (string.IsNullOrEmpty(packet.Json)) AdminMapLayer.Overlays.TryRemove(packet.Key, out _);
            else AdminMapLayer.Overlays[packet.Key] = MapOverlay.FromJson(packet.Json);
        });
        api.Event.LeaveWorld += () => AdminMapLayer.Overlays.Clear();
    }

    public override void Dispose()
    {
        Map?.Dispose();
        Map = null;
        AdminLogs.Close();
    }
}
