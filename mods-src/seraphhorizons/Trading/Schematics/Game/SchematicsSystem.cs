using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Schematics;

/// <summary>
/// Schematics (#468, #469; README "Schematics", docs/trading.md "Schematics"), server side: clients
/// get the changed item types and recipes from the server.
///
/// <list type="bullet">
/// <item>Switch <see cref="SeraphHorizonsConfig.TraderSchematics"/>: every schematic in the pack
/// (<c>config/schematic-gates.json</c> "sold") comes only from traders. <see cref="LootScrub"/> takes
/// them out of loot lists and structures; <see cref="SchematicRecipes"/> removes the recipes that
/// copy or make them and keeps them on crafting everywhere else.</item>
/// <item>Switch <see cref="SeraphHorizonsConfig.MachineSchematics"/>: every machine's and vehicle's
/// first-stage grid recipe takes its <c>seraphhorizons:schematic-{machine}</c>
/// (<see cref="SchematicRecipes"/>). Off, the traders don't sell those schematics either
/// (<see cref="TradingSystem.ExcludeEntry"/>); the items still exist, so a world that had them keeps
/// them.</item>
/// </list>
/// The traders sell them from their lists' cores, each from its standing tier
/// (<see cref="TradeEntry.StandingTier"/>); the table's "sales" is what the tests hold the lists to.
///
/// ExecuteOrder 0.11: its AssetsLoaded runs after the game's patch loader (0.05) and before the item
/// and block types are read from the assets (0.2).
/// </summary>
public class SchematicsSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.schematics";
    public static readonly AssetLocation TableAsset = new("seraphhorizons", "config/schematic-gates.json");

    private Harmony? _harmony;
    private bool _trader, _machine;

    /// <summary>The table, or null when it is missing or broken (both features are off then).</summary>
    public SchematicTable? Table { get; private set; }

    /// <summary>What the recipe pass did (after ModsAndConfigReady).</summary>
    public SchematicRecipeReport? Report { get; private set; }

    public static SchematicsSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<SchematicsSystem>();

    public override double ExecuteOrder() => 0.11;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void Start(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        _trader = config.TraderSchematics;
        _machine = config.MachineSchematics;
        var asset = api.Assets.TryGet(TableAsset);
        if (asset is null)
        {
            api.Logger.Warning("[seraphhorizons] Schematics: {0} is missing; schematics and machine recipes are left as the mods ship them", TableAsset);
            _trader = _machine = false;
            return;
        }
        try
        {
            Table = SchematicTable.Parse(asset.ToText());
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Schematics: {0} does not parse; schematics and machine recipes are left as the mods ship them: {1}", TableAsset, e.Message);
            _trader = _machine = false;
            return;
        }
        foreach (string problem in Table.Problems(TraderTypes.All, TradeListResolver.MaxStandingTier))
            api.Logger.Warning("[seraphhorizons] Schematics: {0}", problem);
        api.Logger.Notification("[seraphhorizons] Schematics: sold only by traders {0}; machine schematics {1}",
            _trader ? "on" : "off", _machine ? "on" : "off");
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (_trader && Table != null) LootScrub.ScrubTypeAssets(api, Table);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Table is not { } table) return;
        if (_trader) LootScrub.Bind(_harmony ??= new Harmony(HarmonyId), table);
        if (!_machine && TradingSystem.Of(api) is { } trading)
            trading.Exclude(e => table.IsMachineSchematic(e.Code));
        if (_trader || _machine)
            api.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, () => ApplyRecipes(api, table));
    }

    private void ApplyRecipes(ICoreServerAPI api, SchematicTable table)
    {
        Report = SchematicRecipes.Apply(api.World, api, table, _trader, _machine);
        api.Logger.Notification("[seraphhorizons] Schematics: {0} recipes gated, {1} recipes making or copying a schematic removed ({2}), {3} schematic slots kept on crafting",
            Report.Gated.Count, Report.Removed.Count, string.Join(", ", Report.Removed), Report.Kept);
        api.Logger.Debug("[seraphhorizons] Schematics: gated {0}", string.Join(", ", Report.Gated.Distinct().OrderBy(g => g)));
        if (Report.Failed.Count > 0)
            api.Logger.Warning("[seraphhorizons] Schematics: {0} gated recipes had no room for their schematic and are left ungated: {1}",
                Report.Failed.Count, string.Join("; ", Report.Failed));
    }

    public override void Dispose()
    {
        LootScrub.Unbind();
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }
}
