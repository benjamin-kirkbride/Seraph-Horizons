using System.Text.RegularExpressions;
using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, UnifiedWoodworking's handbook (<see cref="WoodworkingGuide"/>): the six
/// guide pages, Immersive Woodworking's and Logging Expanded's rewritten text, and the translations
/// that now fall back to the English. The handbook dialog is client only, so which pages it shows
/// is <see cref="WoodworkingGuidePages"/>'s unit tests; this checks what the server can see: the
/// page assets, the lang entries after the tweak's edits, and that every link points somewhere.
/// </summary>
public partial class WoodworkingScenarios
{
    private static IDictionary<string, string> Entries(string language) => Lang.AvailableLanguages[language].GetAllEntries();

    /// <summary>Text that names something the unified system retired.</summary>
    private static readonly Regex[] Retired =
    [
        new(@"chopping block", RegexOptions.IgnoreCase),
        new(@"pit ?saw", RegexOptions.IgnoreCase),
        new(@"splitting log\b", RegexOptions.IgnoreCase),
        new(@"pair of sawhorses", RegexOptions.IgnoreCase),
        new(@"chisel", RegexOptions.IgnoreCase),
        new(@"<hk>toolmode</hk>", RegexOptions.IgnoreCase),
        new(@"handbook://block-immersivewoodworking:sawhorse\b"),
        new(@"handbook://item-immersivewoodworking:pitsaw"),
        new(@"handbook://block-loggingmod:(advanced|bound|debarked)?splittinglog"),
        new(@"handbook://introduction\b"),
    ];

    /// <summary>Entries shown only on what is retired and hidden (or on a guide page the tweak
    /// drops), so they may keep naming it.</summary>
    private static readonly Regex RetiredKeys = new(
        "^(immersivewoodworking:(craftinginfo-woodworking-|pitsaw-handbook-|sawhorse-handbook-|block-sawhorse$|blockhelp-sawhorse-"
        + "|blockinfo-sawhorse-|ingameerror-sawhorse-|item-pitsaw|block-choppingblock-\\*$)"
        + "|loggingmod:(introduction-|block-(debarked|bound|advanced)?splittinglog-|block-handbooktext-loggingmod:(advanced)?splittinglog-"
        + "|wi-(advanced)?splittinglog-|wi-debarkedsplittinglog-|wi-boundsplittinglog-|wi-sawhorse-debark-chisel$))");

    private static readonly string[] Domains = [WoodworkingMods.IwModId + ":", WoodworkingMods.LeModId + ":"];

    [AtlasScenario]
    public void The_tweak_runs()
    {
        Assert.True(World.Api.LoadModConfig("seraphhorizons.json")["UnifiedWoodworking"].AsBool(false));
        var system = World.Api.ModLoader.GetModSystem<SeraphHorizonsSystem>();
        Assert.True(system.Woodworking.Active, "the tweak did not bind; see the server log");
    }

    // Fails when a page asset or its lang is missing, or the mods' guides changed code or title
    // (the client would then show both: update WoodworkingGuidePages.Replaced).
    [AtlasScenario]
    public void The_six_pages_and_the_two_they_replace_are_there()
    {
        var pages = World.Api.Assets.GetMany("config/handbook/")
            .Where(asset => asset.Location.Path.EndsWith(".json", StringComparison.Ordinal))
            .Select(asset => (asset.Location.Domain, Page: asset.ToObject<JObject>()))
            .Where(p => p.Page != null)
            .Select(p => (p.Domain, Code: (string?)p.Page["pageCode"], Title: (string?)p.Page["title"], Text: (string?)p.Page["text"]))
            .ToList();
        // machine oil's, gear reclamation's, the crucible furnace's and the eidolon's pages are not woodworking ones
        var ours = pages.Where(p => p.Domain == "seraphhorizons" && p.Code != SeraphHorizons.Mod.MachineOil.MachineOilSystem.GuidePageCode
                                    && p.Code != SeraphHorizons.Mod.GearReclamation.GearReclamationSystem.GuidePageCode
                                    && p.Code != SeraphHorizons.Mod.CrucibleFurnace.CrucibleFurnaceSystem.GuidePageCode
                                    && p.Code != SeraphHorizons.Mod.Eidolon.EidolonGuideSystem.GuidePageCode).ToList();
        Assert.Equal(WoodworkingGuidePages.Pages.Select(p => (p.PageCode, p.TitleKey(), p.TextKey())).Order(),
            ours.Select(p => (p.Code!, p.Title!, p.Text!)).Order());
        var en = Entries("en");
        Assert.All(WoodworkingGuidePages.Pages, page =>
        {
            Assert.True(en.ContainsKey(page.TitleKey()), page.TitleKey());
            Assert.True(en.ContainsKey(page.TextKey()), page.TextKey());
            Assert.True(page.TextKey().Length < 255, "a page's text under 255 characters is a lang key");
        });
        Assert.All(WoodworkingGuidePages.Replaced, replaced =>
            Assert.Contains(pages, p => p.Code == replaced.PageCode && p.Title == replaced.TitleKey));
    }

