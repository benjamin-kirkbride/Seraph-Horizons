using System.Globalization;
using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Values;

/// <summary>
/// The value line on every item and block handbook page (#506, README "Item base values"): the
/// rusty gear's icon and the item's base value, or "No trade value" when the table has none (no
/// direct value and no family) or when a switch its value depends on (the table's
/// <c>switches</c>) is off on the server. Under the page's general info (name, icon, description),
/// above its sections: a postfix on the game's private
/// <c>CollectibleBehaviorHandbookTextAndExtraInfo.addGeneralInfo</c>, client side. The rusty gear's
/// own page also says once what the number is.
///
/// Which switches are off is the server's: it writes them to the world config
/// (<see cref="OffKey"/>) in <c>Start</c>, as the unified woodworking does its state, and a client
/// reads them from there.
/// </summary>
public static class ValueHandbook
{
    public const string HarmonyId = "seraphhorizons.itemvalues";
    public const string OffKey = "seraphhorizons:switchesOff";
    public const string RustyGear = "game:gear-rusty";

    private static ICoreClientAPI? _capi;
    private static ItemStack? _gear;
    private static HashSet<string>? _off;

    /// <summary>The game method the line goes after; null when 1.22's signature changed.</summary>
    public static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addGeneralInfo",
            [typeof(ItemSlot), typeof(ICoreClientAPI), typeof(ItemStack), typeof(List<RichTextComponentBase>),
             typeof(float).MakeByRefType(), typeof(float).MakeByRefType()]);

    /// <summary>Every bool switch of the config that is false.</summary>
    public static IEnumerable<string> OffSwitches(SeraphHorizonsConfig config) =>
        typeof(SeraphHorizonsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.GetValue(config) is false)
            .Select(p => p.Name);

    /// <summary>Server: tells clients which switches are off.</summary>
    public static void Publish(ICoreAPI api) =>
        api.World.Config.SetString(OffKey, SwitchOwnership.EncodeOff(OffSwitches(SeraphHorizonsSystem.ConfigFor(api))));

    /// <summary>Client: adds the line to every page from now on. False, with a warning, when the
    /// game's method is not as expected.</summary>
    public static bool Patch(Harmony harmony, ICoreClientAPI capi)
    {
        var target = Target();
        if (target == null)
        {
            capi.Logger.Warning("[seraphhorizons] Item values: CollectibleBehaviorHandbookTextAndExtraInfo.addGeneralInfo is not as "
                                + "expected; the game changed, so handbook pages show no value");
            return false;
        }
        _capi = capi;
        _gear = null;
        _off = null;
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(ValueHandbook), nameof(AddGeneralInfoPostfix)));
        return true;
    }

    public static void Unbind()
    {
        _capi = null;
        _gear = null;
        _off = null;
    }

    private static void AddGeneralInfoPostfix(ItemStack stack, List<RichTextComponentBase> components)
    {
        var capi = _capi;
        if (capi == null || stack?.Collectible?.Code is not { } code)
            return;
        try
        {
            components.AddRange(Line(capi, code.ToString()));
        }
        catch (Exception e)
        {
            capi.Logger.Error("[seraphhorizons] Item values: could not add the value line to {0}'s page: {1}", code, e);
        }
    }

    /// <summary>The line's components for <paramref name="code"/>.</summary>
    public static List<RichTextComponentBase> Line(ICoreClientAPI capi, string code)
    {
        _off ??= SwitchOwnership.DecodeOff(capi.World.Config?.GetString(OffKey));
        var shown = ItemValuesSystem.For(capi).Shown(code, _off.Contains);
        var font = CairoFont.WhiteSmallText();
        var line = new List<RichTextComponentBase> { new ClearFloatTextComponent(capi, 4f) };
        if (shown is { } value && Gear(capi) is { } gear)
        {
            line.Add(new ItemstackTextComponent(capi, gear, 30, 4, EnumFloat.Inline) { VerticalAlign = EnumVerticalAlign.Middle });
            line.AddRange(VtmlUtil.Richtextify(capi, Format(value) + "\n", font));
        }
        else
        {
            line.AddRange(VtmlUtil.Richtextify(capi, Escape(Lang.Get("seraphhorizons:itemvalues-handbook-none")) + "\n", font));
        }
        if (code == RustyGear)
            line.AddRange(VtmlUtil.Richtextify(capi, Escape(Lang.Get("seraphhorizons:itemvalues-handbook-explained")) + "\n", font));
        return line;
    }

    /// <summary>The value as the page shows it: up to three decimals, as the table has them.</summary>
    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static ItemStack? Gear(ICoreClientAPI capi)
    {
        if (_gear == null && capi.World.GetItem(new AssetLocation(RustyGear)) is { IsMissing: false } item)
            _gear = new ItemStack(item);
        return _gear;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
