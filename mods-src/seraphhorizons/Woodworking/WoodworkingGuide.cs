using System.Text.RegularExpressions;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The handbook's woodworking guide, one for both mods: six pages of this mod's
/// (<c>assets/seraphhorizons/config/handbook/woodworking-*.json</c>, text in its <c>lang/en.json</c>),
/// an overview that keeps Immersive Woodworking's page code (<c>craftinginfo-woodworking</c>, which
/// its item pages link to) and five chapters. And the two mods' own item and block text, rewritten
/// to match the unified system and to link to the guide.
///
/// - Pages: a handler on <c>ModSystemSurvivalHandbook.OnInitCustomPages</c> (client) arranges the
///   handbook's page list each time it is built (<see cref="WoodworkingGuidePages.Arrange"/>). With
///   the tweak running it drops Immersive Woodworking's "Crafting Mechanic: Immersive Woodworking"
///   and Logging Expanded's "Crafting Mechanic: Logging Expanded" and shows the six in their place;
///   otherwise (the tweak not running) it drops the six. The handler is added in
///   <see cref="Start"/> or <see cref="Off"/>, so it runs either way. The handbook is built on the
///   client; on the server the pages a player does not see are listed in
///   <c>ObjectCache[</c><see cref="HiddenGuidesKey"/><c>]</c> (page code and title key;
///   <see cref="WoodworkingGuidePages.Hidden"/>): the replaced guides with the tweak running, the
///   six without. The recipe export reads it to leave them out, so it lists the guide a player
///   sees. (Emptying their files on the server does not last: the game unloads config assets after
///   startup and reads them from the mod again.)
/// - Exact entries (<see cref="Replacements"/>) are replaced by <see cref="WoodworkingText"/>, which
///   also removes Immersive Woodworking's translations of them, so de, pt-br, ru and zh-cn show the
///   English. Immersive Woodworking composes some of these at display time or on
///   <c>LevelFinalize</c>, after the lang edits, so its own sections show the new text.
/// - Logging Expanded's block texts are pattern entries (<c>...:sawhorse-*</c>), which the game
///   keeps apart from the exact ones (<see cref="LangPatternKeys"/>). <see cref="PatternReplacements"/>
///   replaces them where the game keeps them (<c>TranslationService</c>'s private <c>wildcardCache</c>
///   and <c>regexCache</c>, 1.22.7), in AssetsLoaded on both sides. If those are not there, it warns
///   and Logging Expanded's text stays.
/// </summary>
public sealed class WoodworkingGuide : WoodworkingPart
{
    private const string ModNames = "Immersive Woodworking or Logging Expanded";

    // Page links.
    public static readonly string Overview = WoodworkingGuidePages.Pages[0].PageCode;
    public static readonly string Trunks = WoodworkingGuidePages.Pages[1].PageCode;
    public static readonly string SawhorsesChapter = WoodworkingGuidePages.Pages[2].PageCode;
    public static readonly string SplittingBlockChapter = WoodworkingGuidePages.Pages[3].PageCode;
    public static readonly string BarkChapter = WoodworkingGuidePages.Pages[4].PageCode;
    public static readonly string MachinesChapter = WoodworkingGuidePages.Pages[5].PageCode;
    public const string SplittingBlockPage = "block-immersivewoodworking:choppingblock";
    public const string SawhorsePage = "block-loggingmod:sawhorse-north";
    public const string StandardSawhorsePage = "block-loggingmod:sawhorsestandard-oak-copper-north";
    public const string AdvancedSawhorsePage = "block-loggingmod:sawhorseadvanced-oak-north";
    public const string TrunkPage = "block-loggingmod:treetrunk-oak-xs-no-north";
    public const string SawmillPage = "block-immersivewoodworking:sawmill-frame-north";
    public const string ChopperPage = "block-immersivewoodworking:chopper-frame-north";
    public const string BarkSpudPage = "item-immersivewoodworking:barkspud-copper";
    public const string MaulPage = "item-immersivewoodworking:maul-copper";

    private static string A(string page, string text) => $"<a href=\"handbook://{page}\">{text}</a>";

    private static string See(string page, string title) => $"\n\nSee {A(page, title)}.";

