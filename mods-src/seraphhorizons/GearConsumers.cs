using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.GearReclamation.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod;

/// <summary>
/// Gears (#473, epic #484): the rusty gear (<c>game:gear-rusty</c>) is salvage and money, so
/// nothing is built from one. Every recipe in the pack that took a rusty gear takes the pack's
/// steel gear (<see cref="SteelGear"/>) in the same number, by JSON patches,
/// <c>patches/gearconsumers-{modid}.json</c> (<see cref="PatchAssets"/>), one per mod patched,
/// each <c>dependsOn</c> that mod. Pipes and Power Expanded's machines and valves, and Steelmaking
/// Expanded's, come in pairs, one recipe taking the rusty gear and one ppex's anvil gears
/// (<c>ppex:gear-*</c>): the first takes the steel gear and the second is switched off. ppex's
/// anvil gears and large gears are no longer smithed and are hidden from the creative inventory and
/// the handbook. A few uses are left alone (the rusty gear amulet, the barrel dyes that use the
/// rust and Cartwright's gear sign; BetterLoot+'s gear parts are gone, <see cref="GearPartsRemoved"/>): <c>tools/tests/test_gear_consumers.py</c>
/// lists them, and fails on any other use, so a mod update that adds one is caught.
///
/// Steelmaking Expanded's Bessemer converter is raised from its control block with a large gear in
/// the hotbar, named in its code, not in a recipe: <c>BlockEntityConverterControl.IsSpawnGear</c>
/// takes <c>ppex:largegear-iron</c> or <c>-steel</c>, and <c>BlockConverterBessemer.GetDrops</c>
/// gives a <c>ppex:largegear-iron</c> back when the vessel is broken. Postfixes make the first take
/// only <see cref="SteelLargeGear"/> and the second give that back instead (both sides: the client
/// checks the hotbar before it sends the click). If either method is gone, a warning is logged and
/// the converter keeps taking ppex's large gears, which are no longer made.
///
/// With the switch off, <see cref="DisablePatches"/> empties every patch file in <c>Start</c>,
/// before the game's patch loader runs in <c>AssetsLoaded</c>, and the converter is not patched.
/// The rusty gear stays currency; the mechanic's trade list no longer has ppex's gears (#436).
/// </summary>
public static class GearConsumers
{
    public const string HarmonyId = "seraphhorizons.gearconsumers";

    /// <summary>The pack's steel gear, what every rusty gear use now takes (#474 adds the item).</summary>
    public const string SteelGear = GearCodes.Steel;

    /// <summary>The pack's steel large gear, cut on the gear cutter (#480).</summary>
    public const string SteelLargeGear = GearCodes.LargeSteel;

    public const string SmexId = "smex";
    public const string ControlType = "SteelmakingExpanded.BlockStructures.Converter.BlockEntities.BlockEntityConverterControl";
    public const string ConverterType = "SteelmakingExpanded.BlockStructures.Converter.Blocks.BlockConverterBessemer";
    public const string SpawnGearMethod = "IsSpawnGear";

    /// <summary>The large gears Steelmaking Expanded's converter takes and gives back (0.10.1).</summary>
    public static readonly string[] SmexLargeGears = ["ppex:largegear-iron", "ppex:largegear-steel"];

    /// <summary>The patch files, by the mod each is for (<c>game</c>: the game's own recipes).</summary>
    public static readonly string[] PatchedMods =
    [
        "game", "ppex", "smex", "betterruins", "butchering", "flyingmachine", "immersivewoodworking",
        "playercorpseforked", "spinningwheel", "sprinklersmod",
    ];

    public static readonly AssetLocation[] PatchAssets =
        PatchedMods.Select(mod => new AssetLocation("seraphhorizons", $"patches/gearconsumers-{mod}.json")).ToArray();

    /// <summary>Steelmaking Expanded's handbook and its converter's refusal name the steel large gear.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "smex:bessemer-err-materials", "Needs {0} large gear and", "Needs {0} steel large gear and"),
        new("en", "smex:handbook-bessemer-text", "it with one large gear and eight", "it with one steel large gear and eight"),
    ];

    private static MethodInfo? _isSpawnGear;
    private static MethodInfo? _getDrops;

    /// <summary>Empties every patch file, so the patch loader applies none of them. Runs in Start:
    /// the assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        foreach (var location in PatchAssets)
        {
            var asset = api.Assets.TryGet(location);
            if (asset != null)
                asset.Data = "[]"u8.ToArray();
        }
    }

    public static bool BessemerApplies(ICoreAPI api) => api.ModLoader.IsModEnabled(SmexId);

    /// <summary>Finds the converter's gear check and its drops. Returns whether both are as expected.</summary>
    public static bool Bind(ILogger logger)
    {
        var control = AccessTools.TypeByName(ControlType);
        var converter = AccessTools.TypeByName(ConverterType);
        _isSpawnGear = control == null ? null : AccessTools.DeclaredMethod(control, SpawnGearMethod, [typeof(ItemStack)]);
        _getDrops = converter == null || !typeof(Block).IsAssignableFrom(converter)
            ? null
            : AccessTools.DeclaredMethod(converter, nameof(Block.GetDrops),
                [typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float)]);
        if (_isSpawnGear is { IsStatic: true } && _isSpawnGear.ReturnType == typeof(bool)
            && _getDrops != null && _getDrops.ReturnType == typeof(ItemStack[]))
            return true;
        _isSpawnGear = null;
        _getDrops = null;
        logger.Warning($"[seraphhorizons] {ControlType}.{SpawnGearMethod} or {ConverterType}.GetDrops is not as "
                       + "expected; Steelmaking Expanded changed, so its Bessemer converter keeps taking ppex's large gears");
        return false;
    }

    /// <summary>Postfixes the converter's gear check and drops, once per process (singleplayer runs
    /// both sides in one).</summary>
    public static void Patch(Harmony harmony)
    {
        if (Harmony.HasAnyPatches(HarmonyId))
            return;
        harmony.Patch(_isSpawnGear, postfix: new HarmonyMethod(typeof(GearConsumers), nameof(IsSpawnGearPostfix)));
        harmony.Patch(_getDrops, postfix: new HarmonyMethod(typeof(GearConsumers), nameof(GetDropsPostfix)));
    }

    /// <summary>Whether a hotbar stack raises the converter: the steel large gear only.</summary>
    public static bool RaisesConverter(ItemStack? stack) => stack?.Collectible?.Code?.ToString() == SteelLargeGear;

    public static void IsSpawnGearPostfix(ItemStack stack, ref bool __result) => __result = RaisesConverter(stack);

    /// <summary>The broken vessel gives the steel large gear back in place of smex's large gear,
    /// or nothing for it if the steel large gear does not exist.</summary>
    public static void GetDropsPostfix(IWorldAccessor world, ref ItemStack[] __result)
    {
        if (__result == null || !__result.Any(s => SmexLargeGears.Contains(s?.Collectible?.Code?.ToString())))
            return;
        var steel = world.GetItem(new AssetLocation(SteelLargeGear));
        __result = __result
            .Select(s => SmexLargeGears.Contains(s?.Collectible?.Code?.ToString()) ? steel == null ? null : new ItemStack(steel, s!.StackSize) : s)
            .Where(s => s != null)
            .ToArray()!;
    }
}
