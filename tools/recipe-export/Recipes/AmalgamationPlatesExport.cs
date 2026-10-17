using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One rock of crushed or ground free gold or silver quartz over the plates: the rock (consumed, and given
/// back amalgamated), its amalgam and how much of an item a rock makes on average, the mercury that takes off the
/// dressing, and the <c>oreState</c> the rock goes on with.</summary>
public sealed record PlatesRock(Item Input, string Ore, string Grade, Item Amalgam, double AmalgamItems, double Mercury, int OreState);

public sealed class AmalgamationPlatesData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    public required Item Mercury;
    public required double Recovery;
    public required double PerHour;
    public required double WaterPerItem;
    public required double DressingLitres;
    public required int AmalgamItems;
    public List<PlatesRock> Rocks = new();
}

/// <summary>
/// The amalgamation plates (seraphhorizons, AmalgamationPlates/, #917): read from the frame block's
/// <c>seraphhorizonsAmalgamationPlates</c> attribute, which the server sets with the plates on
/// (AmalgamationPlatesSystem: the catch, rate and water from ore processing, the dressing and the plates' room,
/// and each crushed or ground free quartz with the amalgam and mercury one rock makes and takes by ore
/// processing's recovery). Null when the mod is not loaded or the plates are switched off (no mark).
/// </summary>
public static class AmalgamationPlatesExport
{
    public const string AttributeKey = "seraphhorizonsAmalgamationPlates";
    public const string FrameCode = "seraphhorizons:amalgamationplates-north";

    private static Item? ItemOf(ICoreServerAPI api, string? code) =>
        code != null && api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static AmalgamationPlatesData? Read(ICoreServerAPI api)
    {
        if (api.World.GetBlock(new AssetLocation(FrameCode)) is not { IsMissing: false, Code: not null } frame)
            return null;
        var mark = frame.Attributes?[AttributeKey];
        if (mark?.Exists != true || ItemOf(api, mark["mercury"].AsString(null)) is not { } mercury)
            return null;
        var data = new AmalgamationPlatesData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = frame,
            Mercury = mercury,
            Recovery = mark["recovery"].AsDouble(0),
            PerHour = mark["perHour"].AsDouble(0),
            WaterPerItem = mark["water"].AsDouble(0),
            DressingLitres = mark["dressingLitres"].AsDouble(0),
            AmalgamItems = mark["amalgamItems"].AsInt(0),
        };
        foreach (var rock in mark["rocks"].AsArray() ?? [])
        {
            if (ItemOf(api, rock["input"].AsString(null)) is not { } input || ItemOf(api, rock["amalgam"].AsString(null)) is not { } amalgam)
                continue;
            data.Rocks.Add(new PlatesRock(input, rock["ore"].AsString(""), rock["grade"].AsString(""), amalgam,
                rock["amalgamItems"].AsDouble(0), rock["mercury"].AsDouble(0), rock["oreState"].AsInt(0)));
        }
        data.Rocks.Sort((a, b) => string.CompareOrdinal(a.Input.Code.ToString(), b.Input.Code.ToString()));
        return data.Rocks.Count == 0 || !(data.PerHour > 0) ? null : data;
    }
}
