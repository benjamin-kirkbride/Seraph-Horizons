using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Where ore processing's how-to lives: the guide page "Ore processing" (<c>config/handbook/oreprocessing.json</c>)
/// and a handbook section on each ore item's page. The tooltips keep one terse status line
/// (<see cref="ItemOreProduct.StatusText"/>); an item's handbook page shows the same text as its tooltip,
/// so what moved out of it is here.
/// <list type="bullet">
/// <item>The pack's own forms that are worked one way whatever the ore (roasted rock, concentrate, amalgam,
/// sponge, litharge, bullion, rich lead) declare their section in their type file.</item>
/// <item>Raw and crystallised ore (vanilla's items) and crushed and ground ore are worked one way as an
/// oxide and another as a sulfide, which is the ore's class in <c>config/ore-processing.json</c>, so
/// <see cref="OreProcessingItems.Apply"/> adds their section by it (<see cref="RockSection"/>), on the
/// server; the attributes go to the client with the item.</item>
/// </list>
/// </summary>
public static class OreHandbook
{
    /// <summary>The section title every ore item's how-to section has.</summary>
    public const string SectionTitle = "seraphhorizons:oreproduct-handbook-title";

    /// <summary>The rock form's section text: <c>{form}-handbook-text</c>, a sulfide's
    /// <c>{form}-sulfide-handbook-text</c>; raw ore's and crystallised ore's under <c>oreprocessing-</c>,
    /// crushed and ground ore's under <c>oreproduct-</c>.</summary>
    public static string RockSection(string form, bool sulfide)
    {
        string prefix = form is "rawore" or "crystallisedore" ? "seraphhorizons:oreprocessing-" : "seraphhorizons:oreproduct-";
        // Crystallised ore crushes before anything else is done with it, either way.
        return prefix + form + (sulfide && form != "crystallisedore" ? "-sulfide" : "") + "-handbook-text";
    }

    /// <summary>Adds a section to the item's handbook page (its attributes, which the server sends the
    /// client), once: a section of that title already there is left as it is.</summary>
    public static void AddSection(CollectibleObject item, string title, string text)
    {
        var attributes = item.Attributes?.Token as JObject ?? new JObject();
        if (attributes["handbook"] is not JObject handbook)
            attributes["handbook"] = handbook = new JObject();
        if (handbook["extraSections"] is not JArray sections)
            handbook["extraSections"] = sections = new JArray();
        if (sections.OfType<JObject>().Any(s => (string?)s["title"] == title))
            return;
        sections.Add(new JObject { ["title"] = title, ["text"] = text });
        item.Attributes = new JsonObject(attributes);
    }
}
