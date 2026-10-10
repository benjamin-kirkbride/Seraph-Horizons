using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One thing the still retorts: the item, the mercury, the portions of it one item gives,
/// what each item leaves in the boiler (null: nothing), and the portions a second.</summary>
public sealed record OreRetort(Item Input, Item Mercury, double Portions, Item? Residue, double PortionsPerSecond);

/// <summary>
/// Retorting in the still (seraphhorizons, ore processing, #726): read from the loaded items, as the
/// server marked them (Ore/Processing/MercuryStill.cs, the <c>seraphhorizonsRetort</c> attribute):
/// cinnabar into mercury, an amalgam into mercury and its sponge. The game's still (boiler and
/// condenser) knows them only through the pack's patches, so the export's own <c>distillation</c>
/// attribute does not carry them. Empty with the OreProcessing switch off (nothing is marked).
/// </summary>
public static class OreRetortingExport
{
    public const string AttributeKey = "seraphhorizonsRetort";
    public const string BoilerCode = "game:verticalboiler-west";
    public const string CondenserCode = "game:condenser-west";

    public static List<OreRetort> Read(ICoreServerAPI api)
    {
        var retorts = new List<OreRetort>();
        foreach (var item in api.World.Items)
        {
            var json = item?.Attributes?[AttributeKey];
            if (item?.Code == null || json?.Exists != true)
                continue;
            if (api.World.GetItem(new AssetLocation(json["mercury"].AsString(""))) is not { IsMissing: false } mercury)
                continue;
            string? residueCode = json["residue"].AsString(null);
            var residue = residueCode == null ? null : api.World.GetItem(new AssetLocation(residueCode));
            retorts.Add(new OreRetort(item, mercury, json["portions"].AsDouble(0), residue is { IsMissing: false } ? residue : null,
                json["pace"].AsDouble(0) * 5));
        }
        retorts.Sort((a, b) => string.CompareOrdinal(a.Input.Code.ToString(), b.Input.Code.ToString()));
        return retorts;
    }
}
