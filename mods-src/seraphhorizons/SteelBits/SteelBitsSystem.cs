using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.SteelBits;

/// <summary>
/// Steel bits recovery (<c>SteelBitsRecovery</c>, #478, README "Steel bits back into steel"). A steel
/// bit (<c>game:metalbit-steel</c>) melts at 1502 °C, above every fuel the game has (coke burns at
/// 1340 °C), so it is inert in the game. Two ways back into steel:
/// <list type="number">
/// <item>The cementation furnace, the game's stone coffin. What it takes as an ingot is data: any
/// item whose attributes carry <c>carburizableProps</c>, turned into its <c>carburizedOutput</c>
/// one for one when the furnace completes, and it fires only with all 16 ingot places filled. This
/// mod adds an item that has it, <c>seraphhorizons:steelbitcharge</c> (packed steel bits, out
/// <c>game:ingot-blistersteel</c>), made from 20 bits in the crafting grid; and one prefix and
/// postfix on the coffin's private <c>AddIngot(ItemSlot)</c> let a player put the bits in
/// directly: holding 20 or more, the prefix hands the game's own method one packed charge in
/// place of the held slot, and the postfix takes the 20 bits only if the game put the charge in.
/// The game does every check and message; with fewer than 20 the prefix refuses with its own.</item>
/// <item>Steelmaking Expanded's Bessemer converter, which takes cold scrap with its molten iron
/// charge, by its <c>BessemerScrapCodes</c> setting (5 units a bit, a steel ingot being 100).
/// 0.10.1 lists the steel bit by default; if a server's file does not, it is added to the live
/// setting (not written back), which exlib sends on to clients. See <see cref="SmexScrap"/>.</item>
/// </list>
/// The coffin is patched on both sides (the client predicts the click), once per process (own
/// Harmony id: a singleplayer game runs both sides in one). With the switch off nothing is patched,
/// the recipe and the bit's handbook section are left out, and smex's setting is untouched; the
/// packed item stays in the game, so charges already made are never lost and still go into a coffin
/// by their own attribute.
/// </summary>
public class SteelBitsSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.steelbits";
    public const string SmexModId = "smex";

    public static readonly AssetLocation SteelBit = new("game", "metalbit-steel");
    public static readonly AssetLocation ChargeCode = new("seraphhorizons", "steelbitcharge");
    public static readonly AssetLocation HandbookPatch = new("seraphhorizons", "patches/steelbits-handbook.json");
    public static readonly AssetLocation RecipeAsset = new("seraphhorizons", "recipes/grid/steelbitcharge.json");

    private Harmony? _harmony;

    /// <summary>The patch target, <c>BlockEntityStoneCoffin.AddIngot(ItemSlot)</c>; null when the
    /// game no longer has it (or the switch is off).</summary>
    public static MethodInfo? AddIngot { get; private set; }

    /// <summary>What <see cref="SmexScrap.Ensure"/> found on this side.</summary>
    public SmexScrap.Status SmexStatus { get; private set; } = SmexScrap.Status.NotChecked;

    public static SteelBitsSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<SteelBitsSystem>();

    public static bool On(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).SteelBitsRecovery;

    public override void Start(ICoreAPI api)
    {
        if (!On(api))
        {
            // Before the game's patch loader (AssetsLoaded) and recipe loader (later still).
            Disable(api);
            return;
        }
        // Once per process: in singleplayer the other side's system may have patched already.
        if (Harmony.HasAnyPatches(HarmonyId))
            return;
        var target = AccessTools.DeclaredMethod(typeof(BlockEntityStoneCoffin), "AddIngot", [typeof(ItemSlot)]);
        if (target?.ReturnType != typeof(bool))
        {
            api.Logger.Warning("[seraphhorizons] Steel bits recovery: BlockEntityStoneCoffin.AddIngot(ItemSlot) is not as expected; "
                               + "the game changed, so steel bits go into the stone coffin only as packed steel bits");
            return;
        }
        AddIngot = target;
        _harmony = new Harmony(HarmonyId);
        _harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(SteelBitsSystem), nameof(AddIngotPrefix)),
            postfix: new HarmonyMethod(typeof(SteelBitsSystem), nameof(AddIngotPostfix)));
    }

    // After every mod's Start, where smex loads its setting.
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (!On(api))
            SmexStatus = SmexScrap.Status.Off;
        else if (!api.ModLoader.IsModEnabled(SmexModId))
            SmexStatus = SmexScrap.Status.Absent;
        else
            SmexStatus = SmexScrap.Ensure(api.Logger);
    }

    public override void Dispose()
    {
        if (_harmony == null)
            return;
        _harmony.UnpatchAll(HarmonyId);
        _harmony = null;
        AddIngot = null;
    }

    /// <summary>Leaves the recipe and the steel bit's handbook section out of the game.</summary>
    public static void Disable(ICoreAPI api)
    {
        if (api.Assets.TryGet(HandbookPatch) is { } patch)
            patch.Data = "[]"u8.ToArray();
        if (api.Assets.TryGet(RecipeAsset) is { } recipes)
        {
            var json = JArray.Parse(recipes.ToText());
            foreach (var recipe in json.OfType<JObject>())
                recipe["enabled"] = false;
            recipes.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    // Holding steel bits: hand the game one packed charge in their place, so the coffin's own
    // checks (full, mixed, the move into its slot, the redraw) run unchanged. __state is the held
    // slot, for the postfix to take the bits from.
    private static bool AddIngotPrefix(BlockEntityStoneCoffin __instance, ref ItemSlot slot, ref bool __result, out ItemSlot? __state)
    {
        __state = null;
        if (slot?.Itemstack?.Collectible?.Code is not { } code || !code.Equals(SteelBit))
            return true;
        var world = __instance.Api.World;
        var charge = world.GetItem(ChargeCode);
        if (charge == null)
            return true;
        if (slot.StackSize < SteelBitsRules.BitsPerCharge)
        {
            (__instance.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughbits",
                Lang.Get("seraphhorizons:steelbits-error-few", SteelBitsRules.BitsPerCharge));
            __result = false;
            return false;
        }
        __state = slot;
        slot = new DummySlot(new ItemStack(charge));
        return true;
    }

    private static void AddIngotPostfix(bool __result, ItemSlot? __state)
    {
        if (__state == null || !__result)
            return;
        __state.TakeOut(SteelBitsRules.BitsPerCharge);
        __state.MarkDirty();
    }
}
