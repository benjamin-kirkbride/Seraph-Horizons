using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's gate and its command tool (#670; README "Eidolon"): the command tool's item
/// (<c>seraphhorizons:eidoloncommander</c>) and grid recipe, and the curio dealer's eidolon stock, the
/// eidolon schematic and the Jonas pump head (the one Jonas part nothing else gives). The schematic
/// itself is a <c>machine</c> variant of the machines' schematic item, gated like theirs
/// (<c>config/schematic-gates.json</c>, machine <c>eidolon</c>: the gantry frame and the command tool).
///
/// With the <c>Eidolon</c> switch off the server leaves the command tool and its recipe out of the
/// game (<see cref="Disable"/>), the curio dealer does not stock the pump head
/// (<see cref="TradingSystem.Exclude"/>), and its catalog does not sell the schematic (the gate's
/// <c>switch</c>, <c>SchematicCatalog.ForSale</c>). The schematic item stays, as every machine
/// schematic does, so a world that has one keeps it.
///
/// <para>The tool's behaviour (#675) is <see cref="ItemEidolonCommander"/>: binding, the mode wheel
/// (<see cref="EidolonCommandModes"/>, with follow and stay registered here) and marking. This system
/// also registers the follow order (<see cref="FollowOrder"/>) and the self-defence AI task
/// (<see cref="AiTaskEidolonDefend"/>), and on the client highlights the held tool's marks for its
/// holder (<see cref="Highlight"/>, with x-ray outlines by <see cref="EidolonMarkOutlines"/>), and in mine
/// mode the blocks of its owner's mine orders out of their reach (<see cref="MineOutOfReachPacket"/>, in red).</para>
/// </summary>
public class EidolonCommanderSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string CommanderCode = "seraphhorizons:eidoloncommander";
    public const string SchematicCode = "seraphhorizons:schematic-eidolon";
    public const string PumpHeadCode = "game:jonasparts-pumphead";

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "itemtypes/eidoloncommander.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/eidoloncommander.json"),
    ];

    /// <summary>What the curio dealer shelves for the eidolon, left off the lists with the switch off
    /// (the schematic is its catalog's, which the gate's switch takes out).</summary>
    public static readonly IReadOnlySet<string> TradeStock = new HashSet<string>(StringComparer.Ordinal) { PumpHeadCode };

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>Whether the eidolon is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).Eidolon;

    /// <summary>Whether a trade list entry's code is the eidolon's stock (codes without a domain are the game's).</summary>
    public static bool IsTradeStock(string code) => TradeStock.Contains(CodePattern.Normalise(code));

    /// <summary>The client's highlight slot for the held tool's marks.</summary>
    public const int HighlightSlot = 6750;

    /// <summary>The fill of a closed area (green).</summary>
    public static readonly int AreaFill = ColorUtil.ToRgba(60, 80, 200, 120);

    /// <summary>The fill of a first corner or a marked block (sky blue).</summary>
    public static readonly int BlockFill = ColorUtil.ToRgba(90, 230, 190, 60);

    /// <summary>The fill of the blocks out of a mine order's reach (red: the low byte is red, as
    /// <see cref="EidolonMarkOutlines.LineColor"/> notes).</summary>
    public static readonly int OutOfReachFill = ColorUtil.ToRgba(60, 40, 40, 220);

    private ICoreClientAPI? _capi;
    private readonly Dictionary<MarkArea, IReadOnlyList<MarkArea>> _outOfReach = new();
    private EidolonMarkOutlines? _outlines;
    private string _shown = "";

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass(ItemEidolonCommander.ClassName, typeof(ItemEidolonCommander));
        AiTaskRegistry.Register<AiTaskEidolonDefend>(AiTaskEidolonDefend.Code);
        EidolonOrders.Register(FollowOrder.OrderCode, (_, args) => FollowOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = "follow",
            Order = 10,
            Icon = new AssetLocation("game", "textures/icons/call-back.svg"),
            Command = c => EidolonCommand.Order(FollowOrder.OrderCode, FollowOrder.Args(c.Player)),
        });
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = "stay",
            Order = 20,
            Icon = new AssetLocation("game", "textures/icons/worldmap/0-circle.svg"),
            Command = c => EidolonCommand.Order(StayOrder.OrderCode, StayOrder.Args(c.Eidolon.Pos.XYZ)),
        });
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        _outlines = new EidolonMarkOutlines(api);
        api.Network.GetChannel(EidolonMineSystem.Channel).SetMessageHandler<MineOutOfReachPacket>(OutOfReach);
        api.Event.RegisterGameTickListener(_ => Highlight(), 250);
    }

    /// <summary>A mine order's blocks out of its reach (none: its job is done), kept for its area.</summary>
    private void OutOfReach(MineOutOfReachPacket packet)
    {
        if (packet.Area.Length != 6)
            return;
        var area = new MarkArea(new MarkPos(packet.Area[0], packet.Area[1], packet.Area[2]), new MarkPos(packet.Area[3], packet.Area[4], packet.Area[5]));
        var boxes = new List<MarkArea>();
        for (int i = 0; i + 5 < packet.Boxes.Length; i += 6)
            boxes.Add(new MarkArea(new MarkPos(packet.Boxes[i], packet.Boxes[i + 1], packet.Boxes[i + 2]),
                new MarkPos(packet.Boxes[i + 3], packet.Boxes[i + 4], packet.Boxes[i + 5])));
        if (boxes.Count == 0)
            _outOfReach.Remove(area);
        else
            _outOfReach[area] = boxes;
    }

    public override void Dispose()
    {
        _outlines?.Dispose();
        _outlines = null;
    }

    /// <summary>Highlights the marks the held command tool keeps for its mode (an area as a box, a
    /// first corner or a block as one block), and in mine mode what its holder's mine orders cannot reach
    /// (<see cref="OutOfReachFill"/>), for its holder only; clears them when it is put away.
    /// Each mark is the game's translucent fill with an x-ray outline over it
    /// (<see cref="EidolonMarkOutlines"/>, which also previews an area before its second corner).</summary>
    private void Highlight()
    {
        if (_capi?.World?.Player is not { } player)
            return;
        var stack = player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        var boxes = new List<(MarkArea Box, int Fill)>();
        EidolonCommandMode? shown = null;
        var marks = Marks.None;
        if (stack?.Collectible is ItemEidolonCommander && ItemEidolonCommander.ModeOf(stack) is { Mark: not EidolonMarkKind.None } mode)
        {
            shown = mode;
            marks = ItemEidolonCommander.GetMarks(stack, mode.Code);
            if (marks.Area is { } area)
            {
                boxes.Add((area, AreaFill));
                if (marks.Third is { } block)
                    boxes.Add((MarkArea.Between(block, block), BlockFill));
            }
            else if (marks.First is { } first)
                boxes.Add((MarkArea.Between(first, first), BlockFill));
            if (mode.Code == MineOrder.OrderCode)
                foreach (var box in _outOfReach.Values.SelectMany(b => b))
                    boxes.Add((box, OutOfReachFill));
        }
        _outlines?.Show(shown, marks, boxes.Select(b => (b.Box, EidolonMarkOutlines.LineColor(b.Fill))));
        string key = string.Join(";", boxes.Select(b => $"{b.Box.Min}{b.Box.Max}{b.Fill}"));
        if (key == _shown)
            return;
        _shown = key;
        var positions = new List<BlockPos>();
        foreach (var (box, _) in boxes)
        {
            positions.Add(new BlockPos(box.Min.X, box.Min.Y, box.Min.Z));
            positions.Add(new BlockPos(box.Max.X + 1, box.Max.Y + 1, box.Max.Z + 1));
        }
        _capi.World.HighlightBlocks(player, HighlightSlot, positions, boxes.Select(b => b.Fill).ToList(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cube);
    }

    // Types and recipes are read from the assets later in this phase (the game's loaders run at 0.2
    // and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            Disable(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!Applies(api) && TradingSystem.Of(api) is { } trading)
            trading.Exclude(e => IsTradeStock(e.Code));
    }

    /// <summary>Leaves the command tool out of the game: marks its item type and its recipe disabled
    /// before the game loads them.</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in TypeAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), IgnoreComments);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
        foreach (var location in RecipeAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JArray.Parse(asset.ToText(), IgnoreComments);
            foreach (var recipe in json.OfType<JObject>())
                recipe["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }
}
