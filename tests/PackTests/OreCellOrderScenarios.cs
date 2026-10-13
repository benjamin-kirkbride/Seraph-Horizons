using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// An ore cell's vein should not depend on the order its chunk columns generate in (#946). Found by
/// the ore survey's census (tools/ore-survey/README.md, "Against a full scan"): it does. Interesting
/// Ore Gen's TryPlaceMainOre writes to any chunk the worldgen accessor reaches and draws from the
/// vein's random sequence only where it finds host rock there, and the pack's cell rule reads the
/// try's centre block in the anchor chunk as it is at each pass (vanilla cracked rock lands on it
/// during the anchor's own pass, or the chunk is not reachable). This generates the columns around
/// one anchor in one order, deletes them and generates them again in another, and compares the
/// metal's ore per column. Expected to fail until that is fixed.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class OreCellOrderScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Seed = 424242;
    private const int Reach = 3;   // GenDeposits' reach: the columns a vein from the anchor can be in
    private const int Cleared = 9; // columns deleted between the two orders (the reach and its neighbours' halo)

    private OreCellPlacement Placement =>
        World.Api.ModLoader.GetModSystem<OreSystem>().Placement ?? throw new Xunit.Sdk.XunitException("ore cells are not bound");

    // Skipped while it fails as expected; run by hand (remove Skip) to check a fix. Last run
    // (2026-10-13, seed 424242, copper cell 102,102): 2,243 then 4,134 blocks, 8 of 49 columns
    // differ, the anchor's own column 0 then 1,368.
    [AtlasScenario(Skip = "Expected to fail: ore cell veins depend on generation order (#946); run by hand to check a fix")]
    public async Task A_cell_vein_is_the_same_whatever_order_its_columns_generate_in()
    {
        var sapi = (ICoreServerAPI)World.Api;
        await World.JoinAtSpawn("surveyor");
        var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
        foreach (var metal in new[] { "copper", "iron", "tin" })
        {
            var cell = Placement.Cells.CellOf(metal, spawn.X, spawn.Z);
            // Settle the cell first: generating each active spot places the vein or moves on.
            for (int round = 0; round < OreCells.SpotCount; round++)
            {
                var state = Placement.StateOf(metal, cell);
                if (state.None || state.Placed) break;
                await Load(Placement.SpotsOf(metal, cell)[state.Active].Chunk);
            }
            var settled = Placement.StateOf(metal, cell);
            if (!settled.Placed) continue;
            var anchor = Placement.SpotsOf(metal, cell)[settled.Active].Chunk;
            var around = new List<ChunkPos>();
            for (int dx = -Reach; dx <= Reach; dx++)
                for (int dz = -Reach; dz <= Reach; dz++)
                    around.Add(new ChunkPos(anchor.X + dx, anchor.Z + dz));

            // Order A: the anchor first, then outwards (as the census and Verify do).
            Clear(sapi, anchor);
            foreach (var c in around.OrderBy(c => Math.Max(Math.Abs(c.X - anchor.X), Math.Abs(c.Z - anchor.Z)))) await Load(c);
            var first = around.ToDictionary(c => c, c => OreIn(sapi, c, metal));
            // Order B: the far corner first, row by row towards the anchor and past it (as a walk or a scan's tiles do).
            Clear(sapi, anchor);
            foreach (var c in around.OrderByDescending(c => c.X).ThenByDescending(c => c.Z)) await Load(c);
            var second = around.ToDictionary(c => c, c => OreIn(sapi, c, metal));

            var differ = around.Where(c => first[c] != second[c]).ToList();
            output.WriteLine($"{metal} cell {cell} anchor {anchor}: {first.Values.Sum()} then {second.Values.Sum()} blocks; "
                + $"{differ.Count} of {around.Count} columns differ: "
                + string.Join(", ", differ.Select(c => $"({c.X - anchor.X},{c.Z - anchor.Z}) {first[c]}/{second[c]}")));
            Assert.True(differ.Count == 0, $"{metal}'s vein at {anchor} came out differently in two generation orders");
            return;
        }
        throw new Xunit.Sdk.XunitException("no copper, iron or tin deposit placed near the spawn");
    }

    private async Task Load(ChunkPos c)
    {
        var sapi = (ICoreServerAPI)World.Api;
        bool loaded = false;
        sapi.WorldManager.LoadChunkColumnPriority(c.X, c.Z, new ChunkLoadOptions { OnLoaded = () => loaded = true });
        await World.Until(() => loaded, 3000);
    }

    private static void Clear(ICoreServerAPI sapi, ChunkPos anchor)
    {
        for (int dx = -Cleared; dx <= Cleared; dx++)
            for (int dz = -Cleared; dz <= Cleared; dz++)
                sapi.WorldManager.DeleteChunkColumn(anchor.X + dx, anchor.Z + dz);
    }

    private static int OreIn(ICoreServerAPI sapi, ChunkPos chunk, string metal)
    {
        int n = 0;
        for (int cy = 0; cy < sapi.WorldManager.MapSizeY / 32; cy++)
        {
            if (sapi.WorldManager.GetChunk(chunk.X, cy, chunk.Z) is not { } c) continue;
            c.Unpack();
            for (int i = 0; i < 32 * 32 * 32; i++)
            {
                int id = c.Data.GetBlockIdUnsafe(i);
                if (id > 0 && sapi.World.Blocks[id]?.Code is { } code && OreMetals.MetalOf(OreMetals.OreOfBlockPath(code.Path)) == metal) n++;
            }
        }
        return n;
    }
}
