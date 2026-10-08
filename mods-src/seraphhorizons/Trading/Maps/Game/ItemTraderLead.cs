using System.Text;
using Vintagestory.API.Client;
using SeraphHorizons.Mod.Trading.Maps.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// A lead to another trader camp (#455; item <c>seraphhorizons:traderlead</c>), like an ore map:
/// what it knows is in the stack's attributes (<see cref="MapOfferAttrs"/>: kind, camp type, x, y,
/// z), written when a trader sells it; right-click puts a pinned waypoint (icon <c>trader</c>,
/// titled with the camp's type and "approximate": a lead marks the camp's site, not its trader) on
/// the reader's map through <see cref="MapMarksSystem"/>, once per camp, and keeps the lead. A lead
/// from the creative inventory is blank; offers and leads being drawn read as such
/// (<see cref="MapTradeHooks"/>).
/// </summary>
public class ItemTraderLead : Item, ITradeableCollectible
{
    public const string ClassName = "seraphhorizons.TraderLead";
    public static readonly AssetLocation LeadCode = new("seraphhorizons", "traderlead");

    public bool ShouldTrade(EntityTradingHumanoid trader, TradeItem tradeItem, EnumTradeDirection direction) =>
        MapTradeHooks.Instance.ShouldTrade(trader, tradeItem, direction);

    public EnumTransactionResult OnTryTrade(EntityTradingHumanoid trader, ItemSlot tradeSlot, EnumTradeDirection direction) =>
        MapTradeHooks.Instance.OnTryTrade(trader, tradeSlot, direction);

    public bool OnDidTrade(EntityTradingHumanoid trader, ItemStack stack, EnumTradeDirection direction) =>
        MapTradeHooks.Instance.OnDidTrade(trader, stack, direction);

    /// <summary>Whether the stack is a finished lead (has a place).</summary>
    public static bool IsDrawn(ItemStack stack) =>
        stack.Attributes.HasAttribute(MapOfferAttrs.X) && !stack.Attributes.HasAttribute(MapOfferAttrs.Offer)
                                                       && !stack.Attributes.HasAttribute(MapOfferAttrs.Pending);

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
        bool firstEvent, ref EnumHandHandling handling)
    {
        if (slot.Empty) return;
        handling = EnumHandHandling.PreventDefault;
        if (!firstEvent || (byEntity as EntityPlayer)?.Player is not IServerPlayer player) return;
        string lang = player.LanguageCode ?? Lang.DefaultLocale;
        var stack = slot.Itemstack;
        if (!IsDrawn(stack))
        {
            string key = stack.Attributes.HasAttribute(MapOfferAttrs.Pending) ? "trading-maps-lead-pending" : "trading-maps-lead-blank";
            player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:" + key), EnumChatType.Notification);
            return;
        }
        var a = stack.Attributes;
        int y = a.GetInt(MapOfferAttrs.Y);
        var pos = new Vec3d(a.GetInt(MapOfferAttrs.X) + 0.5, y > 0 ? y + 0.5 : api.World.SeaLevel, a.GetInt(MapOfferAttrs.Z) + 0.5);
        string title = MapMarksSystem.Title(lang, Title(lang, stack), MapMarks.LeadPrecision, lead: true);
        var outcome = MapMarksSystem.Of(api) is { } marks && MapMarksSystem.TargetOf(stack) is { } target
            ? marks.Mark(player, target, pos, title, "trader", MapMarksSystem.TraderColor)
            : MapMarksSystem.Outcome.NoMap;
        switch (outcome)
        {
            case MapMarksSystem.Outcome.NoMap:
                var d = pos.Clone().Sub(byEntity.Pos.XYZ);
                d.Y = 0;
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:trading-maps-lead-distance", title, (int)d.Length()),
                    EnumChatType.Notification);
                return;
            case MapMarksSystem.Outcome.Already:
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:oremap-marked-already"), EnumChatType.Notification);
                return;
        }
        player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:trading-maps-lead-marked", title), EnumChatType.Notification);
    }

    /// <summary>"Smith's camp", "Settlement ground".</summary>
    public static string Title(string lang, ItemStack stack)
    {
        var a = stack.Attributes;
        if (LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind) && kind == LeadKind.Settlement)
            return Lang.GetL(lang, "seraphhorizons:trading-maps-lead-title-settlement");
        string type = a.GetString(MapOfferAttrs.Type) ?? "";
        return Lang.GetL(lang, "seraphhorizons:trading-maps-lead-title", Lang.GetL(lang, "seraphhorizons:trading-type-" + type));
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        if (MapTradeHooks.Instance.HeldName(itemStack) is { } offer) return offer;
        if (!IsDrawn(itemStack)) return base.GetHeldItemName(itemStack);
        return Lang.Get("seraphhorizons:trading-maps-lead-name", Title(Lang.CurrentLocale, itemStack));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is not { } stack) return;
        if (MapTradeHooks.Instance.HeldInfo(stack, dsc)) return;
        if (!IsDrawn(stack))
        {
            dsc.AppendLine(Lang.Get("seraphhorizons:trading-maps-lead-info-blank"));
            return;
        }
        dsc.AppendLine(Lang.Get("seraphhorizons:trading-maps-lead-info", Title(Lang.CurrentLocale, stack)));
        dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-use"));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
    [
        new WorldInteraction { ActionLangCode = "seraphhorizons:oremap-help-read", MouseButton = EnumMouseButton.Right },
        .. base.GetHeldInteractionHelp(inSlot),
    ];
}
