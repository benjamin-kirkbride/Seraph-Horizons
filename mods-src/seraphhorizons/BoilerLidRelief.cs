using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod;

/// <summary>
/// Pipes and Power Expanded (ppex): a boiler whose steam run is sealed and that sits at its choke
/// pressure, still firing, for <c>BoilerOverpressureSeconds</c> calls <c>BlockEntityBoiler.Explode()</c>,
/// which removes the boiler and blasts the area around it (a run open at an end leaks, and ppex
/// blows such a boiler down to about 1 atm instead, so it never gets there). This tweak replaces that call with the boiler's own
/// <c>ToggleLid()</c>: the access lid swings open with its sound and animation, the open lid
/// vents the steam, and ppex resets the over-pressure timer while it is open. The player closes
/// it again by hand. The lid blowing open also bangs: it plays the sound ppex plays when one of
/// its steam engines blows up (<c>BlockEntityEngine.Break()</c>), the same way.
///
/// ppex is not referenced at build time. The boiler is found by name, and if any member this
/// relies on is missing the tweak logs a warning and leaves ppex as it is.
/// </summary>
public static class BoilerLidRelief
{
    public const string ModId = "ppex";
    public const string BoilerType = "PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntityBoiler";

    /// <summary>The sound of a ppex engine blowing up: ExpandedLib's <c>ExSounds.MediumExplosion</c>,
    /// a vanilla sound, which ppex's <c>BlockEntityEngine.Break()</c> plays at the engine's block
    /// centre on the server, unrandomized, over <see cref="BlowSoundRange"/> blocks at
    /// <see cref="BlowSoundVolume"/>.</summary>
    public static readonly AssetLocation BlowSound = new("game:sounds/effect/mediumexplosion");
    public const float BlowSoundRange = 24f;
    public const float BlowSoundVolume = 0.5f;

    private static MethodInfo? _toggleLid;
    private static PropertyInfo? _lidOpen;

