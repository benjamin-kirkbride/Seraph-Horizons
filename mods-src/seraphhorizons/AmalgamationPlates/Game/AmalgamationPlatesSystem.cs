using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.AmalgamationPlates.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.AmalgamationPlates;

/// <summary>
/// The amalgamation plates (#917; README "Ore processing: the amalgamation plates"): registers their block entity
/// and holds their settings (AmalgamationPlatesSettings in ModConfig/seraphhorizons.json). Their frame is the
/// shared ore machine block (<see cref="BlockOreMachine"/>), their cells the shared ones; their figures are
/// ore-core's (<c>config/ore-processing.json</c>, <c>machines.amalgamationplates</c>). With the
/// <c>AmalgamationPlates</c> switch off, or ore processing off (they take crushed ore and make amalgam, which
/// then do not exist), the server leaves their frame and recipe out of the game.
/// </summary>
public class AmalgamationPlatesSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "amalgamationplates";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/amalgamationplates/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/amalgamationplates.json")];

    private ICoreAPI? _api;
    private AmalgamationPlatesConfig? _config;

    public static AmalgamationPlatesSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<AmalgamationPlatesSystem>();

    public AmalgamationPlatesConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>Whether the plates are in the game: their switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.AmalgamationPlates && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.AmalgamationPlates", typeof(BEAmalgamationPlates));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    /// <summary>The frame blocks' attribute the recipe exporter reads (tools/recipe-export, AmalgamationPlatesExport):
    /// what the plates do to one rock of each crushed or ground free gold or silver quartz, by ore processing's
    /// recovery, which only the server reads.</summary>
    public const string ExportKey = "seraphhorizonsAmalgamationPlates";

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !Applies(api) || OreProcessingSystem.Of(api).Recovery is not { } recovery)
            return;
        var rocks = new JArray();
        foreach (var spec in recovery.Ores.Where(o => o.Free).OrderBy(o => o.Ore, StringComparer.Ordinal))
            foreach (var grade in OreProducts.Grades)
                foreach (var input in new[] { OreProducts.CrushedCode(spec.Ore, grade), OreProducts.GroundCode(spec.Ore, grade) })
                {
                    if (api.World.GetItem(new AssetLocation(input)) is not { IsMissing: false } item
                        || OreStreamAttributes.Read(new ItemStack(item)) is not { } stream)
                        continue;
                    var pass = PlateAmalgamation.Pass(recovery, stream, 1, double.MaxValue, 0, double.MaxValue);
                    if (pass.Verdict != PlateVerdict.Caught)
                        continue;
                    var state = pass.Rest.State;
                    if (OreStreamAttributes.FormOf(item) == OreForm.Ground)
                        state &= ~OreState.Ground;
                    rocks.Add(new JObject
                    {
                        ["input"] = input,
                        ["ore"] = spec.Ore,
                        ["grade"] = grade,
                        ["amalgam"] = pass.AmalgamCode,
                        ["amalgamItems"] = pass.AmalgamUnits / recovery.ConcentrateUnits,
                        ["mercury"] = pass.MercurySpent,
                        ["oreState"] = (int)state,
                    });
                }
        var machine = recovery.Machine(OreMachine.AmalgamationPlates);
        var mark = new JObject
        {
            ["mercury"] = recovery.Retort.Mercury,
            ["amalgamMercury"] = recovery.Retort.AmalgamMercury,
            ["recovery"] = machine.Recovery,
            ["perHour"] = machine.ItemsPerHour,
            ["water"] = machine.WaterPerItem,
            ["dressingLitres"] = Config.DressingLitres,
            ["amalgamItems"] = Config.AmalgamItems,
            ["rocks"] = rocks,
        };
        foreach (var block in api.World.Blocks.Where(b => b?.Code is { Domain: Domain } c && c.Path.StartsWith(Machine + "-", StringComparison.Ordinal)))
        {
            var token = block.Attributes?.Token as JObject ?? new JObject();
            token[ExportKey] = mark.DeepClone();
            block.Attributes = new Vintagestory.API.Datastructures.JsonObject(token);
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static AmalgamationPlatesConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).AmalgamationPlatesSettings ?? new AmalgamationPlatesConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Amalgamation plates: ModConfig/{SeraphHorizonsSystem.ConfigFile}, AmalgamationPlatesSettings: {fix}");
        return config;
    }
}
