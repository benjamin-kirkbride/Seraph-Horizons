using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Arrastra;

/// <summary>
/// The arrastra (#732; README "Arrastra"): registers its block entity on the shared ore machine
/// (<see cref="BEArrastra"/>; the frame and its cells are the ore machines' own blocks). With the
/// <c>Arrastra</c> or <c>OreProcessing</c> switch off the server leaves its frame and recipe out of the game.
/// </summary>
public class ArrastraSystem : ModSystem
{
    public const string Domain = "seraphhorizons";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/arrastra/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/arrastra.json")];

    /// <summary>Whether the arrastra is in the game: its switch is on, and ore processing's (its crushed and
    /// ground ore are ore processing's items).</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api) is { Arrastra: true, OreProcessing: true };

    // Registered on both sides whatever the setting: the server decides whether the block exists.
    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass(BEArrastra.ClassName, typeof(BEArrastra));

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }
}
