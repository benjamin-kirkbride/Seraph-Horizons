using SeraphHorizons.Mod.Trading.Orders;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>The view model's <see cref="Text"/> in the player's language: lang keys in the mod's
/// domain, nested texts first, item references by their stacks' names.</summary>
public static class WindowText
{
    public static string Resolve(ICoreClientAPI capi, Text text) =>
        Lang.Get("seraphhorizons:" + text.Key, text.Args.Select(a => Arg(capi, a)).ToArray());

    private static object Arg(ICoreClientAPI capi, object arg) => arg switch
    {
        Text inner => Resolve(capi, inner),
        ItemRef item => TraderFinder.ItemName(capi.World, item.Code),
        _ => arg,
    };

    public static string Lines(ICoreClientAPI capi, IEnumerable<Text> lines) => string.Join("\n", lines.Select(l => Resolve(capi, l)));

    /// <summary>The trader's standing reply as VTML: each line in its voice, the numbers after it in
    /// square brackets, set off in a quieter colour.</summary>
    public static string Speech(ICoreClientAPI capi, IEnumerable<SpeechLine> lines) =>
        string.Join("\r\n", lines.Select(l =>
        {
            string voice = Escape(Resolve(capi, l.Voice));
            if (l.Facts.Count == 0) return voice;
            string facts = string.Join(" · ", l.Facts.Select(f => Resolve(capi, f)));
            return $"{voice} <font color=\"#b8b096\">[{Escape(facts)}]</font>";
        }));

    /// <summary>Text set into VTML as text, not tags.</summary>
    public static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