    private static readonly string SeeOverview = $"\n\nSee the {A(Overview, "Woodworking")} guide.";
    private static readonly string SeeSawhorses = See(SawhorsesChapter, "Woodworking: Sawhorses");
    private static readonly string SeeSplittingBlock = See(SplittingBlockChapter, "Woodworking: Splitting block");
    private static readonly string SeeBark = See(BarkChapter, "Woodworking: Bark");
    private static readonly string SeeMachines = See(MachinesChapter, "Woodworking: Machines");
    private static readonly string SeeTrunks = See(Trunks, "Woodworking: Trunks");

    private static readonly string Planks =
        $"on a {A(SawhorsePage, "sawhorse")}: 9 per log on the primitive sawhorse, 12 on the standard and 18 on the "
        + "advanced";

    private static readonly string Beams =
        $"on a {A(SawhorsePage, "sawhorse")} with a saw while holding <hk>shift</hk>: 2 per log on the primitive and "
        + "standard sawhorse, 3 on the advanced";

    private static readonly string Firewood =
        $"on a {A(SplittingBlockPage, "splitting block")} with an axe or a maul: 6 per log, 8 on an advanced splitting block. "
        + $"The {A(ChopperPage, "automated chopper")} splits 8 per log";

    private static readonly string Debarking =
        $"on a {A(SawhorsePage, "sawhorse")} loaded with logs or a trunk: hold <hk>rightmouse</hk> with a "
        + $"{A(BarkSpudPage, "bark spud")}, or with an axe and a hammer in your offhand. Each log also gives bark";

    private static readonly string DebarkedUses =
        $"\n\nTwo of them go into a {A(StandardSawhorsePage, "standard sawhorse")}. They split into firewood on a "
        + $"{A(SplittingBlockPage, "splitting block")}, and the {A(SawmillPage, "automated sawmill")} saws them into "
        + "support beams.";

    private static LangReplacement Iw(string key, string text) => new("en", WoodworkingMods.IwModId + ":" + key, text);

    private static LangReplacement Le(string key, string text) => new("en", WoodworkingMods.LeModId + ":" + key, text);

