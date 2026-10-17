using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// The rusty gear stacks to 64 (switch <see cref="SeraphHorizonsConfig.RustyGearStack"/>, README
/// "The rusty gear stacks to 64"), not the 1000 the game gives it (its <c>maxstacksizeByType</c>);
/// the temporal gear keeps its 1. Set on the loaded item server side in <c>AssetsFinalize</c>, as
/// <see cref="RockStackSystem"/> does its blocks, before <see cref="BigPiles"/> sizes the rusty
/// gear's pile by it; clients get it in the item type packet. A stack already above 64 in a world
/// stays as it is until it is moved: merges stop at 64 and takes split.
/// </summary>
public static class RustyGearStack
{
    public const string Code = "game:gear-rusty";
    public const int Stack = 64;

    /// <summary>Sets the stack. False when the game has no rusty gear.</summary>
    public static bool Apply(ICoreAPI api)
    {
        if (api.World.GetItem(new AssetLocation(Code)) is not { IsMissing: false } gear) return false;
        gear.MaxStackSize = Stack;
        return true;
    }
}
