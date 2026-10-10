using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// Stainless steel's crucible furnace (<c>StainlessSteel</c>, #484 part C, README "Crucible furnace"):
/// a row of melting holes on one brick chimney, fired with coke, each holding a fireclay melting pot
/// whose charge melts into ferrosilicon, ferrochrome or stainless steel (<see cref="PotRecipes"/>).
/// Registers the classes on both sides; on the server, with the switch off, leaves the holes, the
/// pots, the two ferroalloys, their recipes, the handbook patch and the guide page out of the game.
/// Settings in <c>CrucibleFurnaceSettings</c>.
/// </summary>
public class CrucibleFurnaceSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string HoleClass = "seraphhorizons.MeltingHole";
    public const string PotClass = "seraphhorizons.MeltingPot";
    public const string SmeltedPotClass = "seraphhorizons.MeltingPotSmelted";
    public const string GuidePageCode = "seraphhorizons-cruciblefurnace";
    public const string GuideTitleKey = "seraphhorizons:cruciblefurnace-title";
    public const string FiredPot = "seraphhorizons:meltingpot-fired";
    public const string SmeltedPot = "seraphhorizons:meltingpot-smelted";
    public const string ClosedHole = "seraphhorizons:meltinghole-closed";

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/cruciblefurnace/meltinghole.json"),
        new(Domain, "blocktypes/cruciblefurnace/meltingpot.json"),
        new(Domain, "itemtypes/ferrosilicon.json"),
        new(Domain, "itemtypes/ferrochrome.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/cruciblefurnace.json"),
        new(Domain, "recipes/clayforming/meltingpot.json"),
    ];

    public static readonly AssetLocation PatchAsset = new(Domain, "patches/stainless-steel.json");

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private CrucibleFurnaceConfig? _config;
    private PotRecipes? _recipes;
    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _hidePage;
    private HarmonyLib.Harmony? _bessemerHarmony;

    /// <summary>What <see cref="BessemerStainless.EnsureScrap"/> found on this side (the Bessemer route).</summary>
    public SteelBits.SmexScrap.Status BessemerScrapStatus { get; private set; } = SteelBits.SmexScrap.Status.NotChecked;

    public static CrucibleFurnaceSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<CrucibleFurnaceSystem>();

    /// <summary>Whether the furnace is in the game: its switch is on (the server's decides).</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).StainlessSteel;

    /// <summary>This side's settings, sanitised.</summary>
    public CrucibleFurnaceConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The pot recipes whose every item exists in this game (a recipe naming a mod that is
    /// not installed is left out, with a line in the log).</summary>
    public PotRecipes Recipes => _recipes ??= LoadRecipes(_api!);

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockClass(HoleClass, typeof(BlockMeltingHole));
        api.RegisterBlockEntityClass(HoleClass, typeof(BEMeltingHole));
        api.RegisterBlockClass(PotClass, typeof(BlockMeltingPot));
        api.RegisterBlockClass(SmeltedPotClass, typeof(BlockMeltingPotSmelted));
        // Before the game's patch loader, which applies the patches in AssetsLoaded. On the server
        // only: a client has no assets in Start.
        if (!Applies(api) && api.Side == EnumAppSide.Server && api.Assets.TryGet(PatchAsset) is { } patch)
            patch.Data = "[]"u8.ToArray();
        // The Bessemer route: smex's converter, patched on both sides, once per process.
        if (BessemerStainless.Applies(api) && (BessemerStainless.Bound || BessemerStainless.Bind(api.Logger)))
            _bessemerHarmony = BessemerStainless.Patch();
    }

    // Types and recipes are read from the assets later in this phase (the game's loaders run at 0.2
    // and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            Disable(api);
        if (BessemerStainless.Applies(api) && BessemerStainless.Bound)
            LangText.Apply(BessemerStainless.LangEdits, BessemerStainless.SmexId, api.Logger);
    }

    // After every mod's Start, where smex loads its settings.
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (!Applies(api))
            BessemerScrapStatus = SteelBits.SmexScrap.Status.Off;
        else if (!api.ModLoader.IsModEnabled(BessemerStainless.SmexId))
            BessemerScrapStatus = SteelBits.SmexScrap.Status.Absent;
        else if (!BessemerStainless.Bound)
            BessemerScrapStatus = SteelBits.SmexScrap.Status.Changed;
        else
            BessemerScrapStatus = BessemerStainless.EnsureScrap(api.Logger);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Applies(api))
            return;
        // The recipe export leaves the page out too.
        var hidden = api.ObjectCache.TryGetValue(WoodworkingGuide.HiddenGuidesKey, out var listed)
                     && listed is IEnumerable<(string, string)> pages
            ? pages.ToList()
            : [];
        hidden.Add((GuidePageCode, GuideTitleKey));
        api.ObjectCache[WoodworkingGuide.HiddenGuidesKey] = hidden;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (Applies(api) || api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is not { } handbook)
            return;
        _handbook = handbook;
        _hidePage = pages => pages.RemoveAll(p => p.PageCode == GuidePageCode);
        handbook.OnInitCustomPages += _hidePage;
    }

    public override void Dispose()
    {
        if (_bessemerHarmony != null)
        {
            _bessemerHarmony.UnpatchAll(BessemerStainless.HarmonyId);
            _bessemerHarmony = null;
            BessemerStainless.Unbind();
        }
        if (_handbook != null && _hidePage != null)
            _handbook.OnInitCustomPages -= _hidePage;
        _handbook = null;
        _hidePage = null;
    }

    /// <summary>Leaves the furnace out of the game: marks its types and recipes disabled before the
    /// game loads them.</summary>
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

    /// <summary>The collectible <paramref name="code"/> names, or null.</summary>
    public static CollectibleObject? Resolve(IWorldAccessor world, string code)
    {
        var location = new AssetLocation(code);
        return world.GetItem(location) is { IsMissing: false } item ? item
            : world.GetBlock(location) is { Id: > 0, IsMissing: false } block ? block
            : null;
    }

    private static PotRecipes LoadRecipes(ICoreAPI api)
    {
        var kept = new List<PotRecipe>();
        foreach (var recipe in PotRecipes.Default.Recipes)
        {
            bool Present(string c) => c.Contains('*') || Resolve(api.World, c) != null;
            // An ingredient is there if any of its codes is (chromite concentrate exists only with
            // OreProcessing on, crushed chromite always).
            var missing = recipe.Ingredients.Where(i => !i.Codes.Any(Present)).Select(i => string.Join(" or ", i.Codes))
                .Concat(new[] { recipe.Output }.Concat(recipe.Byproduct is { } b ? [b] : []).Where(c => !Present(c)))
                .ToList();
            if (missing.Count == 0)
                kept.Add(recipe);
            else
                api.Logger.Notification("[seraphhorizons] Crucible furnace: the {0} pot recipe is left out, {1} not in this game",
                    recipe.Code, string.Join(", ", missing));
        }
        return new PotRecipes(kept);
    }

    private static CrucibleFurnaceConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).CrucibleFurnaceSettings ?? new CrucibleFurnaceConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Crucible furnace: ModConfig/{SeraphHorizonsSystem.ConfigFile}, CrucibleFurnaceSettings: {fix}");
        return config;
    }
}
