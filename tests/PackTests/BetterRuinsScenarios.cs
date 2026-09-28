using Atlas.XUnit;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// BetterRuins ships ~800 schematics that name blocks by code. If another mod in the pack
/// (or a game update) removes or renames a block, worldgen silently places nothing there,
/// and only when that ruin happens to generate. Check every reference up front.
/// </summary>
[AtlasWorld]
public class BetterRuinsScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 300_000)]
    public void Every_schematic_block_code_resolves()
    {
        var api = World.Api;
        var assets = api.Assets.GetMany("worldgen/schematics/", "betterruins", true);
        Assert.NotEmpty(assets);

        var missing = new SortedDictionary<string, SortedSet<string>>();
        foreach (var asset in assets)
        {
            var schematic = asset.ToObject<BlockSchematic>();
            // Only palette entries that are actually placed matter: exported schematics can
            // keep stale BlockCodes entries for blocks removed before export. DecorIds pack the
            // palette id in the low 24 bits and the face above it.
            var placed = schematic.BlockIds
                .Concat(schematic.DecorIds.Select(d => (int)(d & 0xFFFFFF)))
                .ToHashSet();
            foreach (var (id, code) in schematic.BlockCodes)
            {
                if (!placed.Contains(id)) continue;
                if (Resolves(api.World, code)) continue;
                if (PackLock.KnownSchematicBlocks.Any(k => k.IsMatch(code.ToString()))) continue;
                if (!missing.TryGetValue(code.ToString(), out var users))
                    missing[code.ToString()] = users = new SortedSet<string>();
                users.Add(asset.Location.Path);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} block code(s) placed by BetterRuins schematics are not registered:\n" +
            string.Join("\n", missing.Select(kv =>
                $"  {kv.Key}  (in {kv.Value.Count} schematic(s), e.g. {kv.Value.First()})")));
    }

    /// <summary>
    /// Mirrors what schematic placement does: a code that isn't registered is looked up in
    /// the engine's legacy remaps (game:config/remaps.json, "/bir remapq new old") first.
    /// </summary>
    private static bool Resolves(IWorldAccessor world, AssetLocation code)
    {
        if (world.GetBlock(code) != null) return true;
        foreach (var remaps in BlockSchematic.BlockRemaps.Values)
        {
            if (remaps.TryGetValue(code.ToShortString(), out var newCode) ||
                remaps.TryGetValue(code.ToString(), out newCode))
            {
                if (world.GetBlock(new AssetLocation(newCode)) != null) return true;
            }
        }
        return false;
    }
}
