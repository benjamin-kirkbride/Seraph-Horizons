using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One crushed ore riddled: the crushed ore (the charge, and the fines it becomes, classified) and the
/// raw ore its oversize goes back as, in the default rock.</summary>
public sealed record RiddleClass(string Ore, string Grade, Item Crushed, Item? Oversize);

public sealed class RiddleData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The riddle's mesh, kept: nails and strips, iron first.</summary>
    public List<Item> Mesh = new();
    public required int MeshCount;
    public required int Lumps;
    public required int Oversize;
    public required double Shakes;
    public List<RiddleClass> Classes = new();
}

/// <summary>
/// The riddle (seraphhorizons, Riddle/, #714): its process, one record per crushed ore, read from the loaded items
/// (<c>seraphhorizons:crushedore-{ore}-{grade}</c> and vanilla's raw ore <c>game:ore-{grade}-{ore}-granite</c>,
/// the oversize's default rock) and the gameplay's RiddleSettings (LumpsPerCharge, ShakesPerCharge,
/// OversizePerCharge), read live from the server's config. A hand station: no oil and no wear. Null when the mod
/// is not loaded, its switch or OreProcessing is off, or what it names is not registered.
/// </summary>
public static class RiddleExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/riddle-rig.json");
    public const string FrameCode = "seraphhorizons:riddle-frame-north";
    public const string CrushedPrefix = "crushedore-";
    public const string DefaultRock = "granite";

    // config/builds/riddle.json (the shared staged build): the mesh and how many it takes.
    public static readonly string[] MeshCodes =
        ["game:metalnailsandstrips-iron", "game:metalnailsandstrips-meteoriciron", "game:metalnailsandstrips-steel"];
    public const int MeshCount = 2;

    private static int Int(object? o, string name, int fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToInt32(v) : fallback;

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static RiddleData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "Riddle") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        var settings = GearChain.Prop(config, "RiddleSettings");
        int lumps = Int(settings, "LumpsPerCharge", 3);
        var data = new RiddleData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            MeshCount = MeshCount,
            Lumps = lumps,
            Oversize = Math.Clamp(Int(settings, "OversizePerCharge", 1), 0, Math.Max(0, lumps - 1)),
            Shakes = Dbl(settings, "ShakesPerCharge", 12),
        };
        if (data.Lumps < 1 || !(data.Shakes > 0)) return null;
        data.Mesh = MeshCodes.Select(c => ItemOf(api, c)).OfType<Item>().ToList();
        if (data.Mesh.Count == 0) return null;
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: GearChain.Mod } code || item.IsMissing || !code.Path.StartsWith(CrushedPrefix, StringComparison.Ordinal)) continue;
            var rest = code.Path[CrushedPrefix.Length..];
            int dash = rest.LastIndexOf('-');
            if (dash <= 0) continue;
            string ore = rest[..dash], grade = rest[(dash + 1)..];
            data.Classes.Add(new RiddleClass(ore, grade, item, ItemOf(api, $"game:ore-{grade}-{ore}-{DefaultRock}")));
        }
        data.Classes.Sort((a, b) => string.CompareOrdinal(a.Crushed.Code.ToString(), b.Crushed.Code.ToString()));
        return data.Classes.Count == 0 ? null : data;
    }
}