    /// <summary>The two mods' exact English entries, rewritten whole. Immersive Woodworking's are
    /// removed from its translations too (<see cref="WoodworkingText.Removals"/>).</summary>
    public static readonly LangReplacement[] Replacements =
    [
        // Composed by Immersive Woodworking onto the game's wood items, by its settings: the
        // "-also" ones when the crafting-grid recipe is kept.
        Iw("iwsource-plank-text",
            $"Sawn from logs {Planks}. The {A(SawmillPage, "automated sawmill")} saws them too. A saw on a "
            + $"{A(TrunkPage, "tree trunk")} lying on the ground gives only 6 per log. They have no crafting-grid recipe."
            + SeeSawhorses),
        Iw("iwsource-plank-text-also",
            $"Can also be sawn from logs {Planks}, or on the {A(SawmillPage, "automated sawmill")}, alongside the "
            + "crafting grid." + SeeSawhorses),
        Iw("iwsource-supportbeam-text",
            $"Sawn from logs {Beams}. The {A(SawmillPage, "automated sawmill")} saws them too. They have no "
            + "crafting-grid recipe." + SeeSawhorses),
        Iw("iwsource-supportbeam-text-also",
            $"Can also be sawn from logs {Beams}, or on the {A(SawmillPage, "automated sawmill")}, alongside the "
            + "crafting grid." + SeeSawhorses),
        Iw("iwsource-firewood-text",
            $"Split from logs {Firewood}. It has no crafting-grid recipe." + SeeSplittingBlock),
        Iw("iwsource-firewood-text-also",
            $"Can also be split from logs {Firewood}, alongside the crafting grid." + SeeSplittingBlock),
        Iw("iwsource-stick-text",
            $"Also split from firewood on a {A(SplittingBlockPage, "splitting block")} with an axe, 2 per piece, or "
            + $"snapped out of beach driftwood by hand. Cutting the branches off a {A(TrunkPage, "tree trunk")} with a "
            + "knife gives sticks as well." + SeeSplittingBlock),
        Iw("iwsource-log-text",
            $"Place a log upright and right-click it with an axe to make a {A(SplittingBlockPage, "splitting block")}. "
            + $"On a splitting block, logs split into firewood. On a {A(SawhorsePage, "sawhorse")}, they are sawn into "
            + $"boards or support beams, or debarked. The {A(SawmillPage, "automated sawmill")} and "
            + $"{A(ChopperPage, "automated chopper")} take them too." + SeeOverview),
        Iw("iwsource-logsection-text",
            $"Split into firewood on a {A(SplittingBlockPage, "splitting block")}, or sawn into boards or support "
            + $"beams on the {A(SawmillPage, "automated sawmill")}. Sawhorses take only whole logs." + SeeOverview),
        Iw("iwsource-debarkedlog-text",
            $"Made {Debarking}. They have no crafting-grid recipe. An axe on a tree trunk lying on the ground, with a "
            + "hammer in your offhand, also gives them, but no bark." + DebarkedUses + SeeSawhorses),
        Iw("iwsource-debarkedlog-text-also",
            $"Can also be made {Debarking}, alongside the crafting grid." + DebarkedUses + SeeSawhorses),

        // The splitting block's own section, composed with {0} (the half-log or direct split, by
        // Immersive Woodworking's settings) and {1} (firewood to sticks, if allowed).
        Iw("choppingblock-handbook-text",
            "Make one by right-clicking an upright log with an axe. Breaking it gives it back, with its wood and "
            + "upgrades.\n\nPlace a log or half-log on the block with <hk>rightmouse</hk>, then hold <hk>rightmouse</hk> "
            + "with an axe to chop it. Take placed wood back with <strong><hk>ctrl</hk> + <hk>rightmouse</hk></strong>."
            + $"\n\n{{0}} {{1}}\n\nA log gives 6 firewood, 8 on an advanced splitting block. A {A(MaulPage, "maul")} "
            + "splits a whole log in one pass.\n\nUpgrade an empty block in three steps: debark it with a "
            + $"{A(BarkSpudPage, "bark spud")}, or an axe with a hammer in your offhand; bind it with 2 iron hoops; then "
            + "hold <hk>rightmouse</hk> with 8 iron nails and strips and a hammer in your offhand. Only an advanced "
            + $"splitting block can be the bed of an {A(ChopperPage, "automated chopper")}." + SeeSplittingBlock),
        Iw("halflog-handbook-text",
            $"Half of a log, split lengthwise. Put it back on a {A(SplittingBlockPage, "splitting block")} and chop it "
            + "into 3 firewood, or 4 on an advanced splitting block.\n\nIt is also a building block. Place it with "
            + "<hk>rightmouse</hk> and it becomes a half-log slab, split side out." + SeeSplittingBlock),
        Iw("maul-handbook-text",
            $"Lay a log on a {A(SplittingBlockPage, "splitting block")} and strike it with the maul: the whole log "
            + "splits into firewood in one pass, 6 pieces or 8 on an advanced splitting block. An axe takes two passes, "
            + $"through half-logs.\n\nForge a {A("item-immersivewoodworking:maulhead-copper", "maul head")} and haft it "
            + "onto a stick. Keep an axe for felling trees." + SeeSplittingBlock),
        Iw("chopper-handbook-text",
            "Place the chopper frame, then install its parts with <hk>rightmouse</hk> in any order: a "
            + $"{A("item-immersivewoodworking:chopperdrive", "drive")}, an {A("item-immersivewoodworking:chopperarm", "arm")} "
            + $"and an advanced {A(SplittingBlockPage, "splitting block")} as the bed, then a "
            + $"{A("item-immersivewoodworking:chopperhead-copper", "chopper head")} into the arm. A splitting block that "
            + "is not advanced is refused. Only the head can be taken back out, with <strong><hk>ctrl</hk> + "
            + "<hk>rightmouse</hk></strong>; break the frame to recover the other parts, the bed included.\n\nDrive the "
            + "frame with an axle from a windmill or water wheel. Load it with logs or firewood, by hand with "
            + "<hk>rightmouse</hk> or through a chute. A log splits straight into 8 firewood and firewood into sticks, "
            + "and each batch comes out of its output side. The metal of the head sets its durability." + SeeMachines),
        Iw("barkspud-handbook-text",
            $"Strips bark off logs. Hold <hk>rightmouse</hk> with it on a loaded {A(SawhorsePage, "sawhorse")} for "
            + $"debarked logs and bark, or on an empty {A(SplittingBlockPage, "splitting block")} to make it a debarked "
            + "one. An axe with a hammer in your offhand does the same, but a bark spud finds the special kinds of bark "
            + "more often.\n\nForge a "
            + $"{A("item-immersivewoodworking:barkspudhead-copper", "bark spud head")} and haft it onto a stick." + SeeBark),
        Iw("bark-tan-green-handbook-text",
            "Fresh tanning bark, stripped off a log on a sawhorse, or off a splitting block as you debark it. Left to "
            + "itself it dries into the dried kind. In a pile on the ground it dries as fast as in your inventory. In a "
            + "chest it dries four times slower. Keep it near a lit firepit, forge, clay oven or anything else that "
            + "warms you, in a pile or in a chest, to dry it up to twice as fast as in your inventory." + SeeBark),

        // Logging Expanded's help line for the last step of the advanced sawhorse, which takes 20
        // nails (BlockAdvancedSawhorseFrameB.RequiredNails), not the 10 it says.
        Le("wi-advancedsawhorseframeb-nail", "20 iron nails + hammer in offhand to complete"),
    ];

