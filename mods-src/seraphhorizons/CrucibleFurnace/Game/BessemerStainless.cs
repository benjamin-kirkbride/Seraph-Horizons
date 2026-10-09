using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.SteelBits;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// Stainless in bulk from Steelmaking Expanded's Bessemer converter (<c>StainlessSteel</c>, #484 part
/// D, README "Crucible furnace"). Ferrochrome (<see cref="Stainless.Ferrochrome"/>) is added to smex's
/// live <c>BessemerScrapCodes</c> (<see cref="SmexScrap.EnsureListed"/>), so before and during the
/// blow the converter takes it as cold scrap of its own, booked under its code in the control's
/// <c>_scrap</c>, which smex saves, syncs, gives back from a broken vessel, and melts into the heat
/// when the blow completes. Harmony on smex's <c>BlockEntityConverterControl</c>, found by name:
/// <list type="bullet">
/// <item><c>CompleteRefining</c>: the ferrochrome the blow melts in is counted in the heat (a
/// prefix reads it from the scrap, a postfix books it once the heat is steel).</item>
/// <item><c>TryChargeScrap</c>: after the blow, ferrochrome goes straight into the molten steel,
/// which smex itself refuses (scrap goes in with raw iron), booked in the heat too.</item>
/// <item><c>TickPouring</c>: as the heat starts to pour, <see cref="BessemerHeat.Judge"/> decides:
/// stainless, the heat's stack swapped for the stainless ingot's with the same temperature; off
/// the ratio or too cold, steel less the ferrochrome, lost to the slag.</item>
/// <item><c>ToTreeAttributes</c>/<c>FromTreeAttributes</c>: the heat's ferrochrome is saved, and
/// synced to clients, as <see cref="HeatKey"/>.</item>
/// <item><c>AppendStructureState</c> (the vessel's block info): a line with the ferrochrome, its
/// share and what the heat will pour.</item>
/// </list>
/// The ledger is valid only while the heat is molten steel: anything else (emptied, broken out,
/// poured as stainless) reads as none. Patched on both sides (the client predicts the charge),
/// once per process. If a member is missing or changed, a warning is logged, nothing is patched
/// and ferrochrome is not added to smex's scrap list.
/// </summary>
public static class BessemerStainless
{
    public const string HarmonyId = "seraphhorizons.bessemerstainless";
    public const string SmexId = "smex";
    public const string ControlType = "SteelmakingExpanded.BlockStructures.Converter.BlockEntities.BlockEntityConverterControl";
    public const string Steel = "game:ingot-steel";

    /// <summary>The control's tree attribute holding the ferrochrome units melted into the heat.</summary>
    public const string HeatKey = "seraphhorizonsFerrochrome";

    private const string SmexValuesType = SmexScrap.ValuesType;

