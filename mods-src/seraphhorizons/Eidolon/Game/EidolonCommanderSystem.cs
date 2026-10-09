using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

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
/// so a world that has one keeps it. The tool's behaviour (binding, the mode wheel) is #675's: it
/// will register an item class here and name it in the item type.
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
