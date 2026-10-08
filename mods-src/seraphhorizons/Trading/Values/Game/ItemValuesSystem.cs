using System.Globalization;
using HarmonyLib;
using SeraphHorizons.Mod.Trading.Values.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Values;

/// <summary>
/// Item base values (#449): loads <c>config/item-values.json</c>, the table <c>tools/item-values</c>
/// derives from the pack's recipe export, and serves it to the trading features
/// (<c>ItemValuesSystem.For(api).ValueOf(code)</c>). Both sides, and with no switch of its own:
/// it changes nothing in play, it only answers the features that price things and the admin
/// command <c>/sh trade value [item]</c>. On the client it also puts the value on every handbook
/// page (<see cref="ValueHandbook"/>).
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

    private Harmony? _handbookHarmony;

    // The server tells clients which switches are off, for the handbook's value line.
    public override void Start(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
            ValueHandbook.Publish(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        var harmony = new Harmony(ValueHandbook.HarmonyId);
        if (ValueHandbook.Patch(harmony, api))
            _handbookHarmony = harmony;
    }

    public override void Dispose()
    {
        if (_handbookHarmony == null)
            return;
        _handbookHarmony.UnpatchAll(ValueHandbook.HarmonyId);
        _handbookHarmony = null;
        ValueHandbook.Unbind();
    }

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
            // value is in unit (gears per litre for a liquid, else per item); perItem and effective
            // are always per item, what trading prices with.
            ["code"] = l.Code, ["value"] = l.Display, ["unit"] = l.PerLitre is null ? "item" : "litre",
            ["itemsPerLitre"] = l.PerLitre, ["perItem"] = l.Value, ["effective"] = l.Effective, ["floorZero"] = l.FloorZero,
            ["source"] = l.Source.ToString().ToLowerInvariant(), ["family"] = l.Family, ["members"] = l.Members,
        });
        return TextCommandResult.Success(Describe(l));
    }

    public static string Describe(ValueLookup l)
    {
        var ci = CultureInfo.InvariantCulture;
        // A liquid: "1.85 gears per litre (0.0185 per item, 100 items per litre)".
        string amount = l.PerLitre is { } n
            ? $"{l.Display.ToString("0.###", ci)} gears per litre ({l.Value.ToString("0.######", ci)} per item, {n} items per litre)"
            : $"{l.Value.ToString("0.###", ci)} gears";
        return l.Source switch
        {
            ValueSource.Missing => $"{l.Code}: no value (missing from the table and from every family)",
            ValueSource.Direct => $"{l.Code}: {amount}{(l.FloorZero ? ", worthless (under a gear per stack)" : "")} (direct)",
            _ => $"{l.Code}: {amount}{(l.FloorZero ? ", worthless" : "")} (family fallback: average of {l.Members} in {l.Family})",
        };
    }
}