    /// <summary>Logging Expanded's block texts, which are pattern entries (one per block family).</summary>
    public static readonly LangReplacement[] PatternReplacements =
    [
        Le("block-handbooktext-loggingmod:sawhorse-*",
            "The first sawhorse, made of sticks and rope. Put exactly 18 sticks in a pile on the ground and right-click "
            + "it with a knife to make a large stick frame, then hold <hk>rightmouse</hk> on the frame with 4 rope."
            + "\n\nLoad it with a debranched tree trunk or up to 16 logs. An axe takes off one log at a time, a saw cuts "
            + "9 boards per log, and a saw with <hk>shift</hk> held cuts 2 support beams per log. A bark spud, or an "
            + "axe with a hammer in your offhand, debarks a log and drops its bark." + SeeSawhorses),
        Le("block-handbooktext-loggingmod:sawhorsestandard-*-*-*",
            "A sawhorse of boards and nails. Put exactly 36 boards of one wood in a pile on the ground and right-click "
            + "it with a hammer to make a board frame. Add 2 debarked logs, then hold <hk>rightmouse</hk> with 20 copper "
            + "or bronze nails and strips and a hammer in your offhand.\n\nIt works like the primitive sawhorse, but a "
            + "saw cuts 12 boards per log. Support beams stay at 2 per log." + SeeSawhorses),
        Le("block-handbooktext-loggingmod:sawhorseadvanced-*-*",
            "The best sawhorse, with iron plates and guide rails. Make a sawhorse frame as for the standard sawhorse, "
            + "then add 2 iron plates and 4 iron rods, and hold <hk>rightmouse</hk> with 20 iron nails and strips and a "
            + "hammer in your offhand.\n\nA saw cuts 18 boards per log, or 3 support beams with <hk>shift</hk> held. "
            + "From a trunk, an axe takes off 3 logs for every 2 it holds, and debarking gives 3 debarked logs for "
            + "every 2." + SeeSawhorses),
        Le("block-handbooktext-loggingmod:sawhorseframe-*",
            "Made from exactly 18 sticks in a pile on the ground, right-clicked with a knife. Hold <hk>rightmouse</hk> "
            + "on it with 4 rope to finish a primitive sawhorse, or add a bowl to start a trunk heating rack."
            + SeeSawhorses),
        Le("block-handbooktext-loggingmod:standardsawhorseframe-*",
            "A board frame with 2 debarked logs added. Hold <hk>rightmouse</hk> on it with 20 copper or bronze nails "
            + "and strips and a hammer in your offhand to finish a sawhorse, or add 2 iron plates to start an advanced "
            + "sawhorse." + SeeSawhorses),
        Le("block-handbooktext-loggingmod:plankframe-*-*",
            "Made from exactly 36 boards of one wood in a pile on the ground, right-clicked with a hammer. Add 2 "
            + "debarked logs to make a sawhorse frame, or 4 iron rods to start a trunk storage rack." + SeeSawhorses),
        // It takes 10 nails (BlockStorageRackFrame.RequiredNails), not the 12 it says.
        Le("block-handbooktext-loggingmod:trunkstorage-*-*-*",
            "A sturdy rack that stores up to 4 tree trunks. Put exactly 36 boards in a pile on the ground and "
            + "right-click it with a hammer to make a board frame, add 4 iron rods, then hold <hk>rightmouse</hk> with "
            + "10 iron nails and strips and a hammer in your offhand." + SeeTrunks),
    ];

    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _arrange;

