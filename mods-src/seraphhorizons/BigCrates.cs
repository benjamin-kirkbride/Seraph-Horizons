using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Big crates (switch <see cref="SeraphHorizonsConfig.BigCrates"/>, README "Big crates"): every
/// crate type (a block of class <c>BlockCrate</c>, its <c>attributes.properties.&lt;type&gt;.quantitySlots</c>)
/// holds <see cref="CrateSlotRules.Slots"/> slots: the game's ebony and purpleheart crates 32, its
/// other woods 26, aged wood and Material Needs' dark aged crate 21. Chests are not crates and stay.
///
/// In code, not a JSON patch: one rule for every crate type of every mod, each found by its class
/// (a patch names files and types, and would miss a crate added later), applied server side in
/// <c>AssetsFinalize</c> to the loaded block's attributes, which clients get in the block type
/// packet. The game's <c>BlockEntityCrate</c> reads its slot count from those attributes when it
/// makes its inventory, and the crate's <c>Props</c> (the boat's crate slot and the item tooltip
/// read it) is parsed again from them.
///
/// A crate already placed grows by itself: it makes its inventory from the block's slots before
/// loading the saved one, and the game's inventory reads every slot it has. The other way round it
/// would lose items, as the game reads only as many saved slots as the block now gives: so
/// <see cref="KeepSavedSlots"/>, whatever the switch says, gives a crate whose saved inventory has
/// more slots than its type now gives that many, and a crate placed with the switch on keeps its
/// slots, and everything in them, when it is turned off.
/// </summary>
public static class BigCrates
{
    public const string HarmonyId = "seraphhorizons.cratekeepsslots";
    public const string SlotsKey = "quantitySlots";

    /// <summary>Scales every crate type's slots. Returns the crate blocks changed.</summary>
    public static int Apply(ICoreAPI api)
    {
        int changed = 0;
        foreach (var block in api.World.Blocks)
        {
            if (block is not BlockCrate crate || block.Code == null) continue;
            // A copy: blocks may share their attributes.
            if (block.Attributes?.Token is not JObject own || own.DeepClone() is not JObject attributes) continue;
            if (Property(attributes, "properties")?.Value is not JObject types) continue;
            bool any = false;
            foreach (var type in types.Properties())
            {
                if (type.Value is not JObject typeProps) continue;
                var slots = Property(typeProps, SlotsKey);
                // A type without the key gets the block entity's default of 16.
                int before = slots?.Value.Type == JTokenType.Integer ? (int)slots.Value : 16;
                int after = CrateSlotRules.Slots(before);
                if (after == before) continue;
                slots?.Remove();
                typeProps[SlotsKey] = after;
                any = true;
            }
            if (!any) continue;
            block.Attributes = new JsonObject(attributes);
            if (crate.Props != null)
                crate.Props = block.Attributes.AsObject<CrateProperties>(null, block.Code.Domain);
            changed++;
        }
        return changed;
    }

    private static JProperty? Property(JObject json, string key) =>
        json.Properties().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

    // --- A crate never loses slots it was saved with ---

    private static MethodInfo? _fromTree;
    private static MethodInfo? _initInventory;
    private static FieldInfo? _inventory;

    // The saved inventory's slot count while a crate loads (FromTreeAttributes makes the inventory
    // first, then reads it).
    [ThreadStatic] private static int _savedSlots;

    /// <summary>Patches <c>BlockEntityCrate</c> (once per process; both sides make crate inventories).
    /// False, with a warning, when the game's crate is not as expected.</summary>
    public static bool KeepSavedSlots(ILogger logger)
    {
        if (Harmony.HasAnyPatches(HarmonyId)) return true;
        _fromTree = AccessTools.DeclaredMethod(typeof(BlockEntityCrate), nameof(BlockEntityCrate.FromTreeAttributes),
            [typeof(ITreeAttribute), typeof(IWorldAccessor)]);
        _initInventory = AccessTools.DeclaredMethod(typeof(BlockEntityCrate), "InitInventory", [typeof(Block), typeof(ICoreAPI)]);
        _inventory = AccessTools.DeclaredField(typeof(BlockEntityCrate), "inventory");
        if (_fromTree == null || _initInventory == null || _inventory?.FieldType != typeof(InventoryGeneric))
        {
            logger.Warning("[seraphhorizons] Big crates: BlockEntityCrate is not as expected; a crate whose type has fewer "
                           + "slots than it was saved with drops the rest when it loads");
            return false;
        }
        var harmony = new Harmony(HarmonyId);
        harmony.Patch(_fromTree, prefix: new HarmonyMethod(typeof(BigCrates), nameof(FromTreePrefix)),
            finalizer: new HarmonyMethod(typeof(BigCrates), nameof(FromTreeFinalizer)));
        harmony.Patch(_initInventory, postfix: new HarmonyMethod(typeof(BigCrates), nameof(InitInventoryPostfix)));
        return true;
    }

    public static bool Patched => Harmony.HasAnyPatches(HarmonyId);

    private static void FromTreePrefix(ITreeAttribute tree) =>
        _savedSlots = tree?.GetTreeAttribute("inventory")?.GetInt("qslots") ?? 0;

    private static Exception? FromTreeFinalizer(Exception? __exception)
    {
        _savedSlots = 0;
        return __exception;
    }

    private static void InitInventoryPostfix(BlockEntityCrate __instance)
    {
        if (_savedSlots <= 0 || _inventory!.GetValue(__instance) is not InventoryGeneric inventory) return;
        int missing = _savedSlots - inventory.Count;
        if (missing > 0) inventory.AddSlots(missing);
    }
}
