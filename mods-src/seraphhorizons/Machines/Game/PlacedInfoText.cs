using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Machines;

/// <summary>The game half of <see cref="PlacedInfo"/>: a machine's controller block keeps its
/// description (<c>blockdesc-…</c>) for the item tooltip and the handbook, and leaves it out of
/// the placed block info, which shows the machine's state.</summary>
public static class PlacedInfoText
{
    /// <summary><paramref name="info"/> (the base <c>GetPlacedBlockInfo</c>) without the block's
    /// description, found the way the game finds it.</summary>
    public static string WithoutDescription(this Block block, string info) =>
        PlacedInfo.WithoutDescription(info,
            Lang.GetMatching($"{block.Code.Domain}:{block.ItemClass.ToString().ToLowerInvariant()}desc-{block.Code.Path}"));
}
