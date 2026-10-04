using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The stations the unified system replaces, retired: nothing makes them any more, and they are
/// out of the creative inventory and the handbook. They stay registered, so a world that has one
/// and code that looks one up keep working.
///
/// - Logging Expanded's four splitting logs: the splitting block takes their place.
/// - Immersive Woodworking's sawhorse, built in the world by right-clicking a block's top with a
///   stick (the stick's <c>iw-sawhorsebuild</c> behavior, added by Immersive Woodworking's patch
///   <see cref="SawhorseBuildPatch"/>, which loses that operation here before the patch loader
///   runs). Logging Expanded's sawhorses are the only ones.
/// - Immersive Woodworking's pit saw and its blade, which only worked at its sawhorse (their
///   recipes go with <c>RemovePitSaw</c>, <see cref="WoodworkingSettings"/>).
/// - Immersive Woodworking's chopping block grid recipes, one per wood, and the one per domain the
///   handbook shows: a splitting block is made in the world. They are made in its mod system's
///   <c>RegisterChoppingBlockRecipes</c> (server AssetsFinalize), which a prefix skips.
/// </summary>
public sealed class RetiredStations : WoodworkingPart
{
    public const string RecipesMethod = "RegisterChoppingBlockRecipes";
    public const string SawhorseBuildBehavior = "iw-sawhorsebuild";
    public static readonly AssetLocation SawhorseBuildPatch = new(WoodworkingMods.IwModId, "patches/sawhorse_build_behavior.json");

    /// <summary>The block and item types hidden, each with the code it is expected to have.</summary>
    public static readonly (AssetLocation Asset, string Code)[] Hidden =
    [
        (new(WoodworkingMods.LeModId, "blocktypes/splittinglog.json"), "splittinglog"),
        (new(WoodworkingMods.LeModId, "blocktypes/debarkedsplittinglog.json"), "debarkedsplittinglog"),
        (new(WoodworkingMods.LeModId, "blocktypes/boundsplittinglog.json"), "boundsplittinglog"),
        (new(WoodworkingMods.LeModId, "blocktypes/advancedsplittinglog.json"), "advancedsplittinglog"),
        (new(WoodworkingMods.IwModId, "blocktypes/sawhorse.json"), "sawhorse"),
        (new(WoodworkingMods.IwModId, "itemtypes/tool/pitsaw.json"), "pitsaw"),
        (new(WoodworkingMods.IwModId, "itemtypes/toolhead/pitsawblade.json"), "pitsawblade"),
    ];

    private MethodInfo? _registerRecipes;
    // The sawhorse build patch as the mod ships it, for Undo.
    private byte[]? _shippedPatch;

    public override string Name => "the retired stations";

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        _registerRecipes = AccessTools.DeclaredMethod(mods.IwSystem.GetType(), RecipesMethod, [typeof(ICoreServerAPI)]);
        if (_registerRecipes?.ReturnType != typeof(void))
            return $"{WoodworkingMods.IwSystemType}.{RecipesMethod}(ICoreServerAPI) is missing";
        if (api.Side != EnumAppSide.Server)
            return null;
        foreach (var (asset, code) in Hidden)
            if (WoodworkingAssets.Read(api, asset) is not { } json || !WoodworkingAssets.HasCode(json, code))
                return $"{asset} is missing or not the {code} type";
        if (ReadSawhorseBuildPatch(api) is not { } ops || !ops.Any(IsSawhorseBuild))
            return $"{SawhorseBuildPatch} does not add {SawhorseBuildBehavior}";
        return null;
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        harmony.Patch(_registerRecipes, prefix: new HarmonyMethod(typeof(RetiredStations), nameof(SkipPrefix)));
        // Before the game's patch loader, which applies the patches in AssetsLoaded.
        var ops = ReadSawhorseBuildPatch(api)!;
        foreach (var op in ops.Where(IsSawhorseBuild).ToList())
            op.Remove();
        var asset = api.Assets.TryGet(SawhorseBuildPatch)!;
        _shippedPatch = asset.Data;
        asset.Data = Encoding.UTF8.GetBytes(ops.ToString());
    }

    public override void Undo(ICoreAPI api)
    {
        if (_shippedPatch != null && api.Assets.TryGet(SawhorseBuildPatch) is { } asset)
            asset.Data = _shippedPatch;
        _shippedPatch = null;
    }

    public override void EditAssets(ICoreServerAPI api)
    {
        foreach (var (asset, code) in Hidden)
            WoodworkingAssets.Edit(api, asset, json => WoodworkingAssets.HasCode(json, code) && WoodworkingAssets.Hide(json),
                $"the {code} type's place in the creative inventory and handbook",
                "it shows there, but nothing makes it");
    }

    public override void Dispose()
    {
        _registerRecipes = null;
        _shippedPatch = null;
    }

    /// <summary>Skips the method: nothing is registered.</summary>
    public static bool SkipPrefix() => false;

    private static JArray? ReadSawhorseBuildPatch(ICoreAPI api)
    {
        try
        {
            return api.Assets.TryGet(SawhorseBuildPatch) is { } asset ? JToken.Parse(asset.ToText()) as JArray : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsSawhorseBuild(JToken op) =>
        op is JObject && op.SelectToken("value.name") is JValue { Type: JTokenType.String } name
                      && (string)name! == SawhorseBuildBehavior;
}
