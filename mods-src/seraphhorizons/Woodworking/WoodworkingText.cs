using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The two mods' text, matched to the unified system on both sides in AssetsLoaded (a client
/// changes it when the server runs the tweak, whatever its own switch says). English is
/// rewritten; where Immersive Woodworking's translations say what the English no longer does,
/// they are removed, so those languages show the English (<see cref="LangText.Remove"/>).
///
/// Immersive Woodworking's chopping block is named "Splitting block". The tiers' own names are
/// this mod's (<c>seraphhorizons:splittingblock-*</c>, <see cref="Core.SplittingBlockTiers"/>).
/// </summary>
public sealed class WoodworkingText : WoodworkingPart
{
    /// <summary>Immersive Woodworking's translations; Logging Expanded ships English only.</summary>
    public static readonly string[] IwTranslations = ["de", "pt-br", "ru", "zh-cn"];

    /// <summary>Exact-passage edits of the English (<see cref="LangText.Apply"/>).</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "immersivewoodworking:block-choppingblock", "Chopping block", "Splitting block"),
        new("en", "immersivewoodworking:block-choppingblock-name", "{0} chopping block", "{0} splitting block"),
    ];

    /// <summary>Whole English entries rewritten (<see cref="LangText.Replace"/>).</summary>
    public static readonly LangReplacement[] Replacements = WoodworkingGuide.Replacements;

    /// <summary>Immersive Woodworking's sentences that it composes into a text replaced here
    /// (<c>WoodworkingHandbookInfo.ComposeOwnPages</c>: the splitting block's section takes the
    /// half-log or direct split as <c>{0}</c> and firewood to sticks as <c>{1}</c>). Their English
    /// stays; their translations go with the text's, or a translated sentence would sit in the
    /// English text.</summary>
    public static readonly string[] ComposedFragments =
    [
        "immersivewoodworking:choppingblock-handbook-frag-halflog",
        "immersivewoodworking:choppingblock-handbook-frag-halflog-atplayer",
        "immersivewoodworking:choppingblock-handbook-frag-direct",
        "immersivewoodworking:choppingblock-handbook-frag-direct-atplayer",
        "immersivewoodworking:choppingblock-handbook-frag-sticks",
    ];

    /// <summary>The keys whose English this changes, and the fragments composed into them,
    /// removed from Immersive Woodworking's translations.</summary>
    public static IEnumerable<LangRemoval> Removals =>
        from key in LangEdits.Select(edit => edit.Key).Concat(Replacements.Select(replacement => replacement.Key))
            .Concat(ComposedFragments)
            .Where(key => key.StartsWith(WoodworkingMods.IwModId + ":", StringComparison.Ordinal)).Distinct()
        from language in IwTranslations
        select new LangRemoval(language, key);

    public override string Name => "the text";

    public override void AssetsLoaded(ICoreAPI api)
    {
        LangText.Apply(LangEdits, "Immersive Woodworking or Logging Expanded", api.Logger);
        LangText.Replace(Replacements, "Immersive Woodworking or Logging Expanded", api.Logger);
        LangText.Remove(Removals, "Immersive Woodworking", api.Logger);
    }
}
