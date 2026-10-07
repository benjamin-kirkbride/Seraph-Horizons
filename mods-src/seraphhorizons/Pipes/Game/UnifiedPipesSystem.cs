using System.Collections;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Pipes;

/// <summary>
/// Unified pipes (README "Unified pipes"), the Pipes and Power Expanded side: one pipe network for
/// water, steam and exhaust. ppex's straight, bend, T- and X-junction pipes get copper and lead as
/// material states, and its valves and pressure valves the three bronzes, by JSON patch
/// (<see cref="PatchAsset"/>), which also switches off ppex's plate-and-nails pipe recipes and its
/// iron and steel valve recipes. The chain (<see cref="PipeSections"/>): this mod's angle (copper or
/// lead, forged from an ingot or folded on the press brake); the game's chute section, the hollow
/// section, which gets a lead state by a second JSON patch (<see cref="ChutePatchAsset"/>, checked by
/// <see cref="ChuteSections"/>) that also switches off the game's anvil and plate recipes for it, so
/// two angles soldered on the grid are the only way to one; this mod's pipe section (every metal,
/// made by the mandrel station, the draw bench or the pipe mold); every pipe shape from pipe sections
/// in the game's chute patterns with a joint (solder for copper and lead, nails and strips for iron
/// and steel); and bronze valves.
///
/// By name, with Harmony (own id, patched once per process: both sides read a pipe's figure):
/// <list type="bullet">
/// <item>a postfix on ppex's <c>BlockPipe.BurstPressure</c> gives each metal its figure from
/// <see cref="UnifiedPipesConfig"/> (ppex knows only iron and steel; anything else is iron to it);</item>
/// <item>a postfix on exlib's <c>PipeNetwork.OnTick</c> bursts, with ppex's own effects, one lead
/// pipe a second of any run holding steam or exhaust (<see cref="PipeRules.LeadBursts"/>);</item>
/// <item>a postfix on exlib's private <c>ExRecipeCosts.GridRecipesFor</c> keeps ppex's recipe cost
/// levels to ppex's own recipes, so this mod's pipe recipes keep their quantities.</item>
/// </list>
/// ppex's steam power page says the four figures and that lead is for water only.
///
/// Off, without ppex, or with ppex's assets (<see cref="PipeAssetGuard"/>), the game's chute section
/// and recipes (<see cref="ChuteSections"/>) or a member the first two patches need not as expected
/// (one warning): the patch files are emptied in <c>Start</c>, before the game's patch loader runs,
/// this mod's angle, pipe section and recipes are marked disabled, nothing is patched, and copper
/// and lead pipes, bronze valves, angles, pipe sections and lead chute sections do not exist (those
/// already placed or held are lost).
/// </summary>
public class UnifiedPipesSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string HarmonyId = "seraphhorizons.unifiedpipes";
    public const string PpexId = PipeRules.PpexDomain;

    public const string PpexBlockPipeType = "PipesAndPowerExpanded.BlockNetworkPipe.Blocks.BlockPipe";
    public const string PipeNetworkType = "ExpandedLib.Industry.Pipes.PipeNetwork";
    public const string NetworkManagerType = "ExpandedLib.Networks.BlockNetworkModSystem";
    public const string RecipeCostsType = "ExpandedLib.Registries.ExRecipeCosts";

    public static readonly AssetLocation PatchAsset = new(Domain, "patches/unifiedpipes-ppex.json");

    /// <summary>The game's chute section in lead, its anvil and plate recipes off, chutes soldered.</summary>
    public static readonly AssetLocation ChutePatchAsset = new(Domain, "patches/unifiedpipes-chutesection.json");

    /// <summary>Better Ruins' five solderless bulk chute recipes off (<see cref="ChuteSections.BetterRuinsChutes"/>).</summary>
    public static readonly AssetLocation BetterRuinsPatchAsset = new(Domain, "patches/unifiedpipes-betterruins.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "itemtypes/angle.json"),
        new(Domain, "itemtypes/pipesection.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/unifiedpipes.json"),
        new(Domain, "recipes/grid/chutesection.json"),
        new(Domain, "recipes/smithing/angle.json"),
    ];

    // The type and recipe files open with a comment.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private static UnifiedPipesConfig _settings = new();
    private static Type? _ppexPipe;
    private static MethodInfo? _burstGetter, _onTick, _executeBurst, _gridRecipesFor;
    private static PropertyInfo? _state, _nodes, _isLiquid, _volume, _medium;
    private static bool _leadBroken;
    private static ILogger? _logger;

    private bool _on;
    private Harmony? _harmony;
    // Hydrate or Diedrate's pipes and its hand pump on ppex's (HydratePipes, HandPumpBridge).
    private IDisposable? _hydratePipes;

    /// <summary>Whether the unified pipes are in the game on this side; decided in <see cref="Start"/>.</summary>
    public bool On => _on;

    /// <summary>The figures in use (sanitised).</summary>
    public static UnifiedPipesConfig Settings => _settings;

    /// <summary>The switch is on and ppex is installed.</summary>
    public static bool Applies(ICoreAPI api) =>
        SeraphHorizonsSystem.ConfigFor(api).UnifiedPipes && api.ModLoader.IsModEnabled(PpexId);

    public override void Start(ICoreAPI api)
    {
        _settings = SeraphHorizonsSystem.ConfigFor(api).UnifiedPipesSettings ?? new UnifiedPipesConfig();
        foreach (var fix in _settings.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Unified pipes: ModConfig/{SeraphHorizonsSystem.ConfigFile}, UnifiedPipesSettings: {fix}");
        _on = Applies(api) && Bind(api);
        // Before the game's patch loader and type and recipe loaders, on the server only: a client
        // has no assets in Start, and gets the blocks, items and recipes from the server.
        if (!_on && api.Side == EnumAppSide.Server)
        {
            DisablePatches(api);
            Disable(api);
        }
        if (_on && api.Side == EnumAppSide.Server)
            GuardBetterRuins(api);
        if (_on && !Harmony.HasAnyPatches(HarmonyId))
            Patch(_harmony = new Harmony(HarmonyId), api.Logger);
        _hydratePipes = HydratePipes.Start(api, _on);
    }

    // Lang files are loaded, mod assets included, before this phase on both sides.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (_on)
            LangText.Apply(LangEdits(_settings), PpexId, api.Logger);
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        _hydratePipes?.Dispose();
        _hydratePipes = null;
    }

    /// <summary>ppex's text that names the pipes' figures and the valves, reworded. The figures are
    /// this side's settings.</summary>
    public static LangEdit[] LangEdits(UnifiedPipesConfig s) =>
    [
        new("en", "ppex:handbook-steampower-text",
            "an <strong>iron</strong> pipe bursts above <strong>5 atm</strong> while a stronger <strong>steel</strong> pipe "
            + "holds up to <strong>10 atm</strong>, and because the weakest pipe sets the limit for the whole run, a single "
            + "iron section caps an otherwise steel line.",
            $"<strong>lead</strong> pipe holds up to <strong>{UnifiedPipesConfig.Format(s.LeadBurstPressure)} atm</strong>, "
            + $"<strong>copper</strong> <strong>{UnifiedPipesConfig.Format(s.CopperBurstPressure)} atm</strong>, "
            + $"<strong>iron</strong> <strong>{UnifiedPipesConfig.Format(s.IronBurstPressure)} atm</strong> and "
            + $"<strong>steel</strong> <strong>{UnifiedPipesConfig.Format(s.SteelBurstPressure)} atm</strong>, and because the "
            + "weakest pipe sets the limit for the whole run, a single iron section caps an otherwise steel line. "
            + "<strong>Lead pipe is for water only</strong>: steam or exhaust in a run bursts its lead sections at once, "
            + "whatever the pressure, while water and air are safe in it. Copper carries exhaust and blast air but fails "
            + "on a stressed boiler. Pipe is made from pipe sections: copper and lead ones soldered, a solder bar a "
            + "section and a soldering iron, iron and steel ones banded with one nails and strips and a hammer; one "
            + "section makes a straight pipe, and two, three or four a bend, T- or X-junction. Copper and lead pipe "
            + "sections come from a chute section, two soldered angles, worked on the mandrel station or drawn on the "
            + "draw bench; iron and steel ones are cast."),
        new("en", "ppex:handbook-fittings-text",
            "A <strong>Valve</strong> is a hand-operated shut-off in a line.",
            "A <strong>Valve</strong> is a hand-operated shut-off in a line. Valves and pressure valves are bronze (tin, "
            + "bismuth or black bronze), made from a straight pipe of any metal, and join pipes of every metal."),
        new("en", "ppex:handbook-fittings-text",
            "then lets the excess spill through to its output side;",
            "then lets the excess spill through to its output side (the gate goes up to "
            + $"{UnifiedPipesConfig.Format(s.BronzeBurstPressure)} atm);"),
    ];

    /// <summary>Better Ruins' blueprint recipes, checked against <see cref="BetterRuinsPatchAsset"/>
    /// on the server before the patch loader runs: if Better Ruins has changed them, one warning and
    /// that patch alone is emptied (its solderless chutes stay; the rest goes ahead). Without Better
    /// Ruins its <c>dependsOn</c> leaves the patch unapplied.</summary>
    private static void GuardBetterRuins(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(ChuteSections.BetterRuinsId))
            return;
        var location = new AssetLocation(ChuteSections.BetterRuinsId, ChuteSections.BetterRuinsFile);
        string? problem;
        try
        {
            problem = api.Assets.TryGet(location) is { } asset ? ChuteSections.CheckBetterRuins(asset.ToText()) : $"{location} (missing)";
        }
        catch (Exception e)
        {
            problem = e.Message;
        }
        if (problem == null)
            return;
        api.Logger.Warning($"[seraphhorizons] Unified pipes: Better Ruins changed {problem}, so its blueprint's chute "
                           + "recipes are left as they ship (no solder)");
        if (api.Assets.TryGet(BetterRuinsPatchAsset) is { } patch)
            patch.Data = "[]"u8.ToArray();
    }

    /// <summary>Empties the patch files, so the patch loader applies none of them.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        foreach (var location in new[] { PatchAsset, ChutePatchAsset, BetterRuinsPatchAsset })
            if (api.Assets.TryGet(location) is { } asset)
                asset.Data = "[]"u8.ToArray();
    }

    /// <summary>Marks this mod's angle, pipe section and recipes disabled before the game loads them.</summary>
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

    /// <summary>Checks ppex's and the game's assets against the patches (server: a client has no
    /// assets yet) and finds every member the patches need. False, with one warning, when anything
    /// is not as expected.</summary>
    public static bool Bind(ICoreAPI api)
    {
        string? problem;
        try
        {
            problem = (api.Side == EnumAppSide.Server ? CheckChuteAssets(api) ?? CheckAssets(api) : null) ?? BindMembers();
            if (problem != null && !problem.StartsWith(GameChanged, StringComparison.Ordinal))
                problem = "Pipes and Power Expanded changed " + problem;
        }
        catch (Exception e)
        {
            problem = "checking for them failed: " + e.Message;
        }
        if (problem == null)
            return true;
        api.Logger.Warning($"[seraphhorizons] Unified pipes: {problem}, so ppex's pipes and the game's chute section "
                           + "are left as they ship (no copper, lead or bronze pipe)");
        return false;
    }

    private const string GameChanged = "the game changed ";

    /// <summary>The game's chute section, its anvil recipe and the chute recipes, as
    /// <see cref="ChutePatchAsset"/> assumes.</summary>
    private static string? CheckChuteAssets(ICoreAPI api)
    {
        var item = new AssetLocation("game", ChuteSections.ItemFile);
        var recipes = new AssetLocation("game", ChuteSections.RecipeFile);
        var smithing = new AssetLocation("game", ChuteSections.SmithingFile);
        var problem = api.Assets.TryGet(item) is not { } itemAsset ? $"{item} (missing)"
            : api.Assets.TryGet(recipes) is not { } recipeAsset ? $"{recipes} (missing)"
            : api.Assets.TryGet(smithing) is not { } smithingAsset ? $"{smithing} (missing)"
            : ChuteSections.CheckItem(itemAsset.ToText()) ?? ChuteSections.CheckRecipes(recipeAsset.ToText())
              ?? ChuteSections.CheckSmithing(smithingAsset.ToText());
        return problem == null ? null : GameChanged + problem;
    }

    private static string? CheckAssets(ICoreAPI api)
    {
        foreach (var (files, added) in new[]
                 {
                     (PipeAssetGuard.PipeBlocktypes, PipeRules.AddedPipeMaterials),
                     (PipeAssetGuard.ValveBlocktypes, PipeRules.Bronzes),
                 })
        {
            foreach (var file in files)
            {
                var location = new AssetLocation(PpexId, file);
                if (api.Assets.TryGet(location) is not { } asset)
                    return $"{location} (missing)";
                if (PipeAssetGuard.CheckBlocktype(location.ToString(), asset.ToText(), added) is { } problem)
                    return problem;
            }
        }
        var recipes = new AssetLocation(PpexId, PipeAssetGuard.RecipeFile);
        return api.Assets.TryGet(recipes) is { } recipeAsset
            ? PipeAssetGuard.CheckRecipes(recipeAsset.ToText())
            : $"{recipes} (missing)";
    }

    private static string? BindMembers()
    {
        _ppexPipe = AccessTools.TypeByName(PpexBlockPipeType);
        _burstGetter = _ppexPipe == null ? null : AccessTools.DeclaredPropertyGetter(_ppexPipe, "BurstPressure");
        if (_ppexPipe == null || !typeof(Block).IsAssignableFrom(_ppexPipe) || _burstGetter?.ReturnType != typeof(float))
            return $"{PpexBlockPipeType}.BurstPressure";
        var network = AccessTools.TypeByName(PipeNetworkType);
        var manager = AccessTools.TypeByName(NetworkManagerType);
        if (network == null || manager == null)
            return $"{PipeNetworkType} or {NetworkManagerType} (exlib)";
        _onTick = AccessTools.DeclaredMethod(network, "OnTick", [typeof(IBlockAccessor), typeof(float), manager]);
        _executeBurst = AccessTools.DeclaredMethod(network, "ExecuteBurst", [typeof(BlockPos), typeof(IBlockAccessor), manager]);
        // PipeNetwork's own State hides the base class's object State (AccessTools.Property would be ambiguous).
        _state = AccessTools.DeclaredProperty(network, "State");
        _nodes = AccessTools.Property(network, "Nodes");
        var stateType = _state?.PropertyType;
        _isLiquid = stateType == null ? null : AccessTools.Property(stateType, "IsLiquid");
        _volume = stateType == null ? null : AccessTools.Property(stateType, "Volume");
        _medium = stateType == null ? null : AccessTools.Property(stateType, "MediumType");
        if (_onTick == null || _executeBurst is not { IsStatic: true } || _state == null
            || _nodes == null || !typeof(IEnumerable).IsAssignableFrom(_nodes.PropertyType)
            || _isLiquid?.PropertyType != typeof(bool) || _volume?.PropertyType != typeof(float) || _medium?.PropertyType != typeof(string))
            return $"{PipeNetworkType}'s OnTick, ExecuteBurst, State or Nodes (exlib)";
        return null;
    }

    private static void Patch(Harmony harmony, ILogger logger)
    {
        _leadBroken = false;
        _logger = logger;
        harmony.Patch(_burstGetter, postfix: new HarmonyMethod(typeof(UnifiedPipesSystem), nameof(BurstPressurePostfix)));
        harmony.Patch(_onTick, postfix: new HarmonyMethod(typeof(UnifiedPipesSystem), nameof(OnTickPostfix)));
        var costs = AccessTools.TypeByName(RecipeCostsType);
        _gridRecipesFor = costs == null ? null : AccessTools.DeclaredMethod(costs, "GridRecipesFor", [typeof(ICoreAPI), typeof(AssetLocation)]);
        if (_gridRecipesFor is { IsStatic: true } && _gridRecipesFor.ReturnType == typeof(IEnumerable<GridRecipe>))
            harmony.Patch(_gridRecipesFor, postfix: new HarmonyMethod(typeof(UnifiedPipesSystem), nameof(GridRecipesForPostfix)));
        else
            logger.Warning($"[seraphhorizons] Unified pipes: {RecipeCostsType}.GridRecipesFor is not as expected; exlib changed, "
                           + "so ppex's recipe cost levels may change this mod's pipe recipes too");
    }

    /// <summary>The pipe's metal's figure, where the settings know the metal.</summary>
    public static void BurstPressurePostfix(Block __instance, ref float __result)
    {
        if (_settings.BurstPressureFor(__instance.Variant?["material"]) is float figure)
            __result = figure;
    }

    /// <summary>After a run's tick: if it holds steam or exhaust, one of its lead pipes bursts, as
    /// ppex bursts an over-pressured pipe (the pipe drops, a plume, a bang). One a tick, so a run
    /// loses its lead a section a second. Arguments by position: (blockAccessor, dt, manager).</summary>
    public static void OnTickPostfix(object __instance, IBlockAccessor __0, object __2)
    {
        if (_leadBroken)
            return;
        try
        {
            if (_state!.GetValue(__instance) is not { } state
                || !PipeRules.LeadBursts((bool)_isLiquid!.GetValue(state)!, (float)_volume!.GetValue(state)!, (string?)_medium!.GetValue(state)))
                return;
            if (_nodes!.GetValue(__instance) is not IEnumerable nodes)
                return;
            BlockPos? lead = null;
            foreach (var node in nodes)
            {
                if (node is BlockPos pos && IsLeadPipe(__0.GetBlock(pos)))
                {
                    lead = pos.Copy();
                    break;
                }
            }
            if (lead != null)
                _executeBurst!.Invoke(null, [lead, __0, __2]);
        }
        catch (Exception e)
        {
            _leadBroken = true;
            _logger?.Error($"[seraphhorizons] Unified pipes: bursting a lead pipe failed, so lead no longer bursts on steam: {e}");
        }
    }

    /// <summary>A lead pipe that can burst: ppex's own pipe class exactly (straight, bend, T- and
    /// X-junction; valves and fittings never burst).</summary>
    public static bool IsLeadPipe(Block? block) =>
        block != null && block.GetType() == _ppexPipe && block.Variant?["material"] == PipeRules.Lead;

    /// <summary>For an output of ppex's, only ppex's own recipes. Arguments by position: (api, outputWildcard).</summary>
    public static void GridRecipesForPostfix(AssetLocation __1, ref IEnumerable<GridRecipe> __result)
    {
        string? domain = __1?.Domain;
        var recipes = __result;
        __result = recipes.Where(r => PipeRules.KeepsCostRecipe(domain, r.Name?.Domain));
    }
}
