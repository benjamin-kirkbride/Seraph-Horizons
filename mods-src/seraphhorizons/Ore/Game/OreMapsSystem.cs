using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Registers the ore and gravel map item class (<see cref="ItemOreMap"/>) on both sides: the
/// items are in every world (a client must know the class), while what makes and reserves maps is
/// server side (<see cref="OreSystem.Maps"/>).
/// </summary>
public class OreMapsSystem : ModSystem
{
    public override void Start(ICoreAPI api) => api.RegisterItemClass(ItemOreMap.ClassName, typeof(ItemOreMap));
}
