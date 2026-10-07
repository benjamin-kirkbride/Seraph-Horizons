using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Pipes;

/// <summary>
/// Cast pipes (<c>CastPipes</c>, README "Cast pipes"), the ferrous stage of the pipe ladder:
/// Steelmaking Expanded's tool mold gets a fourth tool type, <c>pipe</c>
/// (<c>smex:toolmold-{color}-{raw|fired}-pipe</c>), by a JSON patch on its two mold blocktypes
/// (<c>patches/castpipes-smexmold.json</c>): the state, a shape, and the fired mold's figures, so
/// it is smex's own <c>BlockToolMold</c> and its canal pedestal, a crucible and smex's mold patches
/// take it as any other. One fill is one ingot (<see cref="CastPipeMold.RequiredUnits"/>) and
/// casts two of the game's chute sections, <c>game:chutesection-{iron|steel}</c>, which the grid
/// bands with nails and strips of their metal into ppex pipe (<c>UnifiedPipes</c>' recipes). Those
/// states are <c>UnifiedPipes</c>' (<see cref="UnifiedPipesSystem.ChutePatchAsset"/>), so this runs
/// only while that is on, and after it (<see cref="ExecuteOrder"/>). The raw mold is clay-formed.
///
/// The patch assumes the shape of smex's two files (<see cref="CastPipeMold.CheckFired"/>,
/// <see cref="CastPipeMold.CheckRaw"/>), checked in <see cref="Start"/> on the server before the
/// patch loader runs. With the switch off, without smex or ppex, with <c>UnifiedPipes</c> not on, or
/// with either file not as expected (one warning), the patch file is emptied there, and the mold's
/// clay-forming recipe is marked disabled in <see cref="AssetsLoaded"/>, before the game loads it:
/// there is no pipe mold, and those already in a world are lost.
/// </summary>
public class CastPipesSystem : ModSystem
{
    public const string Domain = CastPipeMold.Domain;
    public const string SmexId = CastPipeMold.SmexId;
    public const string PpexId = "ppex";

    public static readonly AssetLocation PatchAsset = new(Domain, "patches/castpipes-smexmold.json");
    public static readonly AssetLocation FiredMoldAsset = new(SmexId, "blocktypes/molds/toolmoldfired.json");
    public static readonly AssetLocation RawMoldAsset = new(SmexId, "blocktypes/molds/toolmoldraw.json");

    /// <summary>None: the mold is smex's, patched, and what it casts is the game's chute section.</summary>
    public static readonly AssetLocation[] TypeAssets = [];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/clayforming/pipemold.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private bool _on;

    /// <summary>Whether the pipe mold is in the game on this server; decided in <see cref="Start"/>.
    /// False on a client, which gets it from the server.</summary>
    public bool On => _on;

    // After UnifiedPipesSystem (the default 0.1), whose Start decides whether the chute sections the
    // mold casts exist; before the game's type and recipe loaders (0.2), which Disable must precede.
    public override double ExecuteOrder() => 0.11;

    /// <summary>The switch on, and both mods it adds to installed.</summary>
    public static bool Applies(ICoreAPI api) =>
        SeraphHorizonsSystem.ConfigFor(api).CastPipes
        && api.ModLoader.IsModEnabled(SmexId) && api.ModLoader.IsModEnabled(PpexId);

    // The server has its assets in Start and the patch loader has not run (it applies the patches
    // in AssetsLoaded). A client has no assets yet, and gets the blocks and items from the server.
    public override void Start(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        _on = Applies(api) && SectionsExist(api) && Bind(api);
        if (!_on)
            DisablePatches(api);
    }

    /// <summary>The iron and steel chute sections the mold casts exist: UnifiedPipes is on here.</summary>
    private static bool SectionsExist(ICoreAPI api)
    {
        if (api.ModLoader.GetModSystem<UnifiedPipesSystem>()?.On == true)
            return true;
        api.Logger.Notification("[seraphhorizons] Cast pipes: off, because Unified pipes is (the mold casts the iron and "
                                + "steel chute sections it adds)");
        return false;
    }

    // Types and recipes are read from the assets later in this phase (the game's loaders run at
    // 0.2 and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        if (_on)
            NameMoldsForSmex(api);
        else
            Disable(api);
    }

    /// <summary>Copies this mod's names for the pipe mold (the <c>smex:</c> keys of its lang
    /// files) into smex's own lang assets, so Expanded Lib's lang coverage check, which reads a
    /// block's own domain's files, finds them (<see cref="ForeignLangKeys"/>). The game itself
    /// reads them from this mod's file either way.</summary>
    public static void NameMoldsForSmex(ICoreAPI api)
    {
        foreach (var own in api.Assets.GetMany("lang/", Domain, true))
        {
            var language = Path.GetFileName(own.Location.Path);
            if (api.Assets.TryGet(new AssetLocation(SmexId, "lang/" + language)) is not { } theirs)
                continue;
            try
            {
                var merged = ForeignLangKeys.Merge(theirs.ToText(), ForeignLangKeys.For(own.ToText(), SmexId));
                if (merged != null)
                    theirs.Data = Encoding.UTF8.GetBytes(merged);
            }
            catch (Exception e)
            {
                api.Logger.Warning($"[seraphhorizons] Cast pipes: could not add the pipe mold's names to {theirs.Location}: {e.Message}");
            }
        }
    }

    /// <summary>Checks smex's two mold files against what the patch assumes. False, with one
    /// warning, when either is missing or not as expected.</summary>
    public static bool Bind(ICoreAPI api)
    {
        string? problem;
        try
        {
            problem = api.Assets.TryGet(FiredMoldAsset) is not { } fired ? $"{FiredMoldAsset} (missing)"
                : api.Assets.TryGet(RawMoldAsset) is not { } raw ? $"{RawMoldAsset} (missing)"
                : CastPipeMold.CheckFired(fired.ToText()) ?? CastPipeMold.CheckRaw(raw.ToText());
        }
        catch (Exception e)
        {
            problem = e.Message;
        }
        if (problem == null)
            return true;
        api.Logger.Warning($"[seraphhorizons] Cast pipes: Steelmaking Expanded changed {problem}, so there is no pipe mold");
        return false;
    }

    /// <summary>Empties the patch file, so the patch loader applies none of it.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }

    /// <summary>Leaves the mold's recipe out of the game: marks it disabled before the game loads it.</summary>
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
