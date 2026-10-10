using System.Text;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// The game's trade hooks for ore maps, gravel maps and leads (#455), shared by both item classes
/// (<see cref="ItemOreMap.Hooks"/>, <see cref="ItemTraderLead"/>), and the names of offer and
/// pending stacks. Stateless: a trade is routed to the server's <see cref="MapsSystem"/> through the
/// trader's own API, so one instance serves both sides of a singleplayer game. Only offers are
/// touched; a real map or lead trades like any item (traders refuse to buy them anyway).
/// </summary>
public sealed class MapTradeHooks : ItemOreMap.IHooks
{
    public static readonly MapTradeHooks Instance = new();

    private static MapsSystem? Sales(EntityTradingHumanoid trader) =>
        trader.Api.Side == EnumAppSide.Server ? trader.Api.ModLoader.GetModSystem<MapsSystem>() : null;

    public bool ShouldTrade(EntityTradingHumanoid trader, TradeItem tradeItem, EnumTradeDirection direction) => true;

    public EnumTransactionResult OnTryTrade(EntityTradingHumanoid trader, ItemSlot tradeSlot, EnumTradeDirection direction)
    {
        if (tradeSlot.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) is null) return EnumTransactionResult.Success;
        if (trader is not EntitySeraphTrader ours || Sales(trader) is not { Active: true } sales) return EnumTransactionResult.Failure;
        return sales.OnTryBuy(ours, tradeSlot);
    }

    public bool OnDidTrade(EntityTradingHumanoid trader, ItemStack stack, EnumTradeDirection direction)
    {
        if (stack.Attributes.GetString(MapOfferAttrs.Offer) is null) return true;
        if (trader is EntitySeraphTrader ours && Sales(trader) is { Active: true } sales) sales.OnBought(ours, stack);
        return true;
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    private static string What(ItemStack stack)
    {
        var a = stack.Attributes;
        return a.GetString(MapOfferAttrs.Offer) switch
        {
            MapOfferAttrs.OreMap => L("oremap-name", ItemOreMap.DepositName(Lang.CurrentLocale, a)),
            MapOfferAttrs.GravelMap => a.GetString(MapOfferAttrs.Rock) is { } rock
                ? L("gravelmap-name", ItemOreMap.RockName(Lang.CurrentLocale, rock))
                : L("item-gravelmap"),
            MapOfferAttrs.Lead => L("trading-maps-lead-name", ItemTraderLead.Title(Lang.CurrentLocale, stack)),
            _ => "",
        };
    }

    public string? HeldName(ItemStack stack)
    {
        var a = stack.Attributes;
        string? offer = a.GetString(MapOfferAttrs.Offer);
        if (offer is null) return null;
        if (offer == MapOfferAttrs.SoldOut)
            return L(stack.Collectible.Code == MapIssuer.GravelMapCode ? "trading-maps-soldout-gravel" : "trading-maps-soldout-ore");
        if (offer == MapOfferAttrs.Surveying)
            return stack.Collectible.Code == MapIssuer.GravelMapCode
                ? L("trading-maps-surveying-gravel")
                : L("trading-maps-surveying-ore", ItemOreMap.MetalName(Lang.CurrentLocale, a.GetString(MapOfferAttrs.Metal) ?? ""));
        return a.HasAttribute(MapOfferAttrs.Pending) ? L("trading-maps-pending-name", What(stack)) : What(stack);
    }

    public bool HeldInfo(ItemStack stack, StringBuilder dsc)
    {
        var a = stack.Attributes;
        string? offer = a.GetString(MapOfferAttrs.Offer);
        if (offer is null) return false;
        if (a.HasAttribute(MapOfferAttrs.Pending))
        {
            dsc.AppendLine(L("trading-maps-pending-info"));
            return true;
        }
        switch (offer)
        {
            case MapOfferAttrs.SoldOut:
                dsc.AppendLine(L("trading-maps-soldout-info"));
                break;
            case MapOfferAttrs.Surveying:
                dsc.AppendLine(L("trading-maps-surveying-info"));
                break;
            case MapOfferAttrs.OreMap:
                dsc.AppendLine(L("oremap-info-precision-" + a.GetInt(MapOfferAttrs.Precision, MapPrecision.Rough)));
                ItemOreMap.AppendDeposit(Lang.CurrentLocale, a, dsc);
                dsc.AppendLine(L("trading-maps-offer-ore-info"));
                break;
            case MapOfferAttrs.GravelMap:
                ItemOreMap.AppendDeposit(Lang.CurrentLocale, a, dsc);
                dsc.AppendLine(L("trading-maps-offer-gravel-info"));
                break;
            case MapOfferAttrs.Lead:
                dsc.AppendLine(L(LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind) && kind != LeadKind.Camp
                    ? "trading-maps-offer-lead-standing" : "trading-maps-offer-lead-info"));
                break;
        }
        return true;
    }
}
