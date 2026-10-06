using System.Globalization;
using SeraphHorizons.Mod.Trading.Values.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Values;

/// <summary>
/// Item base values (#449): loads <c>config/item-values.json</c>, the table <c>tools/item-values</c>
/// derives from the pack's recipe export, and serves it to the trading features
/// (<c>ItemValuesSystem.For(api).ValueOf(code)</c>). Both sides, and with no switch of its own:
/// it changes nothing in play, it only answers the features that price things and the admin
/// command <c>/sh trade value [item]</c>.
/// </summary>
public class ItemValuesSystem : ModSystem
{
    public static readonly AssetLocation Asset = new("seraphhorizons", "config/item-values.json");

    /// <summary>The table, empty until assets are loaded or when the asset is missing or broken.</summary>
    public ItemValues Values { get; private set; } = ItemValues.Empty;

    public static ItemValues For(ICoreAPI api) =>
        api.ModLoader.GetModSystem<ItemValuesSystem>()?.Values ?? ItemValues.Empty;

    // Both sides: the client prices goods off a trader's list itself, to show the offer (#450).
    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void AssetsLoaded(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(Asset);
        if (asset == null)
        {
            api.Logger.Warning("[seraphhorizons] Item values: {0} is missing; every item is unpriced", Asset);
            return;
        }
        try
        {
            Values = ItemValues.Parse(asset.ToText());
            api.Logger.Notification("[seraphhorizons] Item values: {0} items priced", Values.Count);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Item values: {0} does not parse, every item is unpriced: {1}", Asset, e.Message);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var sh = api.ChatCommands.GetOrCreate("sh");
        // Shared with the other trading features: whoever registers first describes the root.
        if (string.IsNullOrEmpty(sh.Description))
            sh.WithDescription("Seraph Horizons commands").RequiresPrivilege(Privilege.chat);
        sh.BeginSubCommand("trade")
            .WithDescription("Trading")
            .BeginSubCommand("value")
                .WithDescription("Base value of an item in rusty gears, and where it comes from (the held item if none is given)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("item code"))
                .HandleWith(args => OnValue(api, args))
            .EndSubCommand()
        .EndSubCommand();
    }

    private TextCommandResult OnValue(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        string? code = args[0] as string;
        if (string.IsNullOrEmpty(code))
            code = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
        if (string.IsNullOrEmpty(code))
            return TextCommandResult.Error("Give an item code, or hold the item.");
        var l = Values.Lookup(code);
        SeraphHorizons.Mod.Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
        {
            ["code"] = l.Code, ["value"] = l.Value, ["effective"] = l.Effective, ["floorZero"] = l.FloorZero,
            ["source"] = l.Source.ToString().ToLowerInvariant(), ["family"] = l.Family, ["members"] = l.Members,
        });
        return TextCommandResult.Success(Describe(l));
    }

    public static string Describe(ValueLookup l)
    {
        var ci = CultureInfo.InvariantCulture;
        return l.Source switch
        {
            ValueSource.Missing => $"{l.Code}: no value (missing from the table and from every family)",
            ValueSource.Direct => $"{l.Code}: {l.Value.ToString("0.###", ci)} gears{(l.FloorZero ? ", worthless (under a gear per stack)" : "")} (direct)",
            _ => $"{l.Code}: {l.Value.ToString("0.###", ci)} gears{(l.FloorZero ? ", worthless" : "")} (family fallback: average of {l.Members} in {l.Family})",
        };
    }
}
