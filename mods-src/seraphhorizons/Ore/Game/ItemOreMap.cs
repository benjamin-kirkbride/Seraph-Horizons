using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// An ore or gravel map (#444; items <c>seraphhorizons:oremap</c> and <c>seraphhorizons:gravelmap</c>),
/// modelled on the game's <c>ItemLocatorMap</c>: everything it knows is in the stack's attributes,
/// written by <see cref="MapIssuer"/>, and using it (right-click) puts a waypoint on the reader's
/// map and keeps the map, so it can be read again, lent or traded. The marker position is stored
/// already offset by the map's precision (<see cref="MapPrecision"/>), so every copy marks the same
/// place. A map without attributes (from the creative inventory) is blank.
/// </summary>
public class ItemOreMap : Item, ITradeableCollectible
{
    /// <summary>What the trading side (#455, <c>Trading/Maps/</c>) adds: a trader's map offers and
    /// sales (the game's ITradeableCollectible), and the names of offer and pending stacks. Unset,
    /// a map trades like any item and reads as above.</summary>
    public interface IHooks : ITradeableCollectible
    {
        /// <summary>The name of an offer or pending stack, else null.</summary>
        string? HeldName(ItemStack stack);

        /// <summary>Describes an offer or pending stack and returns true, else false.</summary>
        bool HeldInfo(ItemStack stack, StringBuilder dsc);
    }

    public static IHooks? Hooks { get; set; }

    public bool ShouldTrade(EntityTradingHumanoid trader, TradeItem tradeItem, EnumTradeDirection direction) =>
        Hooks?.ShouldTrade(trader, tradeItem, direction) ?? true;

    public EnumTransactionResult OnTryTrade(EntityTradingHumanoid trader, ItemSlot tradeSlot, EnumTradeDirection direction) =>
        Hooks?.OnTryTrade(trader, tradeSlot, direction) ?? EnumTransactionResult.Success;

    public bool OnDidTrade(EntityTradingHumanoid trader, ItemStack stack, EnumTradeDirection direction) =>
        Hooks?.OnDidTrade(trader, stack, direction) ?? true;

    public const string ClassName = "seraphhorizons.OreMap";

    public const string AttrDeposit = "depositId";
    public const string AttrMetal = "metal";
    public const string AttrPrecision = "precision";
    public const string AttrSizeTier = "sizeTier";
    public const string AttrRock = "rock";
    public const string AttrX = "x";
    public const string AttrY = "y";
    public const string AttrZ = "z";

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
        bool firstEvent, ref EnumHandHandling handling)
    {
        if (slot.Empty) return;
        handling = EnumHandHandling.PreventDefault;
        if (!firstEvent || (byEntity as EntityPlayer)?.Player is not IServerPlayer player) return;
        string lang = player.LanguageCode ?? Lang.DefaultLocale;
        var a = slot.Itemstack.Attributes;
        if (!a.HasAttribute(AttrX) || a.GetString(AttrMetal) is not { } metal)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:oremap-blank"), EnumChatType.Notification);
            return;
        }
        int y = a.GetInt(AttrY);
        var pos = new Vec3d(a.GetInt(AttrX) + 0.5, y > 0 ? y + 0.5 : api.World.SeaLevel, a.GetInt(AttrZ) + 0.5);
        bool gravel = metal == PlacerCells.Kind;
        int precision = gravel ? MapPrecision.Exact : a.GetInt(AttrPrecision, MapPrecision.Exact);
        string title = SeraphHorizons.Mod.Trading.Maps.MapMarksSystem.Title(lang, WaypointTitle(lang, a), precision);
        var marks = SeraphHorizons.Mod.Trading.Maps.MapMarksSystem.Of(api);
        var target = SeraphHorizons.Mod.Trading.Maps.MapMarksSystem.TargetOf(slot.Itemstack);
        var outcome = marks is null || target is null
            ? SeraphHorizons.Mod.Trading.Maps.MapMarksSystem.Outcome.NoMap
            : marks.Mark(player, target.Value, pos, title, gravel ? "rocks" : "pick",
                gravel ? ColorUtil.ColorFromRgba(220, 190, 90, 255) : ColorUtil.ColorFromRgba(200, 110, 60, 255));
        switch (outcome)
        {
            case SeraphHorizons.Mod.Trading.Maps.MapMarksSystem.Outcome.NoMap:
                var d = pos.Clone().Sub(byEntity.Pos.XYZ);
                d.Y = 0;
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:oremap-distance", title, (int)d.Length()),
                    EnumChatType.Notification);
                return;
            case SeraphHorizons.Mod.Trading.Maps.MapMarksSystem.Outcome.Already:
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "seraphhorizons:oremap-marked-already"), EnumChatType.Notification);
                return;
        }
        player.SendMessage(GlobalConstants.GeneralChatGroup,
            Lang.GetL(lang, precision == MapPrecision.Exact ? "seraphhorizons:oremap-marked" : "seraphhorizons:oremap-marked-near", title),
            EnumChatType.Notification);
    }

    /// <summary>"Copper deposit (large)", "Rich gravel (granite)".</summary>
    public static string WaypointTitle(string lang, Vintagestory.API.Datastructures.ITreeAttribute a)
    {
        string metal = a.GetString(AttrMetal) ?? "";
        if (metal == PlacerCells.Kind)
            return a.GetString(AttrRock) is { } rock
                ? Lang.GetL(lang, "seraphhorizons:oremap-waypoint-gravel-rock", Lang.GetL(lang, "game:rock-" + rock))
                : Lang.GetL(lang, "seraphhorizons:oremap-waypoint-gravel");
        string name = MetalName(lang, metal);
        return a.GetString(AttrSizeTier) is { } tier
            ? Lang.GetL(lang, "seraphhorizons:oremap-waypoint-sized", name, Lang.GetL(lang, "seraphhorizons:ore-size-" + tier))
            : Lang.GetL(lang, "seraphhorizons:oremap-waypoint", name);
    }

    public static string MetalName(string lang, string metal) => Lang.GetL(lang, "seraphhorizons:oremap-metal-" + metal);

    public override string GetHeldItemName(ItemStack itemStack)
    {
        if (Hooks?.HeldName(itemStack) is { } offerName) return offerName;
        var a = itemStack.Attributes;
        string? metal = a.GetString(AttrMetal);
        if (metal == null || metal == PlacerCells.Kind) return base.GetHeldItemName(itemStack);
        return Lang.Get("seraphhorizons:oremap-name", MetalName(Lang.CurrentLocale, metal));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is { } held && Hooks?.HeldInfo(held, dsc) == true) return;
        var a = inSlot.Itemstack?.Attributes;
        if (a == null || a.GetString(AttrMetal) is not { } metal)
        {
            dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-blank"));
            return;
        }
        if (metal == PlacerCells.Kind)
        {
            if (a.GetString(AttrRock) is { } rock)
                dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-rock", Lang.Get("game:rock-" + rock)));
        }
        else
        {
            dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-precision-" + a.GetInt(AttrPrecision, MapPrecision.Exact)));
            dsc.AppendLine(a.GetString(AttrSizeTier) is { } tier
                ? Lang.Get("seraphhorizons:oremap-info-size", Lang.Get("seraphhorizons:ore-size-" + tier))
                : Lang.Get("seraphhorizons:oremap-info-unsurveyed"));
        }
        dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-use"));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
    [
        new WorldInteraction { ActionLangCode = "seraphhorizons:oremap-help-read", MouseButton = EnumMouseButton.Right },
        .. base.GetHeldInteractionHelp(inSlot),
    ];
}
