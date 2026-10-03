using Vintagestory.API.Common;

namespace SeraphHorizons.SeraphTweaks;

/// <summary>
/// Pipes and Power Expanded (ppex): the handbook says a chimney on a pipe outlet vents the
/// network's gas, and nothing more. ppex vents through a chimney only when it stands directly on
/// an outlet, a passthrough or a passthrough bend with a connector on its top face (16 L/s per
/// chimney, <c>ChimneyGasDrawRate</c>). On a plain pipe the chimney is neither a vent nor, not
/// being air, a leak: it caps that end and the run keeps its pressure. This tweak says so in the
/// Fittings handbook page, in every language ppex ships. It changes text only.
/// </summary>
public static class ChimneyVentText
{
    public const string ModId = "ppex";

    /// <summary>The chimney passage of the Fittings page, reworded in every language ppex ships.
    /// Each edit replaces one exact passage of ppex's text; if ppex rewords it, the edit is skipped
    /// with a warning. The quoted look-at line is ppex's own <c>chimney-info-venting</c>.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "ppex:handbook-fittings-text",
            "An outlet ends a run at a machine's port face; or, capped with an ordinary <strong>chimney</strong> "
            + "stood upright on top of it, it instead vents the network's gas to the sky at about 16 L/s - the "
            + "standard way to exhaust a firebox.",
            "An outlet ends a run at a machine's port face. An ordinary <strong>chimney</strong> vents the "
            + "network's gas to the sky at about 16 L/s per chimney - the standard way to exhaust a firebox - but "
            + "only when it stands directly on top of an outlet or a passthrough (straight or bend) whose pipe "
            + "opens upward. To check, look at the chimney: while it draws gas its info reads \"Venting pipe "
            + "network\". A chimney set straight on a plain pipe vents nothing: it only caps that end, and the run "
            + "keeps its pressure."),

        new("ru", "ppex:handbook-fittings-text",
            "Выход трубы служит конечным элементом трубопровода у порта машины. Если поверх него установить "
            + "<strong>дымоход</strong>, тот будет стравливать газ из сети в атмосферу со скоростью около 16 л/с - "
            + "это стандартный способ отвести выхлоп топки.",
            "Выход трубы служит конечным элементом трубопровода у порта машины. Обычный <strong>дымоход</strong> "
            + "стравливает газ из сети в атмосферу со скоростью около 16 л/с на каждый дымоход - это стандартный "
            + "способ отвести выхлоп топки, - но только когда он стоит прямо на выходе трубы или на проходной трубе "
            + "(прямой или с поворотом), труба которых открыта вверх. Чтобы проверить, посмотрите на дымоход: пока "
            + "он вытягивает газ, в его описании есть строка «Сброс из трубопровода». Дымоход, поставленный прямо "
            + "на обычную трубу, ничего не стравливает: он лишь заглушает этот конец, и давление в трубопроводе "
            + "сохраняется."),

        new("uk", "ppex:handbook-fittings-text",
            "Вихід завершує гілку біля порту машини; а якщо накрити його звичайним <strong>димарем</strong>, "
            + "поставленим вертикально зверху, він натомість стравлює газ із мережі в небо зі швидкістю близько "
            + "16 л/с - стандартний спосіб відвести вихлоп топки.",
            "Вихід завершує гілку біля порту машини. Звичайний <strong>димар</strong> стравлює газ із мережі в "
            + "небо зі швидкістю близько 16 л/с на кожен димар - стандартний спосіб відвести вихлоп топки, - але "
            + "лише тоді, коли він стоїть просто на виході труби або на прохідній трубі (прямій чи з поворотом), "
            + "труба яких відкрита догори. Щоб перевірити, подивіться на димар: поки він витягує газ, у його описі "
            + "є рядок «Скидання з трубопроводу». Димар, поставлений просто на звичайну трубу, нічого не стравлює: "
            + "він лише заглушує цей кінець, і тиск у трубопроводі зберігається."),
    ];

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Applies <see cref="LangEdits"/> (<see cref="LangText.Apply"/>).</summary>
    public static void RewriteText(ILogger logger) => LangText.Apply(LangEdits, ModId, logger);
}
