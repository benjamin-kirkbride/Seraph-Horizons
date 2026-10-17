using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// The game's dialogue asks for a stack in one slot: a <c>player.inventory</c> condition and the
/// <c>takefrominventory</c> trigger both go through <c>DialogueComponent.FindDesiredItem(EntityAgent,
/// ItemStack)</c>, which wants one slot holding the whole stack. With the rusty gear stacking to 64
/// (<see cref="RustyGearStack"/>) the treasure hunter's elk, 100 gears, could never be bought. A
/// postfix on it, only where it found no slot: when the matching slots together hold the stack, it
/// returns a slot spanning them (<see cref="SpanningSlot"/>), which the condition takes as found and
/// from which the trigger's <c>TakeOut</c> takes across them, exactly the quantity asked
/// (<see cref="SpanningTake"/>). Every dialogue benefits; the dialogue files are the game's.
/// Patched once per process; acts while <see cref="SeraphHorizonsConfig.RustyGearStack"/>, the
/// cause, is on (as the server has it).
/// </summary>
public static class DialogueStacks
{
    public const string HarmonyId = "seraphhorizons.dialoguestacks";

    private static readonly MethodInfo? Target =
        AccessTools.DeclaredMethod(typeof(DialogueComponent), nameof(DialogueComponent.FindDesiredItem), [typeof(EntityAgent), typeof(ItemStack)]);
    // The game's own match (equal or satisfying, fresh enough, big enough), asked with a stack of one.
    private static readonly MethodInfo? Matches = AccessTools.Method(typeof(DialogueComponent), "matches");
    private static readonly MethodInfo? IgnoreAttrs = AccessTools.Method(typeof(DialogueComponent), "getIgnoreAttrs");
    private static bool _patched;

    public static void Patch(ICoreAPI api)
    {
        lock (typeof(DialogueStacks))
        {
            if (_patched) return;
            if (Target is null || Matches is null || IgnoreAttrs is null)
            {
                api.Logger.Warning("[seraphhorizons] Dialogue stacks: DialogueComponent.FindDesiredItem/matches/getIgnoreAttrs not found; the game "
                                   + "changed, so dialogue still wants a whole stack in one slot (no elk for 100 rusty gears)");
                return;
            }
            new Harmony(HarmonyId).Patch(Target, postfix: new HarmonyMethod(typeof(DialogueStacks), nameof(FindDesiredItemPostfix)));
            _patched = true;
        }
    }

    private static void FindDesiredItemPostfix(EntityAgent eagent, ItemStack wantStack, ref ItemSlot __result)
    {
        if (__result != null || eagent?.Api is not { } api || wantStack is null || wantStack.StackSize <= 1) return;
        try
        {
            if (!StorageTweaksSystem.ServerSwitch(api, nameof(SeraphHorizonsConfig.RustyGearStack), SeraphHorizonsSystem.ConfigFor(api).RustyGearStack))
                return;
            __result = Spanning(eagent, wantStack)!;
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Dialogue stacks: {0}", e);
        }
    }

    /// <summary>A slot spanning the agent's slots that match <paramref name="want"/> but for its size,
    /// when together they hold it; else null.</summary>
    public static SpanningSlot? Spanning(EntityAgent agent, ItemStack want)
    {
        var one = want.Clone();
        one.StackSize = 1;
        var ignored = (string[])IgnoreAttrs!.Invoke(null, null)!;
        var slots = new List<ItemSlot>();
        agent.WalkInventory(slot =>
        {
            if (!slot.Empty && (bool)Matches!.Invoke(null, [agent, one, slot, ignored])!) slots.Add(slot);
            return true;
        });
        if (SpanningTake.Plan(slots.Select(s => s.StackSize).ToList(), want.StackSize) is null) return null;
        return new SpanningSlot(slots, want.StackSize);
    }
}

/// <summary>Several slots of one good as one slot: its stack is their total (as asked, so the
/// dialogue's condition is met); <see cref="TakeOut"/> takes from them in order.</summary>
public sealed class SpanningSlot : DummySlot
{
    private readonly List<ItemSlot> _slots;

    public SpanningSlot(List<ItemSlot> slots, int total) : base(Total(slots, total))
    {
        _slots = slots;
    }

    private static ItemStack Total(List<ItemSlot> slots, int total)
    {
        var stack = slots[0].Itemstack.Clone();
        stack.StackSize = total;
        return stack;
    }

    public override ItemStack? TakeOut(int quantity)
    {
        var plan = SpanningTake.Plan(_slots.Select(s => s.StackSize).ToList(), quantity);
        if (plan is null)
        {
            // One slot holds it after all (or not enough): the game's single-slot take.
            var single = _slots.FirstOrDefault(s => s.StackSize >= quantity);
            return single?.TakeOut(quantity);
        }
        ItemStack? taken = null;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (plan[i] <= 0) continue;
            var part = _slots[i].TakeOut(plan[i]);
            if (part is null) continue;
            if (taken is null) taken = part;
            else taken.StackSize += part.StackSize;
        }
        Itemstack = null;
        return taken;
    }

    public override void MarkDirty()
    {
        foreach (var slot in _slots) slot.MarkDirty();
    }
}
