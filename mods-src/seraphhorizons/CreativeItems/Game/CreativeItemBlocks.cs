using System.Text;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.CreativeItems;

/// <summary>
/// The creative item source and sink (README "Creative item source and sink"), two creative-only
/// blocks for testing item transport. The source (<see cref="BlockEntityCreativeItemSource"/>)
/// copies an item put in its one slot and feeds it, without end, to a hopper under it; the sink
/// (<see cref="BlockEntityCreativeItemSink"/>) takes whatever chutes and hoppers push into it,
/// counts it by item and voids it. Each counts what goes through and its rate, shown in its
/// dialog and its block info. One switch, <c>CreativeItemSource</c>, has both.
/// </summary>
public static class CreativeItemBlocks
{
    public const string SourceCode = "creativeitemsource";
    public const string SinkCode = "creativeitemsink";
    public const string SourceClass = "seraphhorizons.CreativeItemSource";
    public const string SinkClass = "seraphhorizons.CreativeItemSink";

    public static readonly AssetLocation SourceAsset = new("seraphhorizons", "blocktypes/" + SourceCode + ".json");
    public static readonly AssetLocation SinkAsset = new("seraphhorizons", "blocktypes/" + SinkCode + ".json");
    public static readonly AssetLocation[] TypeAssets = [SourceAsset, SinkAsset];

    // Block entity packets from a client's dialog; the game's own container packets are 1000 and
    // 1001 (the dialog's close sends 1001, which these blocks ignore).
    public const int PacketSetItem = 1100;
    public const int PacketClearItem = 1101;
    public const int PacketSettings = 1102;
    public const int PacketReset = 1103;

    /// <summary>Both blocks and their block entities. Registered on both sides whatever the
    /// setting: the server decides whether the blocks exist, and a client must know the classes
    /// then.</summary>
    public static void RegisterClasses(ICoreAPI api)
    {
        api.RegisterBlockClass(SourceClass, typeof(BlockCreativeItems));
        api.RegisterBlockEntityClass(SourceClass, typeof(BlockEntityCreativeItemSource));
        api.RegisterBlockClass(SinkClass, typeof(BlockCreativeItems));
        api.RegisterBlockEntityClass(SinkClass, typeof(BlockEntityCreativeItemSink));
    }

    /// <summary>Leaves both blocks out of the game: marks their blocktypes disabled before the
    /// game loads blocktypes (this runs in AssetsLoaded, before the game's own loader at 0.2).</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in TypeAssets)
        {
            var asset = api.Assets.TryGet(location);
            if (asset == null)
                continue;
            var json = JObject.Parse(asset.ToText());
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    /// <summary>Whether <paramref name="player"/> may open and change either block: in creative
    /// mode, and allowed to use the block where it stands (as for any container). Checked by the
    /// server for every packet, and by the client before it opens a dialog.</summary>
    public static bool CanUse(IWorldAccessor world, IPlayer? player, Vintagestory.API.MathTools.BlockPos pos) =>
        player?.WorldData?.CurrentGameMode == EnumGameMode.Creative
        && world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.Use);
}

/// <summary>The item source's settings, sent by its dialog; clamped by the server.</summary>
[ProtoContract]
public class CreativeItemSourceSettings
{
    [ProtoMember(1)] public double Rate { get; set; }
    [ProtoMember(2)] public bool Unlimited { get; set; }
    [ProtoMember(3)] public bool Paused { get; set; }
    [ProtoMember(4)] public long Limit { get; set; }
}
