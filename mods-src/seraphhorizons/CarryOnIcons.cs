using System.Reflection;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Carry On (<c>carryon</c> 2.0.0-pre.8): its interaction help icons are built again in every world
/// of a client run (#401, upstream NerdScurvy/CarryOn#87).
///
/// Carry On builds the item stacks its interaction help shows (<c>carryon:icon-handsfree</c>,
/// <c>-nohandsfree</c>, <c>-put</c>, <c>-take</c>) the first time it needs them and keeps them in
/// static fields (<see cref="Fields"/>), which nothing ever clears. A client that leaves a world and
/// opens another keeps the first world's <c>Item</c> objects there; when the two worlds number their
/// items differently, the HUD's interaction help renders one of them, indexes past the end of the
/// new world's item model array and the client crashes (<c>IndexOutOfRangeException</c> in
/// <c>InventoryItemRenderer.GetItemStackRenderInfo</c>). <c>EntityCarriedBlock</c> builds its pair
/// again when a stack's <c>Item</c> is null, which a stale stack's never is.
///
/// Each of those fields is filled only while it is null, so setting them all back to null is all it
/// takes: the next world's first interaction help builds them from that world's items. They are
/// cleared when the client's instance of <see cref="SeraphHorizonsSystem"/> is disposed, which is
/// when the client leaves a world (it also drops the old world's items, which hold that world's
/// API), and once more when the client side starts, before the new world draws anything, in case
/// the last world ended without disposing its mods. That is a world-leave hook, not a Harmony patch:
/// nothing in Carry On's code has to change, only the state it keeps between worlds. The server
/// never builds these stacks (interaction help is the client's), so its instance leaves them alone.
///
/// Carry On is not referenced at build time: its types are found in the assembly of its mod system
/// (<c>CarryOn.CarrySystem</c>). A field that is not there, or not a static <c>ItemStack[]</c>, is
/// logged as a warning and left out; the others are still cleared.
/// </summary>
public static class CarryOnIcons
{
    public const string CarrySystemName = "CarryOn.CarrySystem";

    /// <summary>Carry On's static icon stack fields, by declaring type: every static field in its
    /// assembly that holds an item stack (the Atlas scenario holds the assembly to that).</summary>
    public static readonly (string Type, string Field)[] Fields =
    [
        ("CarryOn.Common.Logic.CarryableInteractionHelpBuilder", "handsfreeStacks"),
        ("CarryOn.Common.Logic.CarryableInteractionHelpBuilder", "nohandsfreeStacks"),
        ("CarryOn.Common.Entities.EntityCarriedBlock", "handsfreeStacks"),
        ("CarryOn.Common.Entities.EntityCarriedBlock", "nohandsfreeStacks"),
        ("CarryOn.Common.Behaviors.EntityBehaviorAttachableCarryable", "putStacks"),
        ("CarryOn.Common.Behaviors.EntityBehaviorAttachableCarryable", "takeStacks"),
        ("CarryOn.Common.Behaviors.EntityBehaviorAttachableCarryable", "nohandsfreeStacks"),
    ];

    /// <summary>Carry On's assembly, from its loaded mod system; null without Carry On.</summary>
    public static Assembly? CarryOnAssembly(ICoreAPI api) =>
        api.ModLoader.GetModSystem(CarrySystemName)?.GetType().Assembly;

    /// <summary>The <see cref="Fields"/> that are there as expected; null if Carry On is not loaded.
    /// Logs a warning for each that is not.</summary>
    public static List<FieldInfo>? Find(ICoreAPI api)
    {
        if (CarryOnAssembly(api) is not { } assembly)
            return null;
        var found = new List<FieldInfo>();
        var missing = new List<string>();
        foreach (var (type, name) in Fields)
        {
            var field = assembly.GetType(type)?.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(ItemStack[]) && !field.IsInitOnly && !field.IsLiteral)
                found.Add(field);
            else
                missing.Add($"{type}.{name}");
        }
        if (missing.Count > 0)
            api.Logger.Warning("[seraphhorizons] Carry On icons: {0} {1} not a static ItemStack[] field; Carry On changed, so "
                               + "{2} not built again per world", string.Join(", ", missing), missing.Count == 1 ? "is" : "are",
                               missing.Count == 1 ? "it is" : "they are");
        return found;
    }

    /// <summary>Sets every field in <paramref name="fields"/> back to null, so Carry On builds its
    /// stacks again from the next world's items. Returns how many held stacks.</summary>
    public static int Clear(IEnumerable<FieldInfo> fields)
    {
        int cleared = 0;
        foreach (var field in fields)
        {
            if (field.GetValue(null) != null)
                cleared++;
            field.SetValue(null, null);
        }
        return cleared;
    }
}