    // Fails when a mod drops or renames a key: update WoodworkingGuide.Replacements or
    // PatternReplacements (the server log has a warning naming it).
    [AtlasScenario]
    public void Every_rewritten_text_reads_as_ours()
    {
        var en = Entries("en");
        Assert.All(WoodworkingGuide.Replacements, r => Assert.Equal(r.Text, en[r.Key]));
        Assert.All(WoodworkingGuide.PatternReplacements, r =>
        {
            Assert.NotEqual(LangKeyStore.Exact, LangPatternKeys.StoreOf(r.Key));
            Assert.Equal(r.Text, Lang.AvailableLanguages["en"].GetMatchingIfExists(LangPatternKeys.Example(r.Key)));
        });
        // The splitting block's section keeps Immersive Woodworking's two composed fragments.
        Assert.Contains("FRAGMENT-A FRAGMENT-B",
            Lang.GetL("en", "immersivewoodworking:choppingblock-handbook-text", "FRAGMENT-A", "FRAGMENT-B"));
        // And the handbook pages of Logging Expanded's blocks read the new text.
        Assert.Contains("Woodworking: Sawhorses",
            Lang.GetMatchingIfExists("loggingmod:block-handbooktext-loggingmod:sawhorsestandard-oak-copper-north"));
    }

    // Fails when Immersive Woodworking ships a new translation: add it to WoodworkingText.IwTranslations.
    [AtlasScenario]
    public void Every_rewritten_iw_text_falls_back_to_english_in_its_translations()
    {
        var shipped = World.Api.Assets.GetMany("lang/", WoodworkingMods.IwModId, loadAsset: false)
            .Select(asset => asset.Location.GetName())
            .Where(name => name.EndsWith(".json"))
            .Select(name => name[..^".json".Length])
            .Order();
        Assert.Equal(WoodworkingText.IwTranslations.Append("en").Order(), shipped);

        var removals = WoodworkingText.Removals.ToList();
        Assert.All(WoodworkingGuide.Replacements.Where(r => r.Key.StartsWith(Domains[0])), r =>
            Assert.Equal(WoodworkingText.IwTranslations.Order(),
                removals.Where(removal => removal.Key == r.Key).Select(removal => removal.Language).Order()));
        Assert.All(removals, removal =>
        {
            Assert.False(Entries(removal.Language).ContainsKey(removal.Key), $"{removal.Language} {removal.Key}");
            Assert.True(Entries("en").ContainsKey(removal.Key), removal.Key);
        });
        Assert.Equal(Lang.GetL("en", "immersivewoodworking:iwsource-firewood-text"),
            Lang.GetL("de", "immersivewoodworking:iwsource-firewood-text"));
    }

