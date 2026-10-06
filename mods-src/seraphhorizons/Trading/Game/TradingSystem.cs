using SeraphHorizons.Mod.Trading.Commands;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The trader overhaul's foundation (#436: #447, #448), server side but for the entity class.
///
/// <list type="bullet">
/// <item>Eleven trader types (<see cref="TraderTypes"/>), entity class <see cref="EntitySeraphTrader"/>,
/// registered on both sides whatever the switch (a client must know the class of an entity it is
/// sent), stocked from the pack's lists (<see cref="TradeLists"/>) by region.</item>
/// <item>Camps on a seeded 2 km grid (<see cref="TraderGrid"/>, <see cref="TraderCamps"/>), switch
/// <see cref="SeraphHorizonsConfig.TraderGrid"/>, in new worlds only: whether a world has the grid
/// is decided at its first start with this mod and saved with it (<see cref="GridStateKey"/>), so an
/// existing world keeps vanilla's camps and traders.</item>
/// <item>Admin commands under <c>/sh trade</c> (<see cref="TradeCommands"/>).</item>
/// </list>
///
/// ExecuteOrder: after the game's GenStructuresPosPass (0.5), so its chunk handler runs after the
/// game's structure passes and its worldgen init after GenStructures' (both register in
/// StartServerSide, which runs in ExecuteOrder).
/// </summary>
public class TradingSystem : ModSystem
{
    public const string GridStateKey = "seraphhorizons:tradergrid";

    private ICoreServerAPI? _sapi;
    private bool? _gridActive;
    private string _gridOffReason = "";

    public static TradingSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<TradingSystem>();

    public override double ExecuteOrder() => 0.6;

    /// <summary>The lists, loaded once the world's items exist (server).</summary>
    public TradeLists? Lists { get; private set; }

    public RegionClassifier Classifier { get; private set; } = new();

    /// <summary>What decides whether a player-supplied good is shelved; #451 replaces it.</summary>
    public ISupplyGate SupplyGate { get; set; } = NoSupply.Instance;

    /// <summary>Standing with traders (#452, #463); <see cref="StandingSystem"/> sets it when on.</summary>
    public IStandingSource Standing { get; set; } = NoStanding.Instance;

    /// <summary>Entries left out of the lists when they load (set before GameReady): the machines'
    /// schematics with their switch off (#469).</summary>
    public Predicate<TradeEntry>? ExcludeEntry { get; set; }

    /// <summary>The admin state sections <c>/sh trade export|import</c> move (#459): each system
    /// registers its own (<c>Register(IAdminState)</c>); supply, standing and the deposit registry
    /// are registered by <c>Trading/Admin/TradingAdminSystem</c>.</summary>
    public SeraphHorizons.Mod.Core.AdminStateBook AdminState { get; } = new();

    /// <summary>The camp grid of this world's seed (server, once the lists are loaded).</summary>
    public TraderGrid? Grid { get; private set; }

    public TraderCamps? Camps { get; private set; }

    /// <summary>Whether this world places camps on the grid: the switch is on and the world has had
    /// the grid since its first start.</summary>
    public bool GridActive => _gridActive ?? (_sapi is null ? false : (_gridActive = DecideGrid()).Value);

    /// <summary>Why the grid is not placing camps: the world, the switch, or the lists failing to load.</summary>
    public string GridOffReason => !GridActive ? _gridOffReason : Grid is null ? "the trade lists did not load" : "";

    /// <summary>The grid is on and ready (the lists loaded).</summary>
    public bool GridReady => GridActive && Grid != null && Camps != null;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity(EntitySeraphTrader.ClassName, typeof(EntitySeraphTrader));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        Camps = new TraderCamps(api, this);
        Camps.Register();
        // Whether this world has the grid is decided with the savegame; the lists need the world's
        // items and blocks, which the server finishes loading after the savegame (just before
        // GameReady), and worldgen starts at the end of WorldReady, after them.
        api.Event.SaveGameLoaded += () => _ = GridActive;
        api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, LoadLists);
        TradeCommands.Register(api, this);
    }

    private void LoadLists()
    {
        var api = _sapi!;
        Classifier = new RegionClassifier(RegionProbe.RockGroups(api));
        Lists = TradeLists.Load(api, ExcludeEntry);
        foreach (string problem in Lists.Problems)
            api.Logger.Warning("[seraphhorizons] Trading: trade list {0}", problem);
        if (Lists.Unresolved.Count > 0)
            api.Logger.Warning("[seraphhorizons] Trading: {0} trade list entries name nothing in this game and are left out: {1}",
                Lists.Unresolved.Count, string.Join(", ", Lists.Unresolved));
        Grid = new TraderGrid(api.World.Seed, Lists.CampWeights);
        api.Logger.Notification("[seraphhorizons] Trading: {0} trade lists loaded; trader grid {1}", Lists.Lists.Count,
            GridActive ? "on" : $"off ({GridOffReason})");
    }

    private bool DecideGrid()
    {
        var api = _sapi!;
        var saved = api.WorldManager.SaveGame.GetData<string>(GridStateKey);
        if (saved is null)
        {
            // First start with this mod: a new world takes the grid if the switch is on; a world
            // that existed before keeps vanilla's camps for good.
            saved = api.WorldManager.SaveGame.IsNew && SeraphHorizonsSystem.ConfigFor(api).TraderGrid ? "on" : "off";
            api.WorldManager.SaveGame.StoreData(GridStateKey, saved);
        }
        if (saved != "on")
        {
            _gridOffReason = "this world was created without it";
            return false;
        }
        if (!SeraphHorizonsSystem.ConfigFor(api).TraderGrid)
        {
            _gridOffReason = $"TraderGrid is off in ModConfig/{SeraphHorizonsSystem.ConfigFile}";
            return false;
        }
        return true;
    }

    internal void DisableGrid(string reason)
    {
        _gridActive = false;
        _gridOffReason = reason;
    }

    /// <summary>A trader's TradeProps: the wallet (standing tier 0) and empty lists, so vanilla's
    /// own stocking runs and adds nothing (<see cref="EntitySeraphTrader"/>).</summary>
    public TradeProperties TradePropsFor(string type)
    {
        var wallet = Lists?.For(type)?.WalletFor(0) ?? new NatSpec(60, 10);
        return EmptyTradeProps(wallet.Avg, wallet.Var);
    }

    public static TradeProperties EmptyTradeProps(float avg, float var) => new()
    {
        Money = NatFloat.createUniform(avg, var),
        Buying = new TradeList { MaxItems = 0, List = [] },
        Selling = new TradeList { MaxItems = 0, List = [] },
    };
}
