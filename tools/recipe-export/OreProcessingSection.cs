using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// Writes <c>oreProcessing</c>: the figures the pack's ore processing (epic #684) works by, for the site's
/// ore routes page (docs/recipe-browser/schema.md, "Ore processing"). The machine records say what one
/// machine does to one item; how a line of them adds up (concentrators working each other's tailings, the
/// locked shares, the last step of the whole route) is the mod's <c>Ore/Core/OreRecovery.cs</c>, which the
/// site's <c>ore-routes.ts</c> follows from these figures. Like the power section, only parameters: the
/// shares, recoveries and grade shares of <c>config/ore-processing.json</c> as the mod reads it (and its
/// <c>bullion.richLeadSilver</c>, which decides what the Parkes kettle takes), and each ore
/// the pack processes (one with crushed ore items) with the mod's own reading of it (<c>OreRecovery.Ore</c>:
/// class, main metal, density, by-products) and the metal units its raw ore holds at each grade (its crushed
/// ore items' <c>metalUnits</c>, which raw ore carries unchanged). Nothing is written when the mod is not
/// loaded, its <c>OreProcessing</c> switch is off or its classes are not as expected.
/// </summary>
public static class OreProcessingSection
{
    public const string SystemName = "SeraphHorizons.Mod.Ore.Processing.OreProcessingSystem";
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public static readonly string[] Grades = ["poor", "medium", "rich", "bountiful"];

    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };
    private const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

    /// <summary>The scalar and table figures copied from the config as they are.</summary>
    private static readonly string[] Copied = ["concentrateUnits", "handStations", "unclassified", "fineUnground", "freeUnamalgamated", "smeltByGrade", "smelt"];

    public static void Fill(ICoreServerAPI api, JObject root)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        if (main == null || GearChain.System(api, SystemName) is not { } system) return;
        var config = main.GetType().GetMethod("ConfigFor", S)?.Invoke(null, [api]);
        if (GearChain.Prop(config, "OreProcessing") is not true) return;
        try
        {
            if (Build(api, system) is { } section) root["oreProcessing"] = section;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            api.Logger.Warning("[seraphexport] ore processing's figures could not be read ({0}); they are not exported", e);
        }
    }

    private static JObject? Build(ICoreServerAPI api, ModSystem system)
    {
        if (api.Assets.TryGet(RecoveryAsset) is not { } asset) return null;
        var file = JObject.Parse(asset.ToText(), Lenient);
        var recovery = system.GetType().GetMethod("RecoveryFor", S)?.Invoke(null, [api]);
        var oreOf = recovery?.GetType().GetMethod("Ore", [typeof(string)]);
        if (recovery == null || oreOf == null) return null;

        var section = new JObject();
        foreach (var key in Copied)
            if (file[key] is { } token) section[key] = token.DeepClone();
        // A machine without a recovery loses nothing (OreProcessingConfig.MachineEntry.Recovery's default).
        var machines = new JObject();
        if (file["machines"] is JObject entries)
            foreach (var (name, entry) in entries)
                machines[name] = new JObject { ["recovery"] = (double?)entry?["recovery"] ?? 1 };
        section["machines"] = machines;
        // The share of a rich lead bar's lead that is silver: the Parkes kettle takes only silver-lead leaner.
        if (GearChain.Prop(recovery, "RichLeadSilver") is double richLeadSilver) section["richLeadSilver"] = richLeadSilver;

        using var english = new Items.EnglishLocale();
        var ores = new JArray();
        foreach (var ore in ProcessedOres(api))
        {
            var units = new JArray();
            foreach (var grade in Grades)
            {
                var item = api.World.GetItem(new AssetLocation(GearChain.Mod, $"crushedore-{ore}-{grade}"));
                units.Add(item?.Attributes?["metalUnits"].AsDouble(0) ?? 0);
            }
            if (units.All(u => !((double)u! > 0))) continue;
            var spec = oreOf.Invoke(recovery, [ore]);
            var byProducts = new JArray();
            if (GearChain.Prop(spec, "ByProducts") is IEnumerable list)
                foreach (var bp in list)
                    byProducts.Add(new JObject
                    {
                        ["metal"] = (string?)GearChain.Prop(bp, "Metal"),
                        ["share"] = Convert.ToDouble(GearChain.Prop(bp, "Share")),
                        ["partedBy"] = GearChain.Prop(bp, "PartedBy")?.ToString()?.ToLowerInvariant(),
                    });
            ores.Add(new JObject
            {
                ["ore"] = ore,
                ["name"] = Lang.GetIfExists("game:ore-" + ore) ?? ore,
                ["metal"] = (string?)GearChain.Prop(spec, "Metal") ?? ore,
                ["class"] = GearChain.Prop(spec, "Class")?.ToString()?.ToLowerInvariant() ?? "oxide",
                ["density"] = Convert.ToDouble(GearChain.Prop(spec, "Density") ?? 1.0),
                ["free"] = GearChain.Prop(spec, "Free") is true,
                ["unparted"] = Convert.ToDouble(GearChain.Prop(spec, "Unparted") ?? 1.0),
                ["units"] = units,
                ["byProducts"] = byProducts,
            });
        }
        section["ores"] = ores;
        return section;
    }

    /// <summary>The ores with crushed ore items (<c>seraphhorizons:crushedore-{ore}-{grade}</c>), by code.</summary>
    private static IEnumerable<string> ProcessedOres(ICoreServerAPI api) =>
        api.World.Items
            .Where(i => i?.Code is { Domain: GearChain.Mod } c && !i.IsMissing && c.Path.StartsWith("crushedore-", StringComparison.Ordinal))
            .Select(i => i.Code.Path["crushedore-".Length..])
            .Where(rest => rest.LastIndexOf('-') > 0 && Grades.Contains(rest[(rest.LastIndexOf('-') + 1)..]))
            .Select(rest => rest[..rest.LastIndexOf('-')])
            .Distinct()
            .OrderBy(o => o, StringComparer.Ordinal);
}