    /// <summary>Text that says a boiler explodes, reworded in every language ppex ships. Each edit
    /// replaces one exact passage of ppex's text; if ppex rewords it, the edit is skipped with a
    /// warning.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "ppex:boiler-info-overpressure",
            "until bursts!",
            "until the lid blows open!"),
        new("en", "ppex:handbook-boilers-text",
            "<strong>Boilers are dangerous.</strong> One that keeps firing with nowhere to send its steam sits at "
            + "its choke pressure and can <strong>explode</strong>. Your emergency release is the lid - opening it "
            + "vents steam harmlessly at around 200 L/s. An unpiped steam outlet or a steam run left open at an end "
            + "blows the boiler down to about 1 atm, which saves the vessel but leaves nothing on the run any working "
            + "pressure.",
            "<strong>Mind the pressure.</strong> One that keeps firing with nowhere to send its steam sits at its "
            + "choke pressure, and if it stays there too long the steam <strong>blows the lid open</strong>. The "
            + "open lid vents steam at around 200 L/s, so the boiler loses its pressure until you close it again; "
            + "open it yourself to bleed off steam before that happens. An unpiped steam outlet or a steam run left "
            + "open at an end blows the boiler down to about 1 atm: the lid stays shut, but nothing on the run gets "
            + "any working pressure."),
        new("en", "ppex:handbook-steampower-text",
            "a mismanaged boiler can end in a catastrophic explosion",
            "a mismanaged boiler blows its lid and loses its steam"),

        new("ru", "ppex:boiler-info-overpressure",
            "Превышение давления! {0:F0} с до взрыва!",
            "Превышение давления! Через {0:F0} с давление откинет крышку!"),
        new("ru", "ppex:handbook-boilers-text",
            "<strong>Бойлеры опасны.</strong> Тот, что продолжает топиться, но не имеет куда отправить пар, будет "
            + "находиться на давлении захлёбывания и может <strong>взорваться</strong>. Ваш аварийный сброс - "
            + "крышка: открыв её, вы безвредно стравливаете пар со скоростью около 200 л/с. Неподключённый паровой "
            + "выход или паровая линия с открытым концом стравливают бойлер примерно до 1 атм: резервуар так не "
            + "взорвётся, но и машинам на линии не достанется рабочего давления.",
            "<strong>Следите за давлением.</strong> Тот, что продолжает топиться, но не имеет куда отправить пар, "
            + "будет находиться на давлении захлёбывания, и если пробудет на нём слишком долго, давление "
            + "<strong>откинет крышку</strong>. Через открытую крышку пар уходит со скоростью около 200 л/с, так что "
            + "бойлер теряет давление, пока вы снова её не закроете; откройте её сами, чтобы стравить пар заранее. "
            + "Неподключённый паровой выход или паровая линия с открытым концом стравливают бойлер примерно до "
            + "1 атм: крышку так не откинет, но и машинам на линии не достанется рабочего давления."),
        new("ru", "ppex:handbook-steampower-text",
            "а неправильное управление бойлером может закончиться катастрофическим взрывом",
            "а при неправильном управлении бойлер откидывает крышку и теряет пар"),

        new("uk", "ppex:boiler-info-overpressure",
            "Перевищення тиску! {0:F0} с до вибуху!",
            "Перевищення тиску! Через {0:F0} с тиск відкине кришку!"),
        new("uk", "ppex:handbook-boilers-text",
            "<strong>Бойлери небезпечні.</strong> Той, який і далі топиться, але не має куди подіти пару, "
            + "тримається на своєму тиску захлинання й може <strong>вибухнути</strong>. Ваш аварійний скид - "
            + "кришка: відкривши її, ви безпечно стравлюєте пару зі швидкістю близько 200 л/с. Непідключений вихід "
            + "пари або парова лінія з відкритим кінцем стравлюють бойлер приблизно до 1 атм: так резервуар не "
            + "вибухне, але й машини на лінії не отримають робочого тиску.",
            "<strong>Стежте за тиском.</strong> Той, який і далі топиться, але не має куди подіти пару, тримається "
            + "на своєму тиску захлинання, і якщо простоїть на ньому надто довго, тиск <strong>відкине "
            + "кришку</strong>. Крізь відкриту кришку пара виходить зі швидкістю близько 200 л/с, тож бойлер "
            + "втрачає тиск, доки ви знову її не закриєте; відкрийте її самі, щоб стравити пару заздалегідь. "
            + "Непідключений вихід пари або парова лінія з відкритим кінцем стравлюють бойлер приблизно до 1 атм: "
            + "так кришку не відкине, але й машини на лінії не отримають робочого тиску."),
        new("uk", "ppex:handbook-steampower-text",
            "а недогляд за бойлером може обернутися катастрофічним вибухом",
            "а через недогляд бойлер відкидає кришку та втрачає пару"),
    ];

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Prefixes <c>Explode()</c>. Returns whether the patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var boiler = AccessTools.TypeByName(BoilerType);
        var explode = boiler == null ? null : AccessTools.DeclaredMethod(boiler, "Explode", Type.EmptyTypes);
        _toggleLid = boiler == null ? null : AccessTools.DeclaredMethod(boiler, "ToggleLid", Type.EmptyTypes);
        _lidOpen = boiler == null ? null : AccessTools.DeclaredProperty(boiler, "LidOpen");
        if (boiler == null || !typeof(BlockEntity).IsAssignableFrom(boiler) || explode == null
            || _toggleLid == null || _lidOpen?.PropertyType != typeof(bool) || _lidOpen.GetMethod == null)
        {
            logger.Warning($"[seraphhorizons] {BoilerType} does not have Explode(), ToggleLid() and LidOpen as "
                           + "expected; ppex changed, so its boilers still explode");
            return false;
        }

        harmony.Patch(explode, prefix: new HarmonyMethod(typeof(BoilerLidRelief), nameof(ExplodePrefix)));
        return true;
    }

    /// <summary>Opens the lid in place of the explosion. ppex only calls Explode() with the lid
    /// shut; with it already open there is nothing to do but skip the blast. The bang is played
    /// here and not by ToggleLid(), which plays only the lid's own creak.</summary>
    public static bool ExplodePrefix(BlockEntity __instance)
    {
        if (!(bool)_lidOpen!.GetValue(__instance)!)
        {
            _toggleLid!.Invoke(__instance, null);
            PlayBlowSound(__instance);
        }
        __instance.Api?.Logger.Notification(
            $"[seraphhorizons] Boiler at {__instance.Pos} over-pressured and blew its lid open instead of exploding");
        return false;
    }

    /// <summary>Plays <see cref="BlowSound"/> as ppex plays it for a breaking engine: from the
    /// server, so every client in range hears it.</summary>
    private static void PlayBlowSound(BlockEntity boiler)
    {
        if (boiler.Api?.Side != EnumAppSide.Server)
            return;
        BlockPos pos = boiler.Pos;
        boiler.Api.World.PlaySoundAt(BlowSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null,
            randomizePitch: false, BlowSoundRange, BlowSoundVolume);
    }

    /// <summary>Applies <see cref="LangEdits"/> (<see cref="LangText.Apply"/>).</summary>
    public static void RewriteText(ILogger logger) => LangText.Apply(LangEdits, ModId, logger);
}
