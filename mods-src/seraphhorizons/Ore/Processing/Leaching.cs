using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Leaching borax, saltpeter and alum (#742; README "Ore processing: leaching borax, saltpeter and
/// alum"), set on the server once every item and block is loaded and patched, with ore processing
/// on. The barrel and cooking recipes are asset files (<see cref="OreLeaching"/>); this makes the
/// raw forms raw:
/// <list type="bullet">
/// <item>vanilla's borax and alum ore (<c>game:ore-borax</c>, <c>game:ore-alum</c>) stack to
/// <see cref="OreProducts.RawStack"/> and lose the quern's grinding (borax to powder) and the
/// pulverizer's crushing (alum to crushed alum), so they turn into crystals only by leaching;</item>
/// <item>every block dropping saltpeter (<see cref="OreLeaching.Saltpeter"/>) drops raw saltpeter
/// instead, at the same rate;</item>
/// <item>the raw forms' handbook pages get the leaching section the new items carry in their type
/// files.</item>
/// </list>
/// The recipes that took alum's crushed item take alum powder instead
/// (<see cref="OreProcessingSystem.RetargetRecipes"/>).
/// </summary>
public static class Leaching
{
    public const string SectionTitle = "seraphhorizons:oreprocessing-leaching-title";
    public const string SectionText = "seraphhorizons:oreprocessing-leaching-text";

    /// <summary>What was set, for the log and the scenarios.</summary>
    public sealed class Report
    {
        public List<string> RawItems = [];
        public List<string> SaltpeterBlocks = [];
        public List<string> Missing = [];
    }

    public static Report Apply(IWorldAccessor world, ILogger logger)
    {
        var report = new Report();
        foreach (var code in OreLeaching.VanillaRaw)
        {
            if (world.GetItem(new AssetLocation(code)) is not { } item)
            {
                report.Missing.Add(code);
                continue;
            }
            item.MaxStackSize = OreProducts.RawStack;
            item.GrindingProps = null;
            item.CrushingProps = null;
            AddSection(item);
            report.RawItems.Add(code);
        }

        var raw = world.GetItem(new AssetLocation(OreLeaching.RawSaltpeter));
        if (raw == null)
            report.Missing.Add(OreLeaching.RawSaltpeter);
        else
        {
            var saltpeter = new AssetLocation(OreLeaching.Saltpeter);
            foreach (var block in world.Blocks)
            {
                if (block?.Code == null || block.Drops == null)
                    continue;
                bool changed = false;
                foreach (var drop in block.Drops)
                {
                    if (drop?.Code == null || !drop.Code.Equals(saltpeter) || drop.Type != EnumItemClass.Item)
                        continue;
                    drop.Code = raw.Code.Clone();
                    drop.ResolvedItemstack = new ItemStack(raw, drop.ResolvedItemstack?.StackSize ?? 1);
                    changed = true;
                }
                if (changed)
                    report.SaltpeterBlocks.Add(block.Code.ToString());
            }
        }

        logger.Notification("[seraphhorizons] Ore processing: leaching: {0} raw (stack {1}, no quern or crusher); "
                            + "{2} blocks drop raw saltpeter in place of saltpeter",
            string.Join(", ", report.RawItems), OreProducts.RawStack, report.SaltpeterBlocks.Count);
        if (report.Missing.Count > 0)
            logger.Warning("[seraphhorizons] Ore processing: leaching: no {0}; left as it is", string.Join(", ", report.Missing));
        return report;
    }

    /// <summary>Adds the leaching section to the item's handbook page (its attributes, which the
    /// server sends the client).</summary>
    private static void AddSection(CollectibleObject item)
    {
        var attributes = item.Attributes?.Token as JObject ?? new JObject();
        if (attributes["handbook"] is not JObject handbook)
            attributes["handbook"] = handbook = new JObject();
        if (handbook["extraSections"] is not JArray sections)
            handbook["extraSections"] = sections = new JArray();
        if (sections.OfType<JObject>().Any(s => (string?)s["title"] == SectionTitle))
            return;
        sections.Add(new JObject { ["title"] = SectionTitle, ["text"] = SectionText });
        item.Attributes = new JsonObject(attributes);
    }
}
