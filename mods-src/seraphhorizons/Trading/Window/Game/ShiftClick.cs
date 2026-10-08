using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>
/// Shift-click while the trade window is open (Harmony id <see cref="HarmonyId"/>, both sides: the
/// client predicts the move, the server makes it from the click's packet).
///
/// <para>The playtest lost a delivery package and a stack of gears this way. A shift-click runs
/// <c>PlayerInventoryManager.TryTransferAway</c>, which offers the stack to every inventory the
/// player has open and puts it in the best-weighted slot. The trader's inventory took neither money
/// nor goods it does not buy, and in creative mode the creative inventory is always open
/// (<c>InventoryPlayerCreative.HasOpened</c> is true for a creative player) and offers its black hole
/// slot (weight 0.01), which deletes whatever goes in: with no room in the hotbar or backpack, the
/// stack went there. In survival nothing was lost (the stack stayed or moved between hotbar and
/// backpack).</para>
///
/// <list type="bullet">
/// <item><c>PlayerInventoryManager.TryTransferAway</c> (prefix): a shift-click on a stack in the
/// hotbar or backpack while the player has a pack trader's window open moves the whole stack into
/// the first free sell slot (<see cref="SeraphTraderInventory.ShiftIn"/>), or nothing moves: money
/// and packages never, nor anything with the sell slots full. Shift-clicking out of a sell slot
/// runs the game's own transfer, kept out of the black hole.</item>
/// <item><c>InventoryPlayerCreative.GetBestSuitedSlot</c> (prefix): no black hole while the window
/// moves goods back to the player (<see cref="KeepOutOfBlackHole"/>): out of a sell slot, a sale's
/// gears, and the sell slots given back on closing, the rest at the player's feet.</item>
/// </list>
/// </summary>
public static class ShiftClick
{
    public const string HarmonyId = "seraphhorizons.tradewindow.shiftclick";

    private static readonly object s_lock = new();
    private static int s_users;
    private static Harmony? s_harmony;

    [ThreadStatic] private static int t_noBlackHole;

    /// <summary>Patches once per process (a single player game runs both sides in one).</summary>
    public static void Patch(ICoreAPI api)
    {
        lock (s_lock)
        {
            if (s_users++ > 0) return;
            s_harmony = new Harmony(HarmonyId);
            var transfer = AccessTools.Method(typeof(PlayerInventoryManager), nameof(PlayerInventoryManager.TryTransferAway),
                [typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType(), typeof(bool), typeof(System.Text.StringBuilder), typeof(bool)]);
            var blackHole = AccessTools.Method(typeof(InventoryPlayerCreative), nameof(InventoryPlayerCreative.GetBestSuitedSlot),
                [typeof(ItemSlot), typeof(ItemStackMoveOperation), typeof(List<ItemSlot>)]);
            if (transfer is null || blackHole is null)
            {
                api.Logger.Warning("[seraphhorizons] Trade window: PlayerInventoryManager.TryTransferAway or InventoryPlayerCreative.GetBestSuitedSlot not found; shift-click keeps the game's behaviour");
                return;
            }
            s_harmony.Patch(transfer, prefix: new HarmonyMethod(typeof(ShiftClick), nameof(TransferPrefix)));
            s_harmony.Patch(blackHole, prefix: new HarmonyMethod(typeof(ShiftClick), nameof(BlackHolePrefix)));
        }
    }

    public static void Unpatch()
    {
        lock (s_lock)
        {
            if (s_users == 0 || --s_users > 0) return;
            s_harmony?.UnpatchAll(HarmonyId);
            s_harmony = null;
        }
    }

    /// <summary>While the returned scope lives, the creative inventory's black hole takes nothing on
    /// this thread.</summary>
    public static IDisposable KeepOutOfBlackHole()
    {
        t_noBlackHole++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            t_noBlackHole--;
        }
    }

    public static bool TransferPrefix(ItemSlot sourceSlot, ref ItemStackMoveOperation op, bool onlyPlayerInventory, ref object[]? __result)
    {
        if (onlyPlayerInventory || op is not { ShiftDown: true } || sourceSlot?.Itemstack is null) return true;
        string? cls = sourceSlot.Inventory?.ClassName;
        if (cls is not (GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)) return true;
        if (SeraphTraderInventory.OpenFor(op.ActingPlayer) is not { } trade) return true;
        trade.ShiftIn(sourceSlot, ref op);
        // The packets the game would build here are dropped by its caller (InventoryBase.ActivateSlot
        // sends the click itself), so none are needed.
        __result = null;
        return false;
    }

    public static bool BlackHolePrefix(ref WeightedSlot __result)
    {
        if (t_noBlackHole <= 0) return true;
        __result = new WeightedSlot();
        return false;
    }
}
