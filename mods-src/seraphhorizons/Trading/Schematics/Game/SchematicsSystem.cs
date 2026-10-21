using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Values;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Schematics;

/// <summary>
/// Schematics (#468, #469; README "Schematics", docs/trading.md "Schematics").
///
/// <list type="bullet">
/// <item>Switch <see cref="SeraphHorizonsConfig.TraderSchematics"/>: every schematic in the pack
/// (<c>config/schematic-gates.json</c> "sold") comes only from traders. <see cref="LootScrub"/> takes
/// them out of loot lists and structures; <see cref="SchematicRecipes"/> removes the recipes that
/// copy or make them and keeps them on crafting everywhere else.</item>
/// <item>Switch <see cref="SeraphHorizonsConfig.MachineSchematics"/>: every machine's and vehicle's
/// first-stage grid recipe takes its <c>seraphhorizons:schematic-{machine}</c>
/// (<see cref="SchematicRecipes"/>). Off, no catalog sells those schematics either
/// (<see cref="SchematicCatalog.ForSale"/>); the items still exist, so a world that had them keeps
/// them.</item>
/// <item>The catalogs (<see cref="Catalog"/>, <see cref="BuySchematic"/>): each trader type sells its
/// schematics ("sales") off the shelf, on the trade window's Schematics tab, at a flat price, never
/// sold out, each unlocked from its standing tier by the best tier among the player and their group
/// mates (<see cref="StandingSystem.GroupTierFor"/>). A gate with any of its <see cref="SchematicGate.Switches"/>
/// is off sells nowhere.</item>
/// <item>No trader buys a schematic or a rolled copy of one (<see cref="SchematicTable.NeverBoughtBack"/>,
/// asked by the economy on both sides; the table loads on the client for that).</item>
/// </list>
/// Clients get the changed item types and recipes from the server.
///
/// ExecuteOrder 0.11: its AssetsLoaded runs after the game's patch loader (0.05) and before the item
/// and block types are read from the assets (0.2).
/// </summary>
public class SchematicsSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.schematics";
    public static readonly AssetLocation TableAsset = new("seraphhorizons", "config/schematic-gates.json");

    private ICoreServerAPI? _sapi;
    private Harmony? _harmony;
    private bool _trader, _machine;
    private HashSet<string> _off = [];

    /// <summary>The table, or null when it is missing or broken (both features and the catalogs are off then).</summary>
    public SchematicTable? Table { get; private set; }

    /// <summary>What the recipe pass did (after ModsAndConfigReady).</summary>
    public SchematicRecipeReport? Report { get; private set; }

    public static SchematicsSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<SchematicsSystem>();

    public override double ExecuteOrder() => 0.11;

    public override void Start(ICoreAPI api)
    {
        bool server = api.Side == EnumAppSide.Server;
        var asset = api.Assets.TryGet(TableAsset);
        if (asset is null)
        {
            if (server) api.Logger.Warning("[seraphhorizons] Schematics: {0} is missing; schematics and machine recipes are left as the mods ship them", TableAsset);
            return;
        }
        try
        {
            Table = SchematicTable.Parse(asset.ToText());
        }
        catch (Exception e)
        {
            if (server) api.Logger.Error("[seraphhorizons] Schematics: {0} does not parse; schematics and machine recipes are left as the mods ship them: {1}", TableAsset, e.Message);
            return;
        }
        if (!server) return;
        var config = SeraphHorizonsSystem.ConfigFor(api);
        _trader = config.TraderSchematics;
        _machine = config.MachineSchematics;
        _off = ValueHandbook.OffSwitches(config).ToHashSet();
        var switches = typeof(SeraphHorizonsConfig).GetProperties().Where(p => p.PropertyType == typeof(bool)).Select(p => p.Name).ToList();
        foreach (string problem in Table.Problems(TraderTypes.All.Concat(TraderTypes.Story).ToList(), TradeListResolver.MaxStandingTier, switches))
            api.Logger.Warning("[seraphhorizons] Schematics: {0}", problem);
        api.Logger.Notification("[seraphhorizons] Schematics: sold only by traders {0}; machine schematics {1}",
            _trader ? "on" : "off", _machine ? "on" : "off");
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && _trader && Table != null) LootScrub.ScrubTypeAssets(api, Table);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        if (Table is not { } table) return;
        if (_trader) LootScrub.Bind(_harmony ??= new Harmony(HarmonyId), table);
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

    // ---- The catalogs (server) ----

    /// <summary>Server: whether a <see cref="SeraphHorizonsConfig"/> switch is off.</summary>
    public bool IsOff(string name) => _off.Contains(name);

    /// <summary>Server: trader type <paramref name="traderType"/>'s catalog at tier
    /// <paramref name="tier"/> (null: standing off, all unlocked), the schematics that exist in
    /// this game only.</summary>
    public List<CatalogRow> Catalog(string traderType, int? tier)
    {
        if (Table is not { } table || _sapi is null) return [];
        return SchematicCatalog.For(table, traderType, IsOff, tier).Where(r => Item(r.Code) != null).ToList();
    }

    /// <summary>Server: the trader's catalog for this player, unlocked by the best standing tier among
    /// them and their group mates there (standing off: everything).</summary>
    public List<CatalogRow> CatalogFor(IServerPlayer player, EntitySeraphTrader trader)
    {
        int? tier = TradingSystem.Of(_sapi!)?.Standing is StandingSystem { Enabled: true } standing
            ? standing.GroupTierFor(player.PlayerUID, trader) : null;
        return Catalog(trader.TraderType, tier);
    }

    private Item? Item(string code) => _sapi?.World.GetItem(new AssetLocation(code));

    /// <summary>
    /// Server: buys schematic <paramref name="code"/> from the trader's catalog, at the price the
    /// player saw (<paramref name="expectPrice"/>, null not to check): refused when it is not in the
    /// catalog, locked for them, repriced, or they lack room or gears. Paid into the trader's wallet,
    /// earning standing as any deal does.
    /// </summary>
    public (bool Ok, string Key, object[] Args) BuySchematic(IServerPlayer player, EntitySeraphTrader trader, string? code, int? expectPrice)
    {
        if (code is null || CatalogFor(player, trader).FirstOrDefault(r => r.Code == CodePattern.Normalise(code)) is not { } row)
            return (false, "trading-window-changed", []);
        if (!row.Unlocked) return (false, "trading-schematics-error-locked", []);
        if (expectPrice is int seen && seen != row.Price) return (false, "trading-window-changed", []);
        if (Item(row.Code) is not { } item) return (false, "trading-window-failed", []);
        var stack = new ItemStack(item);
        if (!Window.TradeWindowSystem.HasRoom(player, stack)) return (false, Window.Core.TradeGuard.NoRoomKey, []);
        if (InventoryTrader.GetPlayerAssets(player.Entity) < row.Price) return (false, "trading-schematics-error-gears", [row.Price]);

        InventoryTrader.DeductFromEntity(_sapi, player.Entity, row.Price);
        trader.Inventory.GiveToTrader(row.Price);
        TradingSystem.Of(_sapi!)?.Standing.OnDeal(player, trader, row.Price, 0);
        if (!player.Entity.TryGiveItemStack(stack)) _sapi!.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("schematics", $"{player.PlayerName} buys {row.Code} from a {trader.TraderType} for {row.Price}");
        return (true, "trading-window-schematic-bought", []);
    }

    public override void Dispose()
    {
        LootScrub.Unbind();
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }
}
