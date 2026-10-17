using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One kind of quartz a full charge of the amalgam pan works: the crushed quartz (consumed, and
/// given back amalgamated), its amalgam and how many on average a charge makes.</summary>
public sealed record AmalgamCharge(Item Input, string Ore, string Grade, Item Amalgam, double AmalgamItems);

public sealed class AmalgamPanData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The kept stages' alternatives: the muller (a rod), the shoes (a plate).</summary>
    public List<(string Template, List<Item> Stacks)> Kept = new();
    public required Item Mercury;
    public required Item? Water;
    public required int ChargeItems;
    public required double WaterLitres;
    public required double AmalgamMercury;
    public required double TurnsPerItem;
    public required double HandTurnsPerSecond;
    public List<AmalgamCharge> Charges = new();
}

/// <summary>
/// The amalgam pan (seraphhorizons, AmalgamPan/, #718): read from the frame block's
/// <c>seraphhorizonsAmalgamPan</c> attribute, which the server sets with the pan on (AmalgamPanSystem:
/// the charge, its water, the mercury an amalgam takes, the turns, and each kind of crushed gold or
/// silver quartz with the amalgam a full charge of it makes by ore processing's recovery), and the
/// build file's kept stages (config/builds/amalgampan.json). Null when the mod is not loaded or the pan
/// is switched off (no mark).
/// </summary>
public static class AmalgamPanExport
{
    public const string AttributeKey = "seraphhorizonsAmalgamPan";
    public const string FrameCode = "seraphhorizons:amalgampan-frame-north";
    public static readonly AssetLocation BuildAsset = new(GearChain.Mod, "config/builds/amalgampan.json");

    // config/builds/amalgampan.json: the muller (a rod), then the shoes (a plate)
    public static readonly (string Template, string[] Codes)[] KeptStages =
    [
        ("game:rod-iron", ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"]),
        ("game:metalplate-iron", ["game:metalplate-iron", "game:metalplate-steel"]),
    ];

    private static Item? ItemOf(ICoreServerAPI api, string? code) =>
        code != null && api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static AmalgamPanData? Read(ICoreServerAPI api)
    {
        if (api.World.GetBlock(new AssetLocation(FrameCode)) is not { IsMissing: false, Code: not null } frame)
            return null;
        var mark = frame.Attributes?[AttributeKey];
        if (mark?.Exists != true || ItemOf(api, mark["mercury"].AsString(null)) is not { } mercury)
            return null;
        var data = new AmalgamPanData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = frame,
            Mercury = mercury,
            Water = ItemOf(api, "game:waterportion"),
            ChargeItems = mark["chargeItems"].AsInt(16),
            WaterLitres = mark["water"].AsDouble(0),
            AmalgamMercury = mark["amalgamMercury"].AsDouble(10),
            TurnsPerItem = mark["turnsPerItem"].AsDouble(1),
            HandTurnsPerSecond = mark["handTurnsPerSecond"].AsDouble(2.0 / 3),
        };
        foreach (var (template, codes) in KeptStages)
        {
            var stacks = codes.Select(c => ItemOf(api, c)).OfType<Item>().ToList();
            if (stacks.Count == 0) return null;
            data.Kept.Add((template, stacks));
        }
        foreach (var charge in mark["charges"].AsArray() ?? [])
        {
            if (ItemOf(api, charge["input"].AsString(null)) is not { } input || ItemOf(api, charge["amalgam"].AsString(null)) is not { } amalgam)
                continue;
            data.Charges.Add(new AmalgamCharge(input, charge["ore"].AsString(""), charge["grade"].AsString(""), amalgam,
                charge["amalgamItems"].AsDouble(0)));
        }
        data.Charges.Sort((a, b) => string.CompareOrdinal(a.Input.Code.ToString(), b.Input.Code.ToString()));
        return data.Charges.Count == 0 ? null : data;
    }
}
