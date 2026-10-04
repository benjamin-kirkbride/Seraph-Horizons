using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Primitive Survival (<c>primitivesurvival</c>) has an irrigation vessel
/// (<c>primitivesurvival:irrigationvessel-*</c>, a buried clay pot that keeps the farmland around it
/// moist), and Olla (<c>olla</c>) is a mod of just that. The pack keeps Olla's: this tweak takes the
/// vessel's grid recipes away (<c>enabled: false</c>) and leaves the block out of the creative
/// inventory and the handbook, by a JSON patch, <c>patches/irrigationvessel-primitivesurvival.json</c>.
/// BetterRuins also puts the vessels in its ruins' clay loot (its Primitive Survival compatibility
/// patch adds them to <c>game:stackrandomizer-clayproducts</c>), and <see cref="DropFromRuinLoot"/>
/// takes them out of it again.
///
/// The block type itself stays registered: a vessel already placed in a world keeps its water and
/// its block entity, and still works, breaks and drops as before. Only new ones can no longer be had.
///
/// With the switch off, or without Primitive Survival, <see cref="DisablePatches"/> empties the patch
/// file in <c>Start</c>, before the game's patch loader runs in <c>AssetsLoaded</c>, and the loot is
/// left alone.
/// </summary>
public static class IrrigationVessel
{
    public const string ModId = "primitivesurvival";
    public const string Block = "primitivesurvival:irrigationvessel-normal";
    public const string CodePrefix = "primitivesurvival:irrigationvessel-";

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/irrigationvessel-primitivesurvival.json");

    /// <summary>BetterRuins' loot item (<c>stackrandomizer-*</c>), whose <c>*-clayproducts</c>
    /// variant its Primitive Survival patch adds the vessels to.</summary>
    public static readonly AssetLocation RuinLootAsset = new("game", "itemtypes/meta/stackrandomizer-betterruins.json");

    public const string RuinLootVariant = "*-clayproducts";

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }

    /// <summary>Removes the irrigation vessels from BetterRuins' clay loot. Runs on the server in
    /// AssetsLoaded, after the patch loader (0.05) has added them and before the item types are read
    /// (0.2). Without BetterRuins there is nothing to do; if its loot item is not as expected, or
    /// holds no vessel, it logs a warning and changes nothing.</summary>
    public static void DropFromRuinLoot(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled("betterruins"))
            return;
        var asset = api.Assets.TryGet(RuinLootAsset);
        JObject? json = asset == null ? null : JObject.Parse(asset.ToText());
        int removed = json == null ? -1 : DropFrom(json);
        if (removed <= 0)
        {
            api.Logger.Warning($"[seraphhorizons] {RuinLootAsset} {(removed < 0 ? "is missing or not as expected" : "holds no irrigation vessel")}; "
                               + "BetterRuins changed, so its ruin loot is left as it is");
            return;
        }
        asset!.Data = Encoding.UTF8.GetBytes(json!.ToString());
        api.Logger.Notification($"[seraphhorizons] Irrigation vessel: {removed} stacks taken out of BetterRuins' clay loot");
    }

    /// <summary>Removes every stack of an irrigation vessel from the <see cref="RuinLootVariant"/>
    /// stacks, and returns how many; -1 when that list is not there.</summary>
    public static int DropFrom(JObject json)
    {
        if (json.GetValue("attributesByType", StringComparison.OrdinalIgnoreCase) is not JObject byType
            || byType[RuinLootVariant]?["stacks"] is not JArray stacks)
            return -1;
        var vessels = stacks.OfType<JObject>()
            .Where(s => s["code"]?.Type == JTokenType.String && ((string)s["code"]!).StartsWith(CodePrefix, StringComparison.Ordinal))
            .ToList();
        foreach (var stack in vessels)
            stack.Remove();
        return vessels.Count;
    }
}
