using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// Reads a spot's <see cref="Region"/> from the world: the worldgen (yearly mean) temperature, and
/// the first <c>rock-*</c> block under the terrain surface. Works on the live block accessor and on
/// a worldgen one, so a camp and the trader later spawned in it agree.
/// </summary>
public static class RegionProbe
{
    private const int RockSearchDepth = 64;

    /// <summary>Rock code to RockGroup from the game's <c>worldproperties/block/rock</c> (patched, so
    /// rocks mods add are in it), for <see cref="RegionClassifier"/>.</summary>
    public static Dictionary<string, string> RockGroups(ICoreAPI api)
    {
        var groups = new Dictionary<string, string>();
        var asset = api.Assets.TryGet(new AssetLocation("game", "worldproperties/block/rock.json"));
        if (asset is null) return groups;
        try
        {
            if (JToken.Parse(asset.ToText())["variants"] is JArray variants)
                foreach (var v in variants)
                    if (v["Code"]?.ToString() is { } code && v["RockGroup"]?.ToString() is { } group)
                        groups[code] = group;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Trading: could not read the rock groups, using its own: {0}", e.Message);
        }
        return groups;
    }

    public static Region At(IBlockAccessor blocks, BlockPos pos, RegionClassifier classifier)
    {
        var climate = blocks.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
        return classifier.Classify(climate?.Temperature ?? 10f, RockAt(blocks, pos));
    }

    public static string? RockAt(IBlockAccessor blocks, BlockPos pos)
    {
        var p = pos.Copy();
        int top = blocks.GetTerrainMapheightAt(p);
        if (top <= 0) top = pos.Y;
        for (int y = top; y > Math.Max(0, top - RockSearchDepth); y--)
        {
            p.Y = y;
            var block = blocks.GetBlock(p);
            if (block?.Code is { } code && code.Path.StartsWith("rock-") && block.Variant["rock"] is { } rock)
                return rock;
        }
        return null;
    }
}
