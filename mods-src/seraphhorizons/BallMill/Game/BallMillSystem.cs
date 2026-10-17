using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.BallMill;

/// <summary>
/// The ball mill (#915; README "Ball mill"): registers its block entity. Its frame is the shared ore machine
/// block (<see cref="BlockOreMachine"/>), its cells the shared ones; its figures are ore-core's
/// (<c>config/ore-processing.json</c>, <c>machines.ballmill</c>). With the <c>BallMill</c> switch off, or ore
/// processing off (it grinds crushed ore into ground ore, which then do not exist), the server leaves its frame
/// and recipe out of the game.
/// </summary>
public class BallMillSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "ballmill";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/ballmill/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/ballmill.json")];

    /// <summary>Whether the ball mill is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.BallMill && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass("seraphhorizons.BallMill", typeof(BEBallMill));

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }
}
