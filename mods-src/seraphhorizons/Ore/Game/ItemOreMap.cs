using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
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
    /// <summary>A gravel field's rock, or the rock most of an ore deposit sits in (#692).</summary>
    public const string AttrRock = "rock";
    /// <summary>The ores named (#692, <see cref="DepositMakeup.MainOres"/>), comma-separated.</summary>
    public const string AttrOres = "ores";
    /// <summary>The grades in words (#692, <see cref="GradeMix.Code"/>).</summary>
    public const string AttrGrades = "grades";
    /// <summary>The metals a gravel field pans (#692), comma-separated.</summary>
    public const string AttrMetals = "metals";
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

    /// <summary>"Galena and cerussite deposit (medium)", "Rich gravel (granite)"; a map made before
    /// maps named the ore (#692) says the metal: "Copper deposit (large)".</summary>
    public static string WaypointTitle(string lang, ITreeAttribute a)
    {
        string metal = a.GetString(AttrMetal) ?? "";
        if (metal == PlacerCells.Kind)
            return a.GetString(AttrRock) is { } rock
                ? Lang.GetL(lang, "seraphhorizons:oremap-waypoint-gravel-rock", RockName(lang, rock))
                : Lang.GetL(lang, "seraphhorizons:oremap-waypoint-gravel");
        string name = DepositName(lang, a);
        return a.GetString(AttrSizeTier) is { } tier
            ? Lang.GetL(lang, "seraphhorizons:oremap-waypoint-sized", name, Lang.GetL(lang, "seraphhorizons:ore-size-" + tier))
            : Lang.GetL(lang, "seraphhorizons:oremap-waypoint", name);
    }

    public static string MetalName(string lang, string metal) => Lang.GetL(lang, "seraphhorizons:oremap-metal-" + metal);

    public static string RockName(string lang, string rock) => Lang.GetL(lang, "game:rock-" + rock);

    /// <summary>What an ore map or offer names (#692): its ores, capitalised ("Galena and
    /// cerussite"), or for a map without them its metal ("Copper").</summary>
    public static string DepositName(string lang, ITreeAttribute a)
    {
        var ores = OreNames.Split(a.GetString(AttrOres));
        return ores.Count > 0 ? Capitalise(OreList(lang, ores)) : MetalName(lang, a.GetString(AttrMetal) ?? "");
    }

    /// <summary>"galena", "galena and cerussite", "galena, cerussite and wulfenite".</summary>
    public static string OreList(string lang, IReadOnlyList<string> ores) =>
        List(lang, ores.Select(o => Lang.GetL(lang, "seraphhorizons:" + OreNames.LangKey(o))).ToList());

    /// <summary>"copper", "tin and gold", "tin, silver and gold".</summary>
    public static string MetalList(string lang, IReadOnlyList<string> metals) =>
        List(lang, metals.Select(m => Lang.GetL(lang, "seraphhorizons:ore-metal-" + m)).ToList());

    private static string List(string lang, IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => Lang.GetL(lang, "seraphhorizons:ore-list-and",
            items.Take(items.Count - 1).Aggregate((acc, next) => Lang.GetL(lang, "seraphhorizons:ore-list-comma", acc, next)), items[^1]),
    };

    /// <summary>"mostly poor", "poor and medium", "rich"; null for a code that does not read.</summary>
    public static string? GradeText(string lang, string? code) => GradeMix.Parse(code) is not { } mix ? null : mix.Kind switch
    {
        GradeMix.Mixed => Lang.GetL(lang, "seraphhorizons:ore-grades-mixed", Grade(lang, mix.Grades[0]), Grade(lang, mix.Grades[1])),
        _ => Lang.GetL(lang, "seraphhorizons:ore-grades-" + mix.Kind, Grade(lang, mix.Grades[0])),
    };

    private static string Grade(string lang, string grade) => Lang.GetL(lang, "seraphhorizons:ore-grade-" + grade);

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    /// <summary>Writes a deposit's makeup on a map or an offer (#692): its ores, grades and host rock.</summary>
    public static void SetMakeup(ITreeAttribute a, DepositMakeup makeup)
    {
        if (makeup.MainOres() is { Count: > 0 } ores) a.SetString(AttrOres, OreNames.Csv(ores));
        if (makeup.Mix() is { } mix) a.SetString(AttrGrades, mix.Code);
        if (makeup.HostRock() is { } rock) a.SetString(AttrRock, rock);
    }

    /// <summary>
    /// The lines that say what a deposit is (#692), for a map and its trader's offer alike: an ore
    /// deposit's ores, grades, host rock and size (the metal in the ground, not what a given way of
    /// working the ore wins from it); a gravel field's rock and the metals it pans.
    /// </summary>
    public static void AppendDeposit(string lang, ITreeAttribute a, StringBuilder dsc)
    {
        string metal = a.GetString(AttrMetal) ?? "";
        if (metal == PlacerCells.Kind)
        {
            if (a.GetString(AttrRock) is { } rock)
                dsc.AppendLine(Lang.GetL(lang, "seraphhorizons:oremap-info-rock", RockName(lang, rock)));
            if (OreNames.Split(a.GetString(AttrMetals)) is { Count: > 0 } metals)
                dsc.AppendLine(Lang.GetL(lang, "seraphhorizons:oremap-info-pans", MetalList(lang, metals)));
            return;
        }
        if (OreNames.Split(a.GetString(AttrOres)) is { Count: > 0 } ores)
            dsc.AppendLine(Lang.GetL(lang, "seraphhorizons:oremap-info-ores", OreList(lang, ores)));
        if (GradeText(lang, a.GetString(AttrGrades)) is { } grades)
            dsc.AppendLine(Lang.GetL(lang, "seraphhorizons:oremap-info-grades", grades));
        if (a.GetString(AttrRock) is { } host)
            dsc.AppendLine(Lang.GetL(lang, "seraphhorizons:oremap-info-host", RockName(lang, host)));
        dsc.AppendLine(a.GetString(AttrSizeTier) is { } tier
            ? Lang.GetL(lang, "seraphhorizons:oremap-info-size", Lang.GetL(lang, "seraphhorizons:ore-size-" + tier))
            : Lang.GetL(lang, "seraphhorizons:oremap-info-unsurveyed"));
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        if (Hooks?.HeldName(itemStack) is { } offerName) return offerName;
        var a = itemStack.Attributes;
        string? metal = a.GetString(AttrMetal);
        if (metal == PlacerCells.Kind && a.GetString(AttrRock) is { } rock)
            return Lang.Get("seraphhorizons:gravelmap-name", RockName(Lang.CurrentLocale, rock));
        if (metal == null || metal == PlacerCells.Kind) return base.GetHeldItemName(itemStack);
        return Lang.Get("seraphhorizons:oremap-name", DepositName(Lang.CurrentLocale, a));
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
        if (metal != PlacerCells.Kind)
            dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-precision-" + a.GetInt(AttrPrecision, MapPrecision.Exact)));
        AppendDeposit(Lang.CurrentLocale, a, dsc);
        dsc.AppendLine(Lang.Get("seraphhorizons:oremap-info-use"));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
    [
        new WorldInteraction { ActionLangCode = "seraphhorizons:oremap-help-read", MouseButton = EnumMouseButton.Right },
        .. base.GetHeldInteractionHelp(inSlot),
    ];
}
