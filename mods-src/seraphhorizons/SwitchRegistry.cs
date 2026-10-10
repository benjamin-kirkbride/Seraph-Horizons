using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.GearCutter;
using SeraphHorizons.Mod.GearReclamation;
using SeraphHorizons.Mod.Gears;
using SeraphHorizons.Mod.PicklingTub;
using SeraphHorizons.Mod.Rosser;
using SeraphHorizons.Mod.SteelBits;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Switch ownership (README "Switch ownership"): which <see cref="SeraphHorizonsConfig"/> switch adds
/// a recipe or an item. Built from the lists the features themselves disable with their switch off
/// (their recipe files and their block and item type files, whose codes are read from the files),
/// plus <see cref="SwitchOwnership.HandListed"/>. The recipe exporter calls <see cref="For"/> by
/// reflection (<c>tools/recipe-export</c>, <c>Recipes/Switches.cs</c>).
///
/// A switch that only takes things away (<c>HydrateTunRetired</c>, <c>IrrigationVesselRetired</c>,
/// <c>BloodSausageInMixingBowl</c>, <c>PanningDropsTrimmed</c>, <c>GearPartsRemoved</c>, <c>TraderSchematics</c>), or only
/// changes what an existing recipe takes (<c>GearConsumers</c>, <c>IronWoodworkingMachines</c>,
/// <c>AgeOfFlaxRebalance</c>, <c>MachineSchematics</c>), owns nothing here: no value exists only
/// because it is on.
/// </summary>
public static class SwitchRegistry
{
    // Type files open with comments and use the game's lenient JSON.
    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>Each switch with the recipe files and type files its feature disables when off.</summary>
    public static IEnumerable<(string Switch, AssetLocation[] Recipes, AssetLocation[] Types)> Features()
    {
        yield return (nameof(SeraphHorizonsConfig.BuckingSawmill),
            [BuckingSawmillSystem.RecipeAsset], BuckingSawmillSystem.BlockAssets);
        yield return (nameof(SeraphHorizonsConfig.Rosser), [RosserSystem.RecipeAsset], RosserSystem.BlockAssets);
        yield return (nameof(SeraphHorizonsConfig.GearReclamation),
            [PicklingTubSystem.RecipeAsset, GearReclamationSystem.CookingAsset, .. GearReclamationSystem.BarrelAssets],
            [PicklingTubSystem.BlockAsset]);
        yield return (nameof(SeraphHorizonsConfig.GearBlanks), GearBlanks.RecipeAssets, GearBlanks.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.GearCutter), GearCutterSystem.RecipeAssets, GearCutterSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.DrawBench), DrawBench.DrawBenchSystem.RecipeAssets, DrawBench.DrawBenchSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.PressBrake), PressBrake.PressBrakeSystem.RecipeAssets, PressBrake.PressBrakeSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.SquaringShear), SquaringShear.SquaringShearSystem.RecipeAssets, SquaringShear.SquaringShearSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.MandrelStation), MandrelStation.MandrelStationSystem.RecipeAssets, MandrelStation.MandrelStationSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.StainlessSteel), CrucibleFurnace.CrucibleFurnaceSystem.RecipeAssets, CrucibleFurnace.CrucibleFurnaceSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.SteelBitsRecovery), [SteelBitsSystem.RecipeAsset], []);
        yield return (nameof(SeraphHorizonsConfig.Handcar), [Handcar.HandcarSystem.RecipeAsset], Handcar.HandcarSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.CreativeSteamSource), [], [CreativeSteamSource.BlockAsset]);
        yield return (nameof(SeraphHorizonsConfig.CastPipes), Pipes.CastPipesSystem.RecipeAssets, Pipes.CastPipesSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.UnifiedPipes), Pipes.UnifiedPipesSystem.RecipeAssets, Pipes.UnifiedPipesSystem.TypeAssets);
        yield return (nameof(SeraphHorizonsConfig.Eidolon),
            [.. EidolonGantry.EidolonGantrySystem.RecipeAssets, .. Eidolon.EidolonCommanderSystem.RecipeAssets],
            [.. EidolonGantry.EidolonGantrySystem.TypeAssets, .. Eidolon.EidolonCommanderSystem.TypeAssets, .. Eidolon.EidolonSystem.TypeAssets]);
        // Not the crushed ore's type, whose code (game:crushed) is vanilla's crushed item's too:
        // SwitchOwnership.HandListed lists its codes.
        yield return (nameof(SeraphHorizonsConfig.OreProcessing), [],
            Ore.Processing.OreProcessingSystem.TypeAssets.Where(a => a.Domain != "game").ToArray());
    }

    /// <summary>The registry, its codes read from this side's type assets (a type file that is
    /// missing or has no code adds none; <c>SwitchOwnershipScenarios</c> checks there is none).</summary>
    public static SwitchOwnership For(ICoreAPI api)
    {
        var owned = new List<OwnedBySwitch>();
        foreach (var (name, recipes, types) in Features())
        {
            var codes = new List<string>();
            foreach (var type in types)
                if (CodeOf(api, type) is { } code)
                    codes.AddRange(SwitchOwnership.TypeCodePatterns(type.Domain, code));
            owned.Add(new OwnedBySwitch(name, recipes.Select(r => r.ToString()).ToList(), codes, []));
        }
        foreach (var hand in SwitchOwnership.HandListed)
        {
            int i = owned.FindIndex(o => o.Switch == hand.Switch);
            if (i < 0)
            {
                owned.Add(hand);
                continue;
            }
            owned[i] = owned[i] with
            {
                RecipeAssets = [.. owned[i].RecipeAssets, .. hand.RecipeAssets],
                CodePatterns = [.. owned[i].CodePatterns, .. hand.CodePatterns],
                RecipeTypes = [.. owned[i].RecipeTypes, .. hand.RecipeTypes],
            };
        }
        return new SwitchOwnership(owned);
    }

    /// <summary>The <c>code</c> of a block or item type file, or null.</summary>
    public static string? CodeOf(ICoreAPI api, AssetLocation location)
    {
        if (api.Assets.TryGet(location) is not { } asset)
            return null;
        try
        {
            return JToken.Parse(asset.ToText(), Lenient) is JObject json
                   && json.GetValue("code", StringComparison.OrdinalIgnoreCase) is { Type: JTokenType.String } code
                ? (string)code!
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
