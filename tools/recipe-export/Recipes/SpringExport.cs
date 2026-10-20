using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One kind of spring as a source of water: its blocks (one per rock), the portion it gives
/// (Hydrate or Diedrate's fresh well water, or the game's water without it, as the spring block's
/// <c>waterTightContainerProps.whenFilled</c> says), the portion's items per litre, the litres a
/// second it feeds a pipe tapping it, and what a mechanical pump drawing on it adds per turn a second
/// of its shaft, as a share of that rate (SpringsSettings.PumpBoostPerTurn).</summary>
public sealed record SpringSource(string Kind, List<Block> Blocks, Item Water, double ItemsPerLitre, double LitresPerSecond, double PumpBoostPerTurn);

/// <summary>
/// Springs (seraphhorizons, Springs/): the spring blocks the rivulet generator places, read from the
/// loaded blocks (<c>seraphhorizons:waterspring-{kind}-{rock}</c>), with the rates live from the
/// server's SpringsSettings (NormalLitresPerSecond, RapidLitresPerSecond, PumpBoostPerTurn). Empty when the mod is not
/// loaded or its Springs switch is off.
/// </summary>
public static class SpringExport
{
    public const string BlockBase = "waterspring";
    public static readonly (string Kind, string RateKey)[] Kinds = [("normal", "NormalLitresPerSecond"), ("rapid", "RapidLitresPerSecond")];

    public static List<SpringSource> Read(ICoreServerAPI api)
    {
        var found = new List<SpringSource>();
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem)
            return found;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "Springs") is not true)
            return found;
        var settings = GearChain.Prop(config, "SpringsSettings");
        foreach (var (kind, rateKey) in Kinds)
        {
            string prefix = $"{BlockBase}-{kind}-";
            var blocks = api.World.Blocks
                .Where(b => b is { IsMissing: false, Id: > 0, Code: { Domain: GearChain.Mod } c } && c.Path.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(b => b.Code.Path, StringComparer.Ordinal)
                .ToList();
            if (blocks.Count == 0)
                continue;
            string? code = blocks[0].Attributes?["waterTightContainerProps"]?["whenFilled"]?["stack"]?["code"]?.AsString();
            if (code == null || api.World.GetItem(new AssetLocation(code)) is not { IsMissing: false } water)
                continue;
            double perLitre = water.Attributes?["waterTightContainerProps"]?["itemsPerLitre"]?.AsDouble(100) ?? 100;
            double rate = GearChain.Prop(settings, rateKey) is { } v ? Convert.ToDouble(v) : 0;
            if (!(rate > 0))
                continue;
            double boost = GearChain.Prop(settings, "PumpBoostPerTurn") is { } b ? Convert.ToDouble(b) : 0;
            found.Add(new SpringSource(kind, blocks, water, perLitre, rate, boost));
        }
        return found;
    }
}
