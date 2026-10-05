using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Immersive Woodworking (<c>immersivewoodworking</c>): its sawmill and chopper cost iron and much
/// more smithing. A JSON patch, <c>patches/woodworking-machine-costs.json</c>, rewrites the grid
/// recipes of the two frames and the six fitted parts (<see cref="Parts"/>): every part takes nails
/// and strips, 4 or 8 to a slot where Immersive Woodworking (1.3.11) takes 1; the sash, the
/// crankshaft and the chopper's drive take a rod as well; and the nails and strips, plates and rods
/// must be of <see cref="Metals"/>, where any metal did. The blade kit and the chopper head keep
/// their recipes: their metal is already the machine's durability and speed. The bucking sawmill is
/// built from two sawmill frames and takes the sawmill's parts, so it costs more with them.
///
/// With the switch off, or without Immersive Woodworking, <see cref="DisablePatches"/> empties the
/// patch file in <c>Start</c>, before the game's patch loader runs in <c>AssetsLoaded</c>, and the
/// handbook's Machines chapter keeps its text (<see cref="LangEdits"/>).
/// </summary>
public static class WoodworkingMachineCosts
{
    public const string ModId = "immersivewoodworking";

    /// <summary>The metals a part's nails and strips, plates and rods may be of.</summary>
    public static readonly string[] Metals = ["iron", "meteoriciron", "steel"];

    /// <param name="Recipe">Immersive Woodworking's recipe file, under <c>recipes/grid/</c>.</param>
    /// <param name="Output">The part's code in Immersive Woodworking's domain.</param>
    /// <param name="Nails">Nails and strips in all.</param>
    /// <param name="Plates">Metal plates.</param>
    /// <param name="Rods">Metal rods.</param>
    /// <param name="ShippedNails">Nails and strips as Immersive Woodworking ships the recipe.</param>
    public record Part(string Recipe, string Output, int Nails, int Plates, int Rods, int ShippedNails);

    public static readonly Part[] Parts =
    [
        new("sawmill_frame", "sawmill-frame-north", 8, 0, 0, 0),
        new("sawmill_sash", "sawmillsash", 16, 0, 1, 4),
        new("sawmill_crankshaft", "sawmillcrankshaft", 8, 0, 1, 2),
        new("sawmill_levers", "sawmilllevers", 8, 1, 0, 2),
        new("sawmill_carriage", "sawmillcarriage", 8, 0, 0, 0),
        new("chopper_frame", "chopper-frame-north", 8, 2, 0, 0),
        new("chopper_drive", "chopperdrive", 8, 1, 1, 1),
        new("chopper_arm", "chopperarm", 8, 1, 0, 1),
    ];

    /// <summary>The handbook's Machines chapter (this mod's, <c>UnifiedWoodworking</c>) says what
    /// the two machines cost in all: <see cref="Parts"/> summed for each.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "seraphhorizons:woodworking-machines-text",
            "break the frame to recover the other parts.\n\n<strong><a href=\"handbook://block-immersivewoodworking:sawmill-frame-north\">",
            "break the frame to recover the other parts.\n\nThe frames and the parts are iron work: their "
            + "<a href=\"handbook://item-metalnailsandstrips-iron\">nails and strips</a>, "
            + "<a href=\"handbook://item-metalplate-iron\">plates</a> and <a href=\"handbook://item-rod-iron\">rods</a> must be "
            + "iron, meteoric iron or steel. A sawmill takes 48 nails and strips, 1 plate and 2 rods in all, a chopper 24 "
            + "nails and strips, 4 plates and 1 rod. Only the blade kit and the chopper head can be of any metal.\n\n"
            + "<strong><a href=\"handbook://block-immersivewoodworking:sawmill-frame-north\">"),
    ];

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/woodworking-machine-costs.json");

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }
}
