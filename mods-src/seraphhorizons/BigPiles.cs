using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Big piles (switch <see cref="SeraphHorizonsConfig.BigPiles"/>, README "Big piles"): every ground
/// pile, the game's <c>Stacking</c> ground storage layout, holds <see cref="BigPileRules.StacksPerPile"/>
/// of the item's inventory stacks per block, whoever gave the item its pile: its own type file, a
/// JSON patch or More Piles, which adds its piles in code in its <c>AssetsFinalize</c> (a JSON patch
/// cannot reach those). Run by <see cref="StorageTweaksSystem"/> after More Piles and after the
/// stack sizes are final (<see cref="StorageTweaksSystem.ExecuteOrder"/>).
///
/// Each pile's <c>GroundStorageProperties</c> are changed in place, by <see cref="BigPileRules.Big"/>
/// (capacity, drawing and collision numbers, a Ctrl click's quantity), and so are the behavior's
/// properties as text (<c>propertiesAtString</c>), with every number written out and
/// <see cref="MarkerKey"/> holding the pile as it was. That text is what the item and block type
/// packets carry, and a client builds its behavior from it, so a client gets the big pile as the
/// server has it. A client also runs this pass: More Piles replaces many of its piles again on the
/// client (its <c>ForceReplaceBehavior</c>) from its own config, and those carry no marker, so they
/// are made big there from the stack size the server sent; a pile that came with the marker is left
/// alone. A pile that already holds more than 64 stacks keeps its capacity.
///
/// Piles placed in a world read the item's properties when they load (they keep none of their own,
/// unless the game forced them, which it does only for a shattered pile), so they take the new
/// capacity at once; with the switch off again, a pile filled past its old capacity keeps every
/// item but takes no more.
/// </summary>
public static class BigPiles
{
    /// <summary>The key in a pile behavior's properties that marks it big, holding its numbers before.</summary>
    public const string MarkerKey = "seraphhorizonsBigPiles";

    /// <summary>Makes every stacking pile of this side's collectibles big. Returns how many changed.</summary>
    public static int Apply(ICoreAPI api)
    {
        int changed = 0;
        foreach (var collectible in api.World.Collectibles)
        {
            if (collectible?.Code == null || collectible.CollectibleBehaviors == null) continue;
            foreach (var behavior in collectible.CollectibleBehaviors)
                if (behavior is CollectibleBehaviorGroundStorable ground && Enlarge(collectible, ground))
                    changed++;
        }
        return changed;
    }

    /// <summary>Whether the behavior's pile was big already: it carries <see cref="MarkerKey"/>.</summary>
    public static bool IsBig(CollectibleBehaviorGroundStorable ground) =>
        Properties(ground)?.Property(MarkerKey, StringComparison.OrdinalIgnoreCase) != null;

#pragma warning disable CS0618 // ModelItemsToStackSizeRatio: obsolete in the game, still read by its Layers
    /// <summary>A pile's numbers as <see cref="BigPileRules"/> takes them.</summary>
    public static BigPileRules.Pile Numbers(GroundStorageProperties p) =>
        new(p.StackingCapacity, p.ItemsPerModel, p.CuboidsPerModel, p.CbScaleYByLayer, p.ModelItemsToStackSizeRatio,
            p.TransferQuantity, p.BulkTransferQuantity);

    /// <summary>Makes one behavior's pile big, if it is a stacking pile not made big yet.</summary>
    public static bool Enlarge(CollectibleObject collectible, CollectibleBehaviorGroundStorable ground)
    {
        if (ground.StorageProps is not { Layout: EnumGroundStorageLayout.Stacking } props) return false;
        var json = Properties(ground) ?? new JObject();
        if (json.Property(MarkerKey, StringComparison.OrdinalIgnoreCase) != null) return false;
        var old = Numbers(props);
        var big = BigPileRules.Big(old, collectible.MaxStackSize);
        if (big == old) return false;

        props.StackingCapacity = big.StackingCapacity;
        props.ItemsPerModel = big.ItemsPerModel;
        props.CuboidsPerModel = big.CuboidsPerModel;
        props.CbScaleYByLayer = big.CbScaleYByLayer;
        props.ModelItemsToStackSizeRatio = big.ModelItemsToStackSizeRatio;
        props.BulkTransferQuantity = big.BulkTransferQuantity;

        // Every number written out: the behavior's Initialize on a client derives itemsPerModel and
        // cuboidsPerModel when they are missing.
        Set(json, "stackingCapacity", big.StackingCapacity);
        Set(json, "itemsPerModel", big.ItemsPerModel);
        Set(json, "cuboidsPerModel", big.CuboidsPerModel);
        Set(json, "cbScaleYByLayer", big.CbScaleYByLayer);
        Set(json, "modelItemsToStackSizeRatio", big.ModelItemsToStackSizeRatio);
        Set(json, "bulkTransferQuantity", big.BulkTransferQuantity);
        json[MarkerKey] = new JObject
        {
            ["stackingCapacity"] = old.StackingCapacity,
            ["itemsPerModel"] = old.ItemsPerModel,
            ["cbScaleYByLayer"] = old.CbScaleYByLayer,
            ["modelItemsToStackSizeRatio"] = old.ModelItemsToStackSizeRatio,
            ["bulkTransferQuantity"] = old.BulkTransferQuantity,
        };
        ground.propertiesAtString = json.ToString(Formatting.None);
        return true;
    }
#pragma warning restore CS0618

    private static JObject? Properties(CollectibleBehaviorGroundStorable ground)
    {
        if (string.IsNullOrWhiteSpace(ground.propertiesAtString)) return null;
        try
        {
            return JToken.Parse(ground.propertiesAtString) as JObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sets <paramref name="key"/>, dropping any spelling of it in another case first: the
    /// game reads the properties without regard to case, and More Piles writes them capitalised.</summary>
    private static void Set(JObject json, string key, JToken value)
    {
        foreach (var p in json.Properties().Where(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)).ToList())
            p.Remove();
        json[key] = value;
    }
}
