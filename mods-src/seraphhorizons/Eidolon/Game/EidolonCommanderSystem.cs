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
/// game (<see cref="Disable"/>) and the curio dealer does not stock the schematic or the pump head
/// (<see cref="TradingSystem.Exclude"/>). The schematic item stays, as every machine schematic does,
/// so a world that has one keeps it.
///
/// <para>The tool's behaviour (#675) is <see cref="ItemEidolonCommander"/>: binding, the mode wheel
/// (<see cref="EidolonCommandModes"/>, with follow and stay registered here) and marking. This system
/// also registers the follow order (<see cref="FollowOrder"/>) and the self-defence AI task
/// (<see cref="AiTaskEidolonDefend"/>), and on the client highlights the held tool's marks for its
/// holder (<see cref="Highlight"/>).</para>
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

    /// <summary>What the curio dealer stocks for the eidolon, left off the lists with the switch off.</summary>
    public static readonly IReadOnlySet<string> TradeStock = new HashSet<string>(StringComparer.Ordinal) { SchematicCode, PumpHeadCode };

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>Whether the eidolon is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).Eidolon;

    /// <summary>Whether a trade list entry's code is the eidolon's stock (codes without a domain are the game's).</summary>
    public static bool IsTradeStock(string code) => TradeStock.Contains(CodePattern.Normalise(code));

    /// <summary>The client's highlight slot for the held tool's marks.</summary>
    public const int HighlightSlot = 6750;

    private ICoreClientAPI? _capi;
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
        api.Event.RegisterGameTickListener(_ => Highlight(), 250);
    }

    /// <summary>Highlights the marks the held command tool keeps for its mode (an area as a box, a
    /// first corner or a block as one block), for its holder only; clears them when it is put away.</summary>
    private void Highlight()
    {
        if (_capi?.World?.Player is not { } player)
            return;
        var stack = player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        var positions = new List<BlockPos>();
        var colors = new List<int>();
        if (stack?.Collectible is ItemEidolonCommander && ItemEidolonCommander.ModeOf(stack) is { Mark: not EidolonMarkKind.None } mode)
        {
            var marks = ItemEidolonCommander.GetMarks(stack, mode.Code);
            if (marks.Area is { } area)
            {
                positions.Add(new BlockPos(area.Min.X, area.Min.Y, area.Min.Z));
                positions.Add(new BlockPos(area.Max.X + 1, area.Max.Y + 1, area.Max.Z + 1));
                colors.Add(ColorUtil.ToRgba(60, 80, 200, 120));
                if (marks.Third is { } block)
                {
                    positions.Add(new BlockPos(block.X, block.Y, block.Z));
                    positions.Add(new BlockPos(block.X + 1, block.Y + 1, block.Z + 1));
                    colors.Add(ColorUtil.ToRgba(90, 230, 190, 60));
                }
            }
            else if (marks.First is { } first)
            {
                positions.Add(new BlockPos(first.X, first.Y, first.Z));
                positions.Add(new BlockPos(first.X + 1, first.Y + 1, first.Z + 1));
                colors.Add(ColorUtil.ToRgba(90, 230, 190, 60));
            }
        }
        string key = string.Join(";", positions.Select(p => $"{p.X},{p.Y},{p.Z}"));
        if (key == _shown)
            return;
        _shown = key;
        _capi.World.HighlightBlocks(player, HighlightSlot, positions, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cube);
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
