using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Ore processing's items, crushing and smelting (#686, #687, #688; switch <c>OreProcessing</c>,
/// default off; README "Ore processing: items, crushing and smelting"). With the switch off the
/// server leaves every item, patch and change of it out, and the game is as before.
///
/// With it on, on the server:
/// <list type="bullet">
/// <item><see cref="Start"/>: the new item types and <c>patches/oreprocessing-ore.json</c> (vanilla's
/// ore item's class, <see cref="ItemGradedOre"/>) stay in.</item>
/// <item><see cref="AssetsLoaded"/>, after the patch loader and before the type and recipe loaders:
/// vanilla's grid recipes hammering ore into nuggets (<c>recipes/grid/nuggets.json</c>) are switched
/// off, and every recipe that takes a crushed ore the crusher no longer makes (vanilla's
/// <c>game:crushed-chromite</c>, Expanded Matter's <c>em:crushed-ore-*</c>, ...) takes the new crushed
/// ore of that ore, either grain, instead (<see cref="LegacyCrushed"/>).</item>
/// <item><see cref="AssetsFinalize"/>, with every item loaded and patched (so it holds whatever order
/// Expanded Matter's and smex's crushing patches ran in): stack sizes, crushing and smelting set on
/// the items (<see cref="OreProcessingItems"/>), and smex's blast furnace burden counted in 5-unit
/// items (<see cref="SmexBurden"/>). The server sends the items to clients as they are then.</item>
/// </list>
/// On each side whose own setting is on, a transpiler makes the game's alloy maths count a smelted
/// stack's size (<see cref="AlloyStackSize"/>), as its single-metal maths already does, so a chunk's
/// exact half (7 ingots per 40 chunks) holds in an alloy too.
/// </summary>
public class OreProcessingSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string HarmonyId = "seraphhorizons.oreprocessing";

    public static readonly AssetLocation OrePatch = new(Domain, "patches/oreprocessing-ore.json");
    public static readonly AssetLocation RecoveryAsset = new(Domain, "config/ore-processing.json");
    public static readonly AssetLocation NuggetRecipes = new("game", "recipes/grid/nuggets.json");

    /// <summary>The item types ore processing adds; marked disabled with the switch off.</summary>
    public static readonly AssetLocation[] TypeAssets =
    [
        new("game", "itemtypes/resource/crushedore.json"),
        new(Domain, "itemtypes/oreprocessing/groundore.json"),
        new(Domain, "itemtypes/oreprocessing/concentrate.json"),
        new(Domain, "itemtypes/oreprocessing/roastedconcentrate.json"),
        new(Domain, "itemtypes/oreprocessing/amalgam.json"),
        new(Domain, "itemtypes/oreprocessing/litharge.json"),
    ];

    /// <summary>
    /// The crushed items the crusher made before, which nothing makes with ore processing on, and the
    /// ore whose crushed ore a recipe takes instead (any grain: <c>game:crushed-{ore}-*</c>).
    /// Vanilla's crushed iron has no recipe taking it (smex's blast furnace counts our crushed iron
    /// ore itself, <see cref="SmexBurden"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyCrushed = new Dictionary<string, string>
    {
        ["game:crushed-cassiterite"] = "cassiterite",
        ["game:crushed-chromite"] = "chromite",
        ["game:crushed-galena"] = "galena",
        ["game:crushed-ilmenite"] = "ilmenite",
        ["game:crushed-sphalerite"] = "sphalerite",
        ["em:crushed-ore-hematite"] = "hematite",
        ["em:crushed-ore-limonite"] = "limonite",
        ["em:crushed-ore-magnetite"] = "magnetite",
        ["em:crushed-ore-malachite"] = "malachite",
        ["em:crushed-ore-pentlandite"] = "pentlandite",
        ["em:crushed-ore-rhodochrosite"] = "rhodochrosite",
        ["em:crushed-metal-copper"] = "nativecopper",
        ["em:crushed-metal-gold"] = "quartz_nativegold",
        ["em:crushed-metal-silver"] = "quartz_nativesilver",
        ["em:crushed-metal-uranium"] = "uranium",
    };

    // The files open with a comment, and the game's recipes use its lenient JSON.
    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    private Harmony? _harmony;

    /// <summary>The recovery figures (<c>config/ore-processing.json</c>), read on the server with the
    /// switch on; null otherwise.</summary>
    public OreRecovery? Recovery { get; private set; }

    /// <summary>What <see cref="AssetsFinalize"/> set, for the log and the Atlas scenarios; null until
    /// then, or with the switch off.</summary>
    public OreProcessingItems.Report? Applied { get; private set; }

    /// <summary>The recipe files <see cref="AssetsLoaded"/> retargeted, and the nugget recipes it
    /// switched off.</summary>
    public IReadOnlyList<string> RetargetedRecipes { get; private set; } = [];
    public int NuggetRecipesOff { get; private set; }

    /// <summary>What happened to smex's burden; null without smex or with the switch off.</summary>
    public SmexBurden.Status? Smex { get; private set; }

    public static OreProcessingSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<OreProcessingSystem>();

    /// <summary>Whether ore processing is on, by this side's setting.</summary>
    public static bool On(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).OreProcessing;

    // The classes on both sides whatever the setting: the server decides whether the items exist and
    // which class vanilla's ore takes, and a client builds them from the server's.
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("seraphhorizons.ItemGradedOre", typeof(ItemGradedOre));
        api.RegisterItemClass("seraphhorizons.ItemOreProduct", typeof(ItemOreProduct));
        if (!On(api))
        {
            // Before the patch loader (AssetsLoaded). On the server only: a client has no assets
            // in Start, and gets the items from the server.
            if (api.Side == EnumAppSide.Server)
                Disable(api);
            return;
        }
        // Once per process: in singleplayer the other side may have patched already.
        if (!Harmony.HasAnyPatches(HarmonyId))
        {
            _harmony = new Harmony(HarmonyId);
            if (!AlloyStackSize.Patch(_harmony))
                api.Logger.Warning("[seraphhorizons] Ore processing: the game's AlloyRecipe.mergeAndCompareStacks is not as expected; "
                                   + "an alloy counts a chunk at a whole number of chunks per ingot, which can be off its exact half");
        }
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !On(api))
            return;
        Recovery = LoadRecovery(api);
        NuggetRecipesOff = DisableNuggetRecipes(api);
        RetargetedRecipes = RetargetRecipes(api);
        api.Logger.Notification("[seraphhorizons] Ore processing: {0} grid recipes hammering ore into nuggets off; "
                                + "{1} recipe files take the new crushed ore in place of the old ({2})",
            NuggetRecipesOff, RetargetedRecipes.Count, string.Join(", ", RetargetedRecipes));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !On(api))
            return;
        Recovery ??= LoadRecovery(api);
        Applied = OreProcessingItems.Apply(api.World, Recovery, api.Logger);
        if (api.ModLoader.IsModEnabled(SmexBurden.ModId))
        {
            Smex = SmexBurden.Apply(api.World, api.Logger);
            if (Smex == SmexBurden.Status.Applied)
                SmexBurden.Patch(_harmony ??= new Harmony(HarmonyId + ".smex"), api.Logger);
        }
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(_harmony.Id);
        _harmony = null;
    }

    /// <summary>Leaves ore processing out of the game: its item types marked disabled and its patch
    /// emptied before the game loads them.</summary>
    public static void Disable(ICoreAPI api)
    {
        if (api.Assets.TryGet(OrePatch) is { } patch)
            patch.Data = "[]"u8.ToArray();
        foreach (var location in TypeAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), Lenient);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    /// <summary>The recovery figures, or the defaults' empty figures if the file is missing or broken
    /// (logged; every share then reads 0, so nothing smelts by the rule and it shows).</summary>
    public static OreRecovery LoadRecovery(ICoreAPI api)
    {
        OreProcessingConfig? config = null;
        try
        {
            config = api.Assets.TryGet(RecoveryAsset)?.ToObject<OreProcessingConfig>();
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Ore processing: could not read {0}: {1}", RecoveryAsset, e.Message);
        }
        if (config == null)
            api.Logger.Error("[seraphhorizons] Ore processing: no {0}; every form reads as not smelting", RecoveryAsset);
        var recovery = new OreRecovery(config ?? new OreProcessingConfig());
        foreach (var problem in recovery.Problems)
            api.Logger.Warning("[seraphhorizons] Ore processing: {0}: {1}", RecoveryAsset, problem);
        return recovery;
    }

    /// <summary>Switches off vanilla's grid recipes that hammer ore and crystallised ore into nuggets
    /// (#663, #687); returns how many.</summary>
    public static int DisableNuggetRecipes(ICoreAPI api)
    {
        if (api.Assets.TryGet(NuggetRecipes) is not { } asset)
        {
            api.Logger.Warning("[seraphhorizons] Ore processing: no {0}; the game changed, so nothing is switched off there", NuggetRecipes);
            return 0;
        }
        var token = JToken.Parse(asset.ToText(), Lenient);
        var recipes = token is JArray array ? array.OfType<JObject>().ToList() : token is JObject one ? [one] : [];
        foreach (var recipe in recipes)
            recipe["enabled"] = false;
        asset.Data = Encoding.UTF8.GetBytes(token.ToString());
        return recipes.Count;
    }

    /// <summary>Every recipe file whose ingredients name a crushed item of <see cref="LegacyCrushed"/>
    /// takes <c>game:crushed-{ore}-*</c> there instead; returns the files changed. Outputs are left
    /// alone.</summary>
    public static List<string> RetargetRecipes(ICoreAPI api)
    {
        var changed = new List<string>();
        foreach (var asset in api.Assets.GetMany("recipes/", null, loadAsset: true))
        {
            if (!asset.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            string text = asset.ToText();
            if (!text.Contains("crushed-", StringComparison.Ordinal))
                continue;
            JToken token;
            try
            {
                token = JToken.Parse(text, Lenient);
            }
            catch (Exception)
            {
                continue;
            }
            if (Retarget(token, asset.Location.Domain) == 0)
                continue;
            asset.Data = Encoding.UTF8.GetBytes(token.ToString());
            changed.Add(asset.Location.ToString());
        }
        return changed;
    }

    /// <summary>Rewrites, in place, every ingredient code naming a legacy crushed item; returns how
    /// many. A code without a domain is in <paramref name="domain"/>, the file's.</summary>
    public static int Retarget(JToken token, string domain)
    {
        int count = 0;
        foreach (var value in token.SelectTokens("$..code").OfType<JValue>().ToList())
        {
            if (value.Type != JTokenType.String || value.Path is not { } path)
                continue;
            if (!path.Contains("ngredient", StringComparison.OrdinalIgnoreCase) && !path.Contains("validStacks", StringComparison.OrdinalIgnoreCase))
                continue;
            string code = (string)value!;
            string full = code.Contains(':') ? code : domain + ":" + code;
            if (!LegacyCrushed.TryGetValue(full, out var ore))
                continue;
            value.Value = $"game:crushed-{ore}-*";
            count++;
        }
        return count;
    }

    /// <summary>Whether the code names an ore processing form of an iron ore, for smex.</summary>
    internal static bool IsIron(string ore) => OreMetals.MetalOf(ore) == "iron";
}
