using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The cupel's view of crucibulum's forge (#722), found by name: crucibulum (1.5.0) takes any
/// <see cref="BlockSmeltingContainer"/> as a crucible (<c>BlockEntityCrucibulumForge.IsCrucible</c> and
/// <c>ItemSlotCrucibleCharge.IsFiredCrucible</c> test the class, not the code), so the cupel goes in
/// its work slot and takes a charge with no change to crucibulum; the forge then calls the cupel's
/// <c>CanSmelt</c>, <c>GetMeltingPoint</c>, <c>GetMeltingDuration</c> and <c>DoSmelt</c> with its
/// <c>ChargeSlotProvider</c>. That provider doesn't say which forge it belongs to, so a postfix on the
/// forge's <c>Initialize</c> (Harmony id <see cref="OreProcessingSystem.HarmonyId"/>) notes each
/// forge by its provider, and <see cref="Air"/> reads that forge's blast gate.
/// </summary>
public static class CupelForge
{
    public const string ForgeType = "Crucibulum.BlockEntityCrucibulumForge";

    private static readonly ConditionalWeakTable<object, BlockEntity> ByProvider = new();
    private static FieldInfo? _provider;
    private static PropertyInfo? _airFactor, _gate, _hasGate;

    /// <summary>Whether the postfix is in: crucibulum is loaded and as expected.</summary>
    public static bool Bound { get; private set; }

    /// <summary>Patches crucibulum's forge; false (and nothing patched) when it is missing or changed.</summary>
    public static bool Patch(Harmony harmony)
    {
        var type = AccessTools.TypeByName(ForgeType);
        if (type == null) return false;
        _provider = AccessTools.Field(type, "chargeProvider");
        _airFactor = AccessTools.Property(type, "AirFactor");
        _gate = AccessTools.Property(type, "GatePosition");
        _hasGate = AccessTools.Property(type, "HasGate");
        var init = AccessTools.DeclaredMethod(type, "Initialize", [typeof(ICoreAPI)]);
        if (_provider == null || _airFactor == null || _gate == null || _hasGate == null || init == null) return false;
        harmony.Patch(init, postfix: new HarmonyMethod(typeof(CupelForge), nameof(InitializePostfix)));
        Bound = true;
        return true;
    }

    private static void InitializePostfix(BlockEntity __instance)
    {
        if (_provider?.GetValue(__instance) is { } provider)
            ByProvider.AddOrUpdate(provider, __instance);
    }

    /// <summary>The forge whose charge slots these are, or null if they are not a crucibulum forge's
    /// (a firepit's, say).</summary>
    public static BlockEntity? ForgeOf(ISlotProvider? provider) =>
        provider != null && ByProvider.TryGetValue(provider, out var forge) ? forge : null;

    /// <summary>
    /// The air the forge's blast gate lets through, which sets the cupel's pace: crucibulum's own
    /// figure (open 1, half 0.85, quarter 0.7; 1 for a forge without a gate, whose bellows blow
    /// full), and 0 with the gate shut, when nothing happens. Null when the slots are not a forge's.
    /// </summary>
    public static double? Air(ISlotProvider? provider)
    {
        if (ForgeOf(provider) is not { } forge) return null;
        if (_hasGate!.GetValue(forge) is true && _gate!.GetValue(forge)?.ToString() == "Shut") return 0;
        return _airFactor!.GetValue(forge) is float air ? air : 1;
    }
}
