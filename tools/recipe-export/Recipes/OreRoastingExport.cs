using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One sulfide's roast in the firepit: its concentrate, the roasted concentrate, the share
/// kept and the game's smelting figures.</summary>
public sealed record OreRoast(Item Concentrate, Item Roasted, double Share, int MeltingPoint, double Seconds);

/// <summary>
/// Roasting in the firepit (seraphhorizons, ore processing, #720): read from the loaded items, as the
/// server set them (Ore/Processing/OreProcessingItems.cs): every <c>seraphhorizons:concentrate-*</c>
/// whose smelting is its roasted concentrate, and the share its <c>ItemOreProduct.RoastShare</c>
/// keeps (read by reflection, like <see cref="GearChain"/> reads the mod's settings). The item's own
/// <c>smelting</c> attribute says one roasted item per item, the game's terms; this record carries the
/// share. Empty with the OreProcessing switch off (the items do not exist).
/// </summary>
public static class OreRoastingExport
{
    public const string FirepitCode = "game:firepit-cold";

    public static List<OreRoast> Read(ICoreServerAPI api)
    {
        var roasts = new List<OreRoast>();
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: GearChain.Mod } code || !code.Path.StartsWith("concentrate-", StringComparison.Ordinal))
                continue;
            if (item.CombustibleProps is not { } props
                || props.SmeltedStack?.ResolvedItemstack?.Collectible is not Item roasted
                || !roasted.Code.Path.StartsWith("roastedconcentrate-", StringComparison.Ordinal))
                continue;
            if (GearChain.Prop(item, "RoastShare") is not double share || !(share > 0))
                continue;
            roasts.Add(new OreRoast(item, roasted, share, props.MeltingPoint, props.MeltingDuration));
        }
        roasts.Sort((a, b) => string.CompareOrdinal(a.Concentrate.Code.ToString(), b.Concentrate.Code.ToString()));
        return roasts;
    }
}