    // Immersive Woodworking composes the splitting block's section from fragments
    // (WoodworkingHandbookInfo.ComposeOwnPages). Fails when a translation would show a translated
    // fragment inside the English text, e.g. after it adds a fragment: add it to
    // WoodworkingText.ComposedFragments.
    [AtlasScenario]
    public void A_composed_section_reads_the_same_in_every_translation_as_in_english()
    {
        const string prefix = "immersivewoodworking:choppingblock-handbook-frag-";
        var fragments = Entries("en").Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order().ToList();
        Assert.Equal(fragments, WoodworkingText.ComposedFragments.Order());
        string Composed(string language, string split) => Lang.GetL(language, "immersivewoodworking:choppingblock-handbook-text",
            Lang.GetL(language, split), Lang.GetL(language, prefix + "sticks"));
        foreach (var language in WoodworkingText.IwTranslations)
            foreach (var split in fragments.Where(k => !k.EndsWith("-sticks")))
                Assert.True(Composed("en", split) == Composed(language, split), $"{language} {split}:\n{Composed(language, split)}");
    }

    [AtlasScenario]
    public void No_woodworking_text_names_a_retired_station()
    {
        var en = Lang.AvailableLanguages["en"];
        var texts = en.GetAllEntries().Select(e => (e.Key, e.Value)).ToList();
        // Pattern entries (block-handbooktext-loggingmod:sawhorse-*) live apart from the exact ones.
        var type = en.GetType();
        texts.AddRange(((Dictionary<string, string>)AccessTools.Field(type, "wildcardCache").GetValue(en)!)
            .Select(e => (e.Key + "*", e.Value)));
        texts.AddRange(((Dictionary<string, KeyValuePair<Regex, string>>)AccessTools.Field(type, "regexCache").GetValue(en)!)
            .Select(e => (e.Key, e.Value.Value)));

        var woodworking = texts.Where(t => Domains.Any(d => t.Key.StartsWith(d, StringComparison.Ordinal))).ToList();
        Assert.True(woodworking.Count > 300, $"only {woodworking.Count} entries of the two mods");
        var offending = woodworking
            .Where(t => !RetiredKeys.IsMatch(t.Key))
            .SelectMany(t => Retired.Where(r => r.IsMatch(t.Value)).Select(r => $"{t.Key}: \"{r}\""))
            .ToList();
        foreach (var line in offending)
            output.WriteLine(line);
        Assert.Empty(offending);
    }

    [AtlasScenario]
    public void Every_guide_link_opens_a_page()
    {
        var en = Entries("en");
        var texts = WoodworkingGuidePages.Pages.Select(p => en[p.TextKey()])
            .Concat(WoodworkingGuide.Replacements.Select(r => r.Text))
            .Concat(WoodworkingGuide.PatternReplacements.Select(r => r.Text));
        var links = texts.SelectMany(text => Regex.Matches(text, "handbook://([^\"]+)\"").Select(m => m.Groups[1].Value))
            .Distinct().Order().ToList();
        Assert.True(links.Count > 20, $"only {links.Count} links");
        var ownCodes = WoodworkingGuidePages.Pages.Select(p => p.PageCode).ToHashSet();
        var hidden = RetiredStations.Hidden.Select(h => h.Asset.Domain + ":" + h.Code).ToList();
        var broken = new List<string>();
        foreach (string link in links)
        {
            output.WriteLine(link);
            if (ownCodes.Contains(link))
                continue;
            CollectibleObject? found = link.StartsWith("item-") ? W.GetItem(new AssetLocation(link["item-".Length..]))
                : link.StartsWith("block-") ? W.GetBlock(new AssetLocation(link["block-".Length..]))
                : null;
            if (found == null || found.Id == 0 || found.Code == null)
                broken.Add($"{link}: no such page");
            else if (found.Attributes?["handbook"]?["exclude"].AsBool(false) == true)
                broken.Add($"{link}: excluded from the handbook");
            else if (hidden.Any(code => found.Code.ToString().StartsWith(code + "-") || found.Code.ToString() == code))
                broken.Add($"{link}: retired");
            else if (!(found.CreativeInventoryTabs?.Length > 0 || found.CreativeInventoryStacks?.Length > 0
                       || found.Attributes?["handbook"]?["include"].AsBool(false) == true))
                broken.Add($"{link}: has no handbook page (not in the creative inventory)");
        }
        Assert.Empty(broken);
    }
}
