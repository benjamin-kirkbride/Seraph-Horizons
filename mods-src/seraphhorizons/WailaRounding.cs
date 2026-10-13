using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod;

/// <summary>
/// The block and entity info HUD (the "WAILA" box at the top of the screen,
/// <c>HudElementBlockAndEntityInfo</c>) shows numbers as their makers formatted them, often a raw
/// <c>float</c>: a creature's health reads <c>Health: 13.4567/20</c>. This tweak rounds the numbers
/// in its info text (<see cref="InfoTextRounding"/>): those on the health line to whole numbers,
/// every other one to one decimal.
///
/// <c>ComposeBlockInfoHud</c> takes the text from <c>Block.GetPlacedBlockInfo</c> (a block, its
/// block entity and their behaviors) or <c>Entity.GetInfoText</c> (an entity and its behaviors),
/// and composes the HUD again only when the text differs from what it shows. A transpiler passes
/// each of those two results through <see cref="Round"/> right where the method reads it, so the
/// comparison sees the rounded text and the HUD is not composed again for a change it would not
/// show. Both calls are counted before patching: if either is not there exactly once, the tweak logs
/// a warning and the HUD stays as it ships. The title (the block's or entity's name) is left alone.
///
/// The health line is found by the game's own format string, in the client's language
/// (<see cref="HealthKey"/>, read on every compose, so a language change is followed), and English.
/// Client side only: the HUD is the client's.
/// </summary>
public static class WailaRounding
{
    public const string HarmonyId = "seraphhorizons.wailarounding";
    public const string HudType = "Vintagestory.Client.NoObf.HudElementBlockAndEntityInfo";

    /// <summary>The lang key of vanilla's health line (<c>EntityBehaviorHealth.GetInfoText</c>,
    /// shown in creative), whose value in English is the key.</summary>
    public const string HealthKey = "Health: {0}/{1}";

    private static readonly MethodInfo BlockInfo = AccessTools.Method(typeof(Block), nameof(Block.GetPlacedBlockInfo));
    private static readonly MethodInfo EntityInfo = AccessTools.Method(typeof(Entity), nameof(Entity.GetInfoText));

    private static InfoTextRounding? _rounding;
    private static (string Template, string Separator) _roundingFor;
    private static ILogger? _logger;
    private static bool _failed;

    public static MethodInfo? Target => AccessTools.TypeByName(HudType) is { } type
        ? AccessTools.DeclaredMethod(type, "ComposeBlockInfoHud")
        : null;

    /// <summary>Patches the HUD; logs and leaves it when it is not as expected. Returns whether the
    /// patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        if (Target is not { } target)
        {
            logger.Warning("[seraphhorizons] Info HUD rounding: {0}.ComposeBlockInfoHud not found; the game changed, so the HUD's numbers stay as they are",
                HudType);
            return false;
        }
        var original = PatchProcessor.GetOriginalInstructions(target);
        int blockCalls = original.Count(i => i.Calls(BlockInfo));
        int entityCalls = original.Count(i => i.Calls(EntityInfo));
        if (blockCalls != 1 || entityCalls != 1)
        {
            logger.Warning("[seraphhorizons] Info HUD rounding: {0}.ComposeBlockInfoHud calls GetPlacedBlockInfo {1} and GetInfoText {2} times, not once each; "
                           + "the game changed, so the HUD's numbers stay as they are", HudType, blockCalls, entityCalls);
            return false;
        }
        _logger = logger;
        _failed = false;
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(WailaRounding), nameof(Transpiler)));
        logger.Notification("[seraphhorizons] Info HUD rounding: health to whole numbers, other numbers to one decimal");
        return true;
    }

    /// <summary>Forgets the logger and the cached rounding, so a patch left over does nothing odd.</summary>
    public static void Unbind()
    {
        _logger = null;
        _rounding = null;
        _roundingFor = default;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var round = AccessTools.Method(typeof(WailaRounding), nameof(Round));
        foreach (var i in instructions)
        {
            yield return i;
            if (i.Calls(BlockInfo) || i.Calls(EntityInfo))
                yield return new CodeInstruction(OpCodes.Call, round);
        }
    }

    /// <summary>The info text with its numbers rounded. Never throws: on a failure the text is
    /// shown as it came, and the tweak is off until the client starts again.</summary>
    public static string? Round(string? text)
    {
        if (_failed || string.IsNullOrEmpty(text))
            return text;
        try
        {
            return Rounding().Round(text);
        }
        catch (Exception e)
        {
            // Once: the HUD composes every half second.
            _failed = true;
            _logger?.Error("[seraphhorizons] Info HUD rounding failed and is off until the client restarts: {0}", e);
            return text;
        }
    }

    private static InfoTextRounding Rounding()
    {
        string template = Lang.GetUnformatted(HealthKey) ?? HealthKey;
        string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (_rounding == null || _roundingFor != (template, separator))
        {
            _rounding = new InfoTextRounding([template, HealthKey], separator);
            _roundingFor = (template, separator);
        }
        return _rounding;
    }
}