    public override string Name => "the handbook guide";

    public override void Start(ICoreAPI api, Harmony harmony) => ArrangePages(api, unified: true);

    public override void Undo(ICoreAPI api) => Dispose();

    public override void Off(ICoreAPI api) => ArrangePages(api, unified: false);

    private void ArrangePages(ICoreAPI api, bool unified)
    {
        if (api.Side == EnumAppSide.Server)
        {
            api.ObjectCache[HiddenGuidesKey] = WoodworkingGuidePages.Hidden(unified).ToList();
            return;
        }
        if (api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is not { } handbook)
            return;
        _handbook = handbook;
        _arrange = pages => Arrange(pages, unified);
        handbook.OnInitCustomPages += _arrange;
    }

    private static void Arrange(List<GuiHandbookPage> pages, bool unified)
    {
        var refs = pages.Select(page => page is GuiHandbookTextPage text
            ? new HandbookPageRef(text.PageCode, text.Title)
            : new HandbookPageRef(page.PageCode, null)).ToList();
        var order = WoodworkingGuidePages.Arrange(refs, unified);
        var all = pages.ToList();
        pages.Clear();
        pages.AddRange(order.Select(i => all[i]));
    }

    /// <summary>The server's <c>ObjectCache</c> key under which the handbook pages hidden from players
    /// are listed, as <c>(string PageCode, string Title)</c> tuples, the title as its lang key.</summary>
    public const string HiddenGuidesKey = "handbook-hiddenGuides";

    public override void AssetsLoaded(ICoreAPI api) => ReplacePatterns(PatternReplacements, ModNames, api.Logger);

    public override void Dispose()
    {
        if (_handbook != null && _arrange != null)
            _handbook.OnInitCustomPages -= _arrange;
        _handbook = null;
        _arrange = null;
    }

    /// <summary>Replaces whole pattern entries (<c>*</c> in the key) where the game keeps them. A
    /// pattern the mod no longer has logs a warning naming <paramref name="modName"/>. Safe to run
    /// twice, as <see cref="LangText.Replace"/>.</summary>
    public static void ReplacePatterns(IEnumerable<LangReplacement> replacements, string modName, ILogger logger)
    {
        foreach (var language in replacements.GroupBy(replacement => replacement.Language))
        {
            if (!Lang.AvailableLanguages.TryGetValue(language.Key, out var translations))
                continue;
            translations.GetAllEntries(); // loads a language that is not the current one
            var type = translations.GetType();
            if (AccessTools.Field(type, "wildcardCache")?.GetValue(translations) is not Dictionary<string, string> wildcards
                || AccessTools.Field(type, "regexCache")?.GetValue(translations)
                    is not Dictionary<string, KeyValuePair<Regex, string>> patterns)
            {
                logger.Warning($"[seraphhorizons] {type.FullName} does not keep its wildcard and regex lang entries as "
                               + $"expected; the game changed, so {modName}'s block texts are not replaced");
                return;
            }
            foreach (var replacement in language)
            {
                string key = LangPatternKeys.StoredKey(replacement.Key);
                switch (LangPatternKeys.StoreOf(replacement.Key))
                {
                    case LangKeyStore.Wildcard when wildcards.ContainsKey(key):
                        wildcards[key] = replacement.Text;
                        continue;
                    case LangKeyStore.Regex when patterns.TryGetValue(key, out var pattern):
                        patterns[key] = new(pattern.Key, replacement.Text);
                        continue;
                }
                logger.Warning($"[seraphhorizons] No {replacement.Language} lang pattern {replacement.Key}; {modName} "
                               + "changed, so it is not replaced");
            }
        }
    }
}
