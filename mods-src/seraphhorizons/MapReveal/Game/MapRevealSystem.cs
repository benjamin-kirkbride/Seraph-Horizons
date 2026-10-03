using SeraphHorizons.Mod.MapReveal.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.MapReveal;

/// <summary>
/// Map Reveal (a Seraph Horizons feature, switch <see cref="SeraphHorizonsConfig.MapReveal"/>):
/// <c>/revealmap &lt;radius&gt;</c> shows on the caller's world map the terrain already generated
/// around them (e.g. by <c>/wgen pregen</c>), without them going there. The game's map only draws
/// chunks the client has loaded, so the server reads the columns from the savegame
/// (<see cref="MapRevealServer"/>) and the client draws and stores them like explored ones
/// (<see cref="MapRevealClient"/>). The sampling, shading and wire format are in Core/.
///
/// The network channel is registered on both sides whatever the switch, so a client and a server
/// with different settings still agree on it. With the switch off on the server there is no
/// command; on a client, nothing is patched and whatever arrives is ignored.
/// </summary>
public class MapRevealSystem : ModSystem
{
    public const string ChannelName = "seraphhorizons-mapreveal";

    private MapRevealServer? _server;
    private MapRevealClient? _client;

    /// <summary>The block properties the map colours by, for every block id, as the game's
    /// <c>ChunkMapLayer</c> reads them (its <c>isLake</c>, and the snow it looks through).</summary>
    public static BlockTraits MapTraits(IList<Block> blocks)
    {
        var lake = new bool[blocks.Count];
        var snow = new bool[blocks.Count];
        for (int id = 0; id < blocks.Count; id++)
        {
            if (blocks[id] is not { } block) continue;
            lake[id] = block.BlockMaterial == EnumBlockMaterial.Water
                       || (block.BlockMaterial == EnumBlockMaterial.Ice && block.Code?.Path != "glacierice");
            snow[id] = block.BlockMaterial == EnumBlockMaterial.Snow;
        }
        return new BlockTraits(lake, snow);
    }

    internal static bool Enabled(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).MapReveal;

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(ChannelName).RegisterMessageType<MapRevealPacket>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!Enabled(api))
        {
            api.Logger.Notification("[seraphhorizons] Map Reveal is off (MapReveal in ModConfig/{0}): no /{1} command",
                SeraphHorizonsSystem.ConfigFile, MapRevealServer.CommandName);
            return;
        }
        _server = new MapRevealServer(api, api.Network.GetChannel(ChannelName));
        _server.RegisterCommand();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        var channel = api.Network.GetChannel(ChannelName);
        if (!Enabled(api))
        {
            channel.SetMessageHandler<MapRevealPacket>(_ => { });
            return;
        }
        _client = MapRevealClient.Start(api, channel);
    }

    public override void Dispose()
    {
        _server?.Dispose();
        _server = null;
        _client?.Dispose();
        _client = null;
    }
}