    /// <summary>smex's handbook page says what ferrochrome does in the converter.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "smex:handbook-bessemer-text", "so it comes out as steel with the rest.",
            "so it comes out as steel with the rest. <br><br> <strong>Ferrochrome makes it stainless.</strong> The ferrochrome from a "
            + "<a href=\"handbook://seraphhorizons-cruciblefurnace\">crucible furnace</a> charges at the same hatch, any time: with the "
            + "raw iron before the blow, during it, or into the finished steel after it. If it is 18-22 % of the heat's metal when you "
            + "pour (about 4 to 1, as in a melting pot) and the bath is at least 1530 °C, the heat pours as molten stainless steel; "
            + "otherwise it pours steel and the ferrochrome is lost to the slag. The vessel's panel says which."),
    ];

    private static FieldInfo? _content;
    private static FieldInfo? _contentUnits;
    private static FieldInfo? _scrap;
    private static FieldInfo? _solidified;
    private static MethodInfo? _canOperate;
    private static MethodInfo? _setStatus;
    private static MethodInfo? _syncConverter;
    private static MethodInfo? _completeRefining;
    private static MethodInfo? _tickPouring;
    private static MethodInfo? _tryChargeScrap;
    private static MethodInfo? _appendState;
    private static MethodInfo? _toTree;
    private static MethodInfo? _fromTree;
    private static PropertyInfo? _capacity;
    private static PropertyInfo? _unitsPerBit;

    private static readonly ConditionalWeakTable<object, StrongBox<int>> Heats = new();

    /// <summary>Whether smex's members were found as expected (and so are patched).</summary>
    public static bool Bound => _tickPouring != null;

    /// <summary>Whether this applies: the switch is on and smex is installed.</summary>
    public static bool Applies(ICoreAPI api) => CrucibleFurnaceSystem.Applies(api) && api.ModLoader.IsModEnabled(SmexId);

    /// <summary>Finds smex's members. Returns whether all are as expected; if not, logs a warning
    /// and leaves nothing bound.</summary>
    public static bool Bind(ILogger logger)
    {
        var control = AccessTools.TypeByName(ControlType);
        var values = AccessTools.TypeByName(SmexValuesType);
        const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
        _content = Field(control, "_content", typeof(ItemStack));
        _contentUnits = Field(control, "_contentUnits", typeof(int));
        _scrap = Field(control, "_scrap", typeof(Dictionary<string, int>));
        _solidified = Field(control, "_solidified", typeof(bool));
        _canOperate = Method(control, "CanOperate", typeof(bool), typeof(string).MakeByRefType());
        _setStatus = Method(control, "SetStatus", typeof(void), typeof(string));
        _syncConverter = Method(control, "SyncConverter", typeof(void));
        _completeRefining = Method(control, "CompleteRefining", typeof(void));
        _tickPouring = Method(control, "TickPouring", typeof(void), typeof(float));
        _tryChargeScrap = Method(control, "TryChargeScrap", typeof(bool), typeof(IPlayer), typeof(string).MakeByRefType());
        _appendState = Method(control, "AppendStructureState", typeof(void), typeof(IPlayer), typeof(StringBuilder));
        _toTree = Method(control, nameof(BlockEntity.ToTreeAttributes), typeof(void), typeof(ITreeAttribute));
        _fromTree = Method(control, nameof(BlockEntity.FromTreeAttributes), typeof(void), typeof(ITreeAttribute), typeof(IWorldAccessor));
        _capacity = values?.GetProperty("BessemerConverterCapacity", Static) is { PropertyType: var c } cap && c == typeof(int) ? cap : null;
        _unitsPerBit = values?.GetProperty("MoltenUnitsPerBit", Static) is { PropertyType: var u } upb && u == typeof(int) ? upb : null;
        if (control != null && typeof(BlockEntity).IsAssignableFrom(control)
            && _content != null && _contentUnits != null && _scrap != null && _solidified != null && _canOperate != null
            && _setStatus != null && _syncConverter != null && _completeRefining != null && _tickPouring != null
            && _tryChargeScrap != null && _appendState != null && _toTree != null && _fromTree != null
            && _capacity != null && _unitsPerBit != null)
            return true;
        Unbind();
        logger.Warning($"[seraphhorizons] Stainless steel: Steelmaking Expanded's {ControlType} or {SmexValuesType} is not as expected; "
                       + "smex changed, so its Bessemer converter does not take ferrochrome or make stainless");
        return false;
    }

    public static void Unbind()
    {
        _content = _contentUnits = _scrap = _solidified = null;
        _canOperate = _setStatus = _syncConverter = _completeRefining = _tickPouring = _tryChargeScrap = _appendState = _toTree = _fromTree = null;
        _capacity = _unitsPerBit = null;
    }

    /// <summary>Patches the bound members, once per process (singleplayer runs both sides in one).
    /// Returns the Harmony that did, or null.</summary>
    public static Harmony? Patch()
    {
        if (!Bound || Harmony.HasAnyPatches(HarmonyId))
            return null;
        var harmony = new Harmony(HarmonyId);
        HarmonyMethod Of(string name) => new(typeof(BessemerStainless), name);
        harmony.Patch(_completeRefining, prefix: Of(nameof(CompleteRefiningPrefix)), postfix: Of(nameof(CompleteRefiningPostfix)));
        harmony.Patch(_tryChargeScrap, prefix: Of(nameof(TryChargeScrapPrefix)));
        harmony.Patch(_tickPouring, prefix: Of(nameof(TickPouringPrefix)));
        harmony.Patch(_appendState, postfix: Of(nameof(AppendStructureStatePostfix)));
        harmony.Patch(_toTree, postfix: Of(nameof(ToTreePostfix)));
        harmony.Patch(_fromTree, postfix: Of(nameof(FromTreePostfix)));
        return harmony;
    }

    /// <summary>Lists ferrochrome in smex's live scrap setting.</summary>
    public static SmexScrap.Status EnsureScrap(ILogger logger) =>
        SmexScrap.EnsureListed(logger, Stainless.Ferrochrome, "Stainless steel", "ferrochrome");

    // ---- The control's state, read and written by reflection ----

    /// <summary>The heat's molten stack, or null.</summary>
    public static ItemStack? Content(object control) => (ItemStack?)_content!.GetValue(control);

    public static int ContentUnits(object control) => (int)_contentUnits!.GetValue(control)!;

    public static bool Solidified(object control) => (bool)_solidified!.GetValue(control)!;

    /// <summary>Cold scrap in the vessel, of <paramref name="code"/> or (null) of every kind, in units.</summary>
    public static int ScrapUnits(object control, string? code = null)
    {
        var scrap = (Dictionary<string, int>)_scrap!.GetValue(control)!;
        return code == null ? scrap.Values.Sum() : scrap.GetValueOrDefault(code);
    }

    public static int Capacity => (int)_capacity!.GetValue(null)!;

    public static int UnitsPerBit => Math.Max(1, (int)_unitsPerBit!.GetValue(null)!);

    /// <summary>Whether the heat is molten steel (or at least steel not yet set solid).</summary>
    public static bool IsSteel(object control) =>
        Content(control)?.Collectible?.Code?.ToString() == Steel && ContentUnits(control) > 0;

    /// <summary>Ferrochrome units melted into the heat: none unless the heat is steel.</summary>
    public static int Heat(object control)
    {
        if (!Heats.TryGetValue(control, out var box))
            return 0;
        if (box.Value > 0 && !IsSteel(control))
            box.Value = 0;
        return box.Value;
    }

    public static void SetHeat(object control, int units) => Heats.GetOrCreateValue(control).Value = Math.Max(0, units);

    /// <summary>Melts <paramref name="slot"/>'s ferrochrome into a molten steel heat, up to
    /// <paramref name="maxLumps"/>: takes it from the slot and books it in the heat. Server side;
    /// the caller has checked the converter can take it. Returns the lumps taken.</summary>
    public static int ChargeIntoHeat(object control, ItemSlot slot, int maxLumps)
    {
        int lumps = Math.Min(maxLumps, slot.StackSize);
        if (lumps <= 0)
            return 0;
        slot.TakeOut(lumps);
        slot.MarkDirty();
        int units = lumps * UnitsPerBit;
        _contentUnits!.SetValue(control, ContentUnits(control) + units);
        SetHeat(control, Heat(control) + units);
        _setStatus!.Invoke(control, [Lang.Get("smex:bessemer-status-scrap-charged")]);
        ((BlockEntity)control).MarkDirty(true);
        return lumps;
    }

    /// <summary>Stainless's melting point (°C) as the game has it (the pack patches 1530 in).</summary>
    public static float StainlessMeltingPoint(IWorldAccessor world) =>
        world.GetItem(new AssetLocation(Stainless.Ingot)) is { } item
            ? item.GetMeltingPoint(world, null!, new DummySlot(new ItemStack(item)))
            : float.MaxValue;

    /// <summary>What the heat will pour now, from its units, ferrochrome and temperature.</summary>
    public static BessemerHeat.Outcome Outcome(object control)
    {
        var world = ((BlockEntity)control).Api.World;
        var content = Content(control);
        int ferrochrome = Heat(control);
        if (content == null || ferrochrome <= 0)
            return BessemerHeat.Outcome.None;
        return BessemerHeat.Judge(ContentUnits(control), ferrochrome, content.Collectible.GetTemperature(world, content),
            StainlessMeltingPoint(world));
    }

    /// <summary>Decides the heat as it pours (see <see cref="BessemerHeat.Judge"/>): a stainless heat
    /// becomes the stainless ingot's metal at the same temperature; otherwise the ferrochrome goes to
    /// the slag and the heat stays steel. The heat's ferrochrome is then spent. Returns the outcome.</summary>
    public static BessemerHeat.Outcome Decide(object control)
    {
        if (Solidified(control))
            return BessemerHeat.Outcome.None;
        var outcome = Outcome(control);
        if (outcome == BessemerHeat.Outcome.None)
            return outcome;
        var world = ((BlockEntity)control).Api.World;
        var content = Content(control)!;
        int units = ContentUnits(control);
        if (outcome == BessemerHeat.Outcome.Stainless && world.GetItem(new AssetLocation(Stainless.Ingot)) is { } stainless)
        {
            _content!.SetValue(control, new ItemStack(stainless) { Attributes = (ITreeAttribute)content.Attributes.Clone() });
        }
        else
        {
            int left = BessemerHeat.UnitsPoured(units, Heat(control), outcome);
            _contentUnits!.SetValue(control, left);
            if (left <= 0)
                _content!.SetValue(control, null);
        }
        SetHeat(control, 0);
        _syncConverter!.Invoke(control, []);
        ((BlockEntity)control).MarkDirty(true);
        return outcome;
    }

    // ---- Patches ----

    private static void CompleteRefiningPrefix(object __instance, out int __state) =>
        __state = ScrapUnits(__instance, Stainless.Ferrochrome);

    private static void CompleteRefiningPostfix(object __instance, int __state)
    {
        // smex merges every scrap into the heat and clears the ledger when the steel is made.
        if (__state > 0 && IsSteel(__instance) && ScrapUnits(__instance, Stainless.Ferrochrome) == 0)
            SetHeat(__instance, Heat(__instance) + __state);
    }

    private static bool TryChargeScrapPrefix(object __instance, IPlayer byPlayer, ref string error, ref bool __result)
    {
        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack?.Collectible?.Code?.ToString() != Stainless.Ferrochrome || !IsSteel(__instance))
            return true;
        __result = true;
        object?[] args = [null];
        if (!(bool)_canOperate!.Invoke(__instance, args)!)
        {
            error = (string?)args[0] ?? "";
            return false;
        }
        if (Solidified(__instance))
        {
            error = Lang.Get("smex:bessemer-err-scrap-solidified");
            return false;
        }
        int room = (Capacity - ContentUnits(__instance) - ScrapUnits(__instance)) / UnitsPerBit;
        if (room <= 0)
        {
            error = Lang.Get("smex:bessemer-status-filling-full");
            return false;
        }
        error = "";
        if (((BlockEntity)__instance).Api.Side == EnumAppSide.Server)
            ChargeIntoHeat(__instance, slot, room);
        return false;
    }

    private static void TickPouringPrefix(object __instance) => Decide(__instance);

    private static void AppendStructureStatePostfix(object __instance, StringBuilder dsc)
    {
        if (((BlockEntity)__instance).Api?.World is not { } world)
            return;
        int melted = Heat(__instance);
        int cold = ScrapUnits(__instance, Stainless.Ferrochrome);
        if (melted + cold <= 0)
            return;
        int heat;
        int ferrochrome;
        BessemerHeat.Outcome outcome;
        if (melted > 0)
        {
            heat = ContentUnits(__instance);
            ferrochrome = melted;
            outcome = Outcome(__instance);
        }
        else
        {
            // Not blown yet: the ratio of everything in the vessel, the temperature still to come.
            heat = ContentUnits(__instance) + ScrapUnits(__instance);
            ferrochrome = cold;
            outcome = BessemerHeat.Judge(heat, ferrochrome, null, 0);
        }
        string verdict = outcome switch
        {
            BessemerHeat.Outcome.Stainless when melted == 0 =>
                Lang.Get("seraphhorizons:bessemer-ferrochrome-onratio", StainlessMeltingPoint(world).ToString("F0")),
            BessemerHeat.Outcome.Stainless => Lang.Get("seraphhorizons:bessemer-ferrochrome-stainless"),
            BessemerHeat.Outcome.TooCold => Lang.Get("seraphhorizons:bessemer-ferrochrome-toocold", StainlessMeltingPoint(world).ToString("F0")),
            _ => Lang.Get("seraphhorizons:bessemer-ferrochrome-offratio",
                (int)Math.Round(Stainless.FerrochromeMin * 100), (int)Math.Round(Stainless.FerrochromeMax * 100)),
        };
        dsc.AppendLine(Lang.Get("seraphhorizons:bessemer-info-ferrochrome", ferrochrome, BessemerHeat.SharePercent(heat, ferrochrome), verdict));
    }

    private static void ToTreePostfix(object __instance, ITreeAttribute tree)
    {
        int heat = Heat(__instance);
        if (heat > 0)
            tree.SetInt(HeatKey, heat);
        else
            tree.RemoveAttribute(HeatKey);
    }

    private static void FromTreePostfix(object __instance, ITreeAttribute tree) => SetHeat(__instance, tree.GetInt(HeatKey));

    private static FieldInfo? Field(Type? type, string name, Type fieldType) =>
        type == null ? null : AccessTools.DeclaredField(type, name) is { } f && f.FieldType == fieldType && !f.IsStatic ? f : null;

    private static MethodInfo? Method(Type? type, string name, Type returns, params Type[] parameters) =>
        type == null ? null
        : AccessTools.DeclaredMethod(type, name, parameters) is { } m && m.ReturnType == returns && !m.IsStatic ? m : null;
}
