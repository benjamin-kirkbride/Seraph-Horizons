using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Trading.Values;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// The pile line on handbook pages (README "Big piles"): under the page's general info, for every
/// item that goes on the ground, whether it piles, and for a pile how many one block holds, how
/// many a click and a Ctrl click move, whether a full pile can be built on (so the pile grows past
/// one block) and the keys that put it down (<see cref="PileText"/>). Read from the loaded
/// collectible on the client, after <see cref="BigPiles"/>. A pile is the game's <c>Stacking</c>
/// ground storage or an item pile (<c>ItemPileable</c>: coal, charcoal, coke, whose pile block's
/// entity says what it holds); a loose heap (<c>Messy12</c>) and the placed layouts say they are
/// not piles; an item that does not go on the ground gets no line, as the game's "Storable in/on"
/// section has none for it. Not in the item's tooltip: the tooltip is for the stack in hand and
/// already long, and the handbook is where the game says what an item is storable in or on.
///
/// A postfix on the game's private <c>CollectibleBehaviorHandbookTextAndExtraInfo.addGeneralInfo</c>
/// (the one the value line uses, <c>ValueHandbook</c>), after it (low priority), client side; not
/// switchable on its own: it says what the piles are, big or not.
/// </summary>
public static class PileHandbook
{
    public const string HarmonyId = "seraphhorizons.pilehandbook";

    private static ICoreClientAPI? _capi;
    private static readonly Dictionary<int, PileText.Facts?> ItemPiles = new();

    /// <summary>Client: adds the line to every page from now on. False, with a warning, when the
    /// game's method is not as expected.</summary>
    public static bool Patch(Harmony harmony, ICoreClientAPI capi)
    {
        var target = ValueHandbook.Target();
        if (target == null)
        {
            capi.Logger.Warning("[seraphhorizons] Pile handbook line: CollectibleBehaviorHandbookTextAndExtraInfo.addGeneralInfo is not as "
                                + "expected; the game changed, so handbook pages say nothing of piles");
            return false;
        }
        _capi = capi;
        ItemPiles.Clear();
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(PileHandbook), nameof(AddGeneralInfoPostfix)) { priority = Priority.Low });
        return true;
    }

    public static void Unbind()
    {
        _capi = null;
        ItemPiles.Clear();
    }

    private static void AddGeneralInfoPostfix(ItemStack stack, List<RichTextComponentBase> components)
    {
        var capi = _capi;
        if (capi == null || stack?.Collectible?.Code is not { } code)
            return;
        try
        {
            if (Facts(capi, stack.Collectible) is not { } facts) return;
            components.Add(new ClearFloatTextComponent(capi, 4f));
            components.AddRange(VtmlUtil.Richtextify(capi, Escape(PileText.Line(facts, Lang.Get)) + "\n", CairoFont.WhiteSmallText()));
        }
        catch (Exception e)
        {
            capi.Logger.Error("[seraphhorizons] Pile handbook line: could not add it to {0}'s page: {1}", code, e);
        }
    }

    /// <summary>How <paramref name="collectible"/> goes on the ground, or null when it does not.</summary>
    public static PileText.Facts? Facts(ICoreAPI api, CollectibleObject collectible)
    {
        if (collectible.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps is { } props)
        {
            return props.Layout switch
            {
                EnumGroundStorageLayout.Stacking => new PileText.Facts(PileText.Kind.Pile, Math.Max(1, props.StackingCapacity),
                    Math.Max(1, props.TransferQuantity), Math.Max(1, props.BulkTransferQuantity), props.CtrlKey, props.UpSolid,
                    props.UpSolid && props.MaxStackingHeight > 0 ? props.MaxStackingHeight : 0),
                EnumGroundStorageLayout.Messy12 => new PileText.Facts(PileText.Kind.Loose, 12, 0, 0, props.CtrlKey, false),
                EnumGroundStorageLayout.SingleCenter => new PileText.Facts(PileText.Kind.Placed, 1, 0, 0, props.CtrlKey, false),
                EnumGroundStorageLayout.Quadrants => new PileText.Facts(PileText.Kind.Placed, 4, 0, 0, props.CtrlKey, false),
                _ => new PileText.Facts(PileText.Kind.Placed, 2, 0, 0, props.CtrlKey, false),
            };
        }
        if (collectible is ItemPileable pileable && pileable.IsPileable)
        {
            lock (ItemPiles)
            {
                if (!ItemPiles.TryGetValue(collectible.Id, out var facts))
                    ItemPiles[collectible.Id] = facts = ItemPileFacts(api, pileable);
                return facts;
            }
        }
        return null;
    }

    private static readonly PropertyInfo? PileBlockCode = AccessTools.Property(typeof(ItemPileable), "PileBlockCode");

    /// <summary>An item pile's facts, from its pile block's entity: shift + right click puts it down,
    /// a full pile takes the next one on top.</summary>
    public static PileText.Facts? ItemPileFacts(ICoreAPI api, ItemPileable item)
    {
        if (PileBlockCode?.GetValue(item) is not AssetLocation code
            || api.World.GetBlock(code) is not { EntityClass: { } entityClass }
            || api.ClassRegistry.GetBlockEntity(entityClass) is not { } type
            || !typeof(BlockEntityItemPile).IsAssignableFrom(type)
            || Activator.CreateInstance(type) is not BlockEntityItemPile pile)
            return null;
        return new PileText.Facts(PileText.Kind.Pile, pile.MaxStackSize, Math.Max(1, pile.DefaultTakeQuantity),
            Math.Max(1, pile.BulkTakeQuantity), false, true);
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
