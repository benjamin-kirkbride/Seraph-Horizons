using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using Vintagestory.API.Common;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Trading.Schematics;

/// <summary>
/// Takes every sold schematic out of the places the world hands them out (#468).
///
/// <list type="bullet">
/// <item><b>Loot lists</b> (<see cref="ScrubTypeAssets"/>): in the item and block type assets, after
/// the game's patch loader (AssetsLoaded, 0.05) and before the types are read from them (0.2), any
/// array entry with a <c>code</c> naming a sold schematic and a <c>chance</c>: stack randomizers'
/// stacks (BetterRuins' gear randomizer, Abyssal Depths' four), loot vessels, panning drops. Done on
/// the assets because the collectibles copy those lists in OnLoaded, before any later hook, and the
/// entries are appended by other mods' patches (<c>/-</c>, <c>addmerge</c>), so no JSON patch can
/// name their index. Only files whose text contains "schematic" are parsed.</item>
/// <item><b>Structures</b> (<see cref="Bind"/>): a postfix on <c>BlockSchematic.Remap</c>, which every
/// worldgen structure's schematic runs once loaded (ruins, BetterRuins' story locations, the
/// game's), maps each sold schematic in its item code table to <see cref="SchematicTable.Replacement"/>,
/// so the chest that held one holds parchment. Player-made schematics (WorldEdit) are plain
/// BlockSchematics and are left alone.</item>
/// </list>
/// Story rewards outside chests are kept: Tobias' dialogue still gives the translocator schematic
/// his translocator's repair needs.
/// </summary>
public static class LootScrub
{
    private static SchematicTable? _table;

    public static int ScrubTypeAssets(ICoreAPI api, SchematicTable table)
    {
        var needle = Encoding.UTF8.GetBytes("schematic");
        int removed = 0, files = 0;
        foreach (string category in new[] { "itemtypes/", "blocktypes/" })
        {
            foreach (var asset in api.Assets.GetMany(category, null, false))
            {
                if (asset.Data is not { } data || data.AsSpan().IndexOf(needle) < 0) continue;
                JToken token;
                try
                {
                    token = JToken.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
                }
                catch (JsonException e)
                {
                    api.Logger.Warning("[seraphhorizons] Schematics: {0} does not parse, its loot is left as it is: {1}", asset.Location, e.Message);
                    continue;
                }
                int n = Strip(token, table);
                if (n == 0) continue;
                removed += n;
                files++;
                asset.Data = Encoding.UTF8.GetBytes(token.ToString(Formatting.None));
            }
        }
        api.Logger.Notification("[seraphhorizons] Schematics: {0} loot entries naming a schematic taken out of {1} type files", removed, files);
        return removed;
    }

    /// <summary>Removes, anywhere under <paramref name="token"/>, every array entry that names a sold
    /// schematic and has a chance; returns how many.</summary>
    public static int Strip(JToken token, SchematicTable table)
    {
        int removed = 0;
        if (token is not JContainer root) return 0;
        foreach (var array in root.DescendantsAndSelf().OfType<JArray>().ToList())
        {
            foreach (var entry in array.Children<JObject>().ToList())
            {
                if (entry["code"] is JValue { Type: JTokenType.String } code && entry["chance"] != null && table.IsSold((string)code!))
                {
                    entry.Remove();
                    removed++;
                }
            }
        }
        return removed;
    }

    public static void Bind(Harmony harmony, SchematicTable table)
    {
        _table = table;
        harmony.Patch(AccessTools.Method(typeof(BlockSchematic), nameof(BlockSchematic.Remap)),
            postfix: new HarmonyMethod(typeof(LootScrub), nameof(RemapPostfix)));
    }

    public static void Unbind() => _table = null;

    public static void RemapPostfix(BlockSchematic __instance)
    {
        if (_table is not { } table || __instance is not BlockSchematicStructure) return;
        foreach (var (id, code) in __instance.ItemCodes.ToList())
            if (code != null && table.IsSold(code.ToString()))
                __instance.ItemCodes[id] = new AssetLocation(table.Replacement);
    }
}
