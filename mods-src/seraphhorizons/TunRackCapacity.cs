using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Food Shelves (<c>foodshelves</c>): the tun in a tun rack (<c>foodshelves:tunrack-*</c>) holds
/// <see cref="Litres"/> (950, Hydrate or Diedrate's tun's default size) instead of 500, so it can
/// stand in for Hydrate or Diedrate's tun, which <see cref="HydrateTun"/> retires.
///
/// Food Shelves keeps the capacity in two places, and both are set:
/// <list type="bullet">
/// <item>the block's <c>attributes.capacityLitres</c> (500), which <c>BlockLiquidContainerBase</c>
/// reads in <c>OnLoaded</c> as its <c>CapacityLitres</c>: the limit every fill and pour checks. A JSON
/// patch, <c>patches/tun-foodshelves.json</c>.</item>
/// <item><c>BETunRack</c>'s own <c>private readonly int capacityLitres = 500</c>, which its
/// constructor gives the liquid slot (<c>ItemSlotLiquidOnly.CapacityLitres</c>) and its
/// <c>Initialize</c> sets on it again. Other code that fills a liquid slot reads that. A postfix on
/// the constructor sets the field and the slot, so <c>Initialize</c> sets the same.</item>
/// </list>
///
/// Food Shelves is not referenced at build time. If <c>BETunRack</c>, its parameterless constructor
/// or its <c>int capacityLitres</c> field is gone, the tweak logs a warning and leaves the rack as it
/// is, the JSON patch included (<see cref="DisablePatches"/>, in <c>Start</c>), so the two never
/// disagree.
/// </summary>
public static class TunRackCapacity
{
    public const string ModId = "foodshelves";
    public const string RackType = "FoodShelves.BETunRack";
    public const string CapacityField = "capacityLitres";
    /// <summary>The rack's liquid slot; slot 0 holds the tun itself.</summary>
    public const int LiquidSlot = 1;
    public const int Litres = 950;

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/tun-foodshelves.json");

    private static ConstructorInfo? _ctor;
    private static FieldInfo? _capacity;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Finds <c>BETunRack</c>'s constructor and capacity field. Returns whether both were
    /// found as expected.</summary>
    public static bool Bind(ILogger logger)
    {
        var rack = AccessTools.TypeByName(RackType);
        _ctor = rack == null || !typeof(BlockEntityContainer).IsAssignableFrom(rack)
            ? null
            : AccessTools.DeclaredConstructor(rack, Type.EmptyTypes);
        _capacity = rack == null ? null : AccessTools.DeclaredField(rack, CapacityField);
        if (_ctor != null && _capacity is { IsStatic: false } && _capacity.FieldType == typeof(int))
            return true;
        _ctor = null;
        _capacity = null;
        logger.Warning($"[seraphhorizons] {RackType} does not have a constructor and an int {CapacityField} as "
                       + "expected; Food Shelves changed, so its tun rack keeps its own capacity");
        return false;
    }

    /// <summary>Empties the patch file, so the patch loader leaves the block's capacity alone. Runs
    /// in Start: the assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }

    /// <summary>Postfixes <c>BETunRack</c>'s constructor with <see cref="ConstructorPostfix"/>.</summary>
    public static void Patch(Harmony harmony) =>
        harmony.Patch(_ctor, postfix: new HarmonyMethod(typeof(TunRackCapacity), nameof(ConstructorPostfix)));

    /// <summary>Sets the rack's capacity field (read-only in C#, but an ordinary instance field to
    /// reflection) and its liquid slot, which the constructor has just made with the old value.</summary>
    public static void ConstructorPostfix(BlockEntityContainer __instance)
    {
        _capacity!.SetValue(__instance, Litres);
        if (__instance.Inventory is { Count: > LiquidSlot } inv && inv[LiquidSlot] is ItemSlotLiquidOnly slot)
            slot.CapacityLitres = Litres;
    }
}
