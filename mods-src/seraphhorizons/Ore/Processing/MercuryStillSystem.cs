using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Retorting mercury and amalgam in the game's still (#726; switch <c>OreProcessing</c>; README "Ore
/// processing: retorting mercury in the still"). With the switch on, on the server:
/// <list type="bullet">
/// <item><see cref="AssetsLoaded"/>: Expanded Matter's cooking pot recipe for mercury
/// (<c>em:recipes/cooking/mercury.json</c>) is switched off, so the still is the only way to it;</item>
/// <item><see cref="AssetsFinalize"/>: cinnabar and the amalgams are marked as the still's inputs
/// (<see cref="MercuryStill.Mark"/>) before the items go to clients, and the still's patches go in.</item>
/// </list>
/// A client patches once it has the server's items, if any carries the mark, so it follows the
/// server's switch. The guide page "Retorting mercury" (<c>config/handbook/oreretorting.json</c>) is
/// hidden with the switch off.
/// </summary>
public class MercuryStillSystem : ModSystem
{
    public static readonly AssetLocation MercuryRecipes = new("em", "recipes/cooking/mercury.json");

    /// <summary>The guide page (<c>config/handbook/oreretorting.json</c>).</summary>
    public const string GuidePage = "seraphhorizons-oreretorting";
    public const string GuideTitleKey = "seraphhorizons:oreretorting-title";

    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    private Harmony? _harmony;
    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _hidePage;

    /// <summary>The codes marked as the still's inputs (on the server, with the switch on).</summary>
    public IReadOnlyList<string> Marked { get; private set; } = [];

    /// <summary>Expanded Matter's mercury recipes switched off.</summary>
    public int MercuryRecipesOff { get; private set; }

    public static MercuryStillSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<MercuryStillSystem>();

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (OreProcessingSystem.On(api))
            return;
        // The recipe export leaves the page out too.
        var hidden = api.ObjectCache.TryGetValue(WoodworkingGuide.HiddenGuidesKey, out var listed)
                     && listed is IEnumerable<(string, string)> pages
            ? pages.ToList()
            : [];
        hidden.Add((GuidePage, GuideTitleKey));
        api.ObjectCache[WoodworkingGuide.HiddenGuidesKey] = hidden;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.LevelFinalize += () =>
        {
            if (MercuryStill.AnyMarked(api.World))
                Patch();
        };
        if (api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is not { } handbook)
            return;
        _handbook = handbook;
        _hidePage = pages =>
        {
            if (!MercuryStill.AnyMarked(api.World))
                pages.RemoveAll(p => p.PageCode == GuidePage);
        };
        handbook.OnInitCustomPages += _hidePage;
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !OreProcessingSystem.On(api))
            return;
        MercuryRecipesOff = DisableMercuryRecipes(api);
        if (MercuryRecipesOff > 0)
            api.Logger.Notification("[seraphhorizons] Ore processing: {0} of Expanded Matter's cooking recipes for mercury off; the still makes it", MercuryRecipesOff);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !OreProcessingSystem.On(api))
            return;
        var recovery = OreProcessingSystem.Of(api).Recovery ?? OreProcessingSystem.LoadRecovery(api);
        Marked = MercuryStill.Mark(api.World, recovery, api.Logger);
        api.Logger.Notification("[seraphhorizons] Ore processing: the still retorts {0}", Marked.Count == 0 ? "nothing" : string.Join(", ", Marked));
        if (Marked.Count > 0)
            Patch();
    }

    // Once per process: in singleplayer the other side may have patched already.
    private void Patch()
    {
        if (Harmony.HasAnyPatches(MercuryStill.HarmonyId))
            return;
        _harmony = new Harmony(MercuryStill.HarmonyId);
        MercuryStill.Patch(_harmony);
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(_harmony.Id);
        _harmony = null;
        if (_handbook != null && _hidePage != null)
            _handbook.OnInitCustomPages -= _hidePage;
        _handbook = null;
        _hidePage = null;
    }

    /// <summary>Switches off Expanded Matter's recipes cooking cinnabar into mercury (none without
    /// Expanded Matter); returns how many.</summary>
    public static int DisableMercuryRecipes(ICoreAPI api)
    {
        if (api.Assets.TryGet(MercuryRecipes) is not { } asset)
            return 0;
        var token = JToken.Parse(asset.ToText(), Lenient);
        var recipes = token is JArray array ? array.OfType<JObject>().ToList() : token is JObject one ? [one] : [];
        foreach (var recipe in recipes)
            recipe["enabled"] = false;
        asset.Data = Encoding.UTF8.GetBytes(token.ToString());
        return recipes.Count;
    }
}
