using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.StampBattery;

/// <summary>
/// The stamp battery (#728, README "Stamp battery"): registers its block entity on the shared ore machine
/// code, and with the <c>StampBattery</c> switch off (or <c>OreProcessing</c>, whose items it works) leaves
/// its block and recipe out of the game on the server.
/// </summary>
public class StampBatterySystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string EntityClass = "seraphhorizons.StampBattery";

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/stampbattery/frame.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/stampbattery.json"),
    ];

    /// <summary>Whether the battery is in the game: its switch and ore processing's are on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.StampBattery && config.OreProcessing;
    }

    // Registered on both sides whatever the setting: the server decides whether the block exists.
    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass(EntityClass, typeof(BEStampBattery));

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            MachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }
}
