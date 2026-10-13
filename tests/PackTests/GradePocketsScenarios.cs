using System.Collections;
using System.Text;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, Ore/ (#944): grade pockets, on by default, in a new standard world with
/// a fixed seed. The prefix is bound to Interesting Ore Gen's <c>TryPlaceMainOre</c> and recorded
/// for the world, and real deposits, generated and measured by the deposit registry, carry the
/// pockets: every ore block of a deposit matches what <see cref="GradePockets"/> gives for one drawn
/// grade at its position, in every chunk column (each its own chunk pass over the vein, so no pass
/// drew a different grade or tore the vein), and a deposit drawn medium or rich is no longer one
/// grade. Prospect veins (always poor) and chimney cores (rich) are the expected exceptions, so a
/// share, not every block, has to match.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class GradePocketsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Seed = 944944;

    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;

    private OreSystem Ore => World.Api.ModLoader.GetModSystem<OreSystem>();

    private DepositService Deposits => Ore.Deposits ?? throw new Xunit.Sdk.XunitException("the deposit service is not bound");

    [AtlasScenario]
    public void Grade_pockets_are_bound_and_recorded_for_the_new_world()
    {
        Assert.True(Ore.World.GradePockets);
        var saved = Sapi.WorldManager.SaveGame.GetData(OreSystem.WorldRecordKey);
        Assert.NotNull(saved);
        Assert.True(OreWorldRecord.Parse(Encoding.UTF8.GetString(saved)).GradePockets);

        Assert.Null(GradePocketPlacement.Unsupported(World.Api));
        var method = AccessTools.Method(OreCellPlacement.GeneratorType!, GradePocketPlacement.MethodName);
        var patches = Harmony.GetPatchInfo(method);
        Assert.NotNull(patches);
        Assert.Contains(patches.Prefixes, p => p.owner == OreSystem.HarmonyId);
        Assert.NotNull(GradePocketPlacement.Pockets);
        Assert.Equal(GradePocketSettings.Default, GradePocketPlacement.Pockets!.Settings);
        // Prospect veins keep their own method, unpatched.
        var tracer = AccessTools.Method(OreCellPlacement.GeneratorType!, "TryPlaceTracerOre");
        Assert.DoesNotContain(Harmony.GetPatchInfo(tracer)?.Prefixes ?? [], p => p.owner == OreSystem.HarmonyId);
    }

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Generated_deposits_vary_by_pocket_around_one_drawn_grade()
    {
        var pockets = GradePocketPlacement.Pockets ?? throw new Xunit.Sdk.XunitException("grade pockets are not bound");
        var spawn = Sapi.World.DefaultSpawnPosition.AsBlockPos;
        var cells = new OreCells(Seed);
        int interior = 0;
        var oneGrade = new List<string>();
        var keys = new[] { "copper", "iron", "tin", "zinc", "lead", "bismuth" }
            .SelectMany(m => new[] { (12_000, -12_000), (-12_000, 12_000) }
                .Select(o => new DepositKey(m, cells.CellOf(m, spawn.X + o.Item1, spawn.Z + o.Item2))))
            .Distinct();
        foreach (var key in keys)
        {
            string metal = key.Kind;
            VerifyResult? result = null;
            Deposits.Verify(key, r => result = r);
            await World.Until(() => result != null, 9000);
            Assert.True(result != null, $"verifying {key} never answered");
            if (result!.Status != VerifyStatus.Measured || result.OreBlocks < 200 || result.Makeup is not { } makeup)
            {
                output.WriteLine($"{key}: {result.Status}, {result.OreBlocks} blocks, skipped");
                continue;
            }
            var ores = OreBlocks(metal, result.Candidate!);
            var shares = string.Join(", ", OreTally.Grades.Select(g => $"{g} {makeup.Grades.GetValueOrDefault(g) / (double)Math.Max(1, makeup.Grades.Values.Sum()):0.00}"));

            // The drawn grade every chunk pass agrees on: the one most blocks match.
            var (drawn, share) = Enumerable.Range(0, 4)
                .Select(d => (d, ores.Count(o => pockets.GradeAt(o.Units, d, o.X, o.Y, o.Z) == o.Index) / (double)ores.Count))
                .MaxBy(t => t.Item2);
            output.WriteLine($"{key}: {result.OreBlocks} blocks, {result.Ingots:0} ingots, {makeup.Mix()?.Code}; {shares}; drawn {OreTally.Grades[drawn]}, {share:0.00} of the blocks match it");
            Assert.True(share >= 0.8, $"{key}: only {share:0.00} of its blocks match one drawn grade");
            // Per column, less the poorest grade where pockets can't make it (drawn rich or better):
            // a prospect vein's trail, always poor, can fill a column of its own.
            foreach (var column in ores.Where(o => drawn < 2 || o.Index > 0)
                         .GroupBy(o => (o.X / 32, o.Z / 32)).Where(g => g.Count() >= 100))
            {
                double matched = column.Count(o => pockets.GradeAt(o.Units, drawn, o.X, o.Y, o.Z) == o.Index) / (double)column.Count();
                output.WriteLine($"  column {column.Key}: {column.Count()} blocks, {matched:0.00} match");
                Assert.True(matched >= 0.7, $"{key}: column {column.Key} matches the drawn grade in only {matched:0.00} of its blocks");
            }

            // Medium or rich in its commonest rock, which has a grade either side: no longer one
            // grade, once it spans enough pockets for their shares to show (a 650-block deposit is
            // a pocket or two, and came out 81 % medium).
            var common = ores.GroupBy(o => o.Units.Length).MaxBy(g => g.Count())!;
            if (drawn > 0 && drawn < common.Key - 1 && result.OreBlocks >= MinBlocks)
            {
                interior++;
                long top = makeup.Grades.Values.Max();
                double topShare = top / (double)makeup.Grades.Values.Sum();
                if (topShare >= 0.8 || makeup.Mix()!.Kind == GradeMix.Only)
                    oneGrade.Add($"{key}: drawn {OreTally.Grades[drawn]}, but {topShare:0.00} of it is one grade");
            }
        }
        Assert.True(oneGrade.Count == 0, string.Join("\n", oneGrade));
        Assert.True(interior > 0, $"no deposit of {MinBlocks} blocks or more drawn medium or rich among the cells tried; pick another seed");
    }

    // About ten pockets' volume.
    private const int MinBlocks = 5000;

    private sealed record OreBlock(int X, int Y, int Z, int Index, double[] Units);

    /// <summary>The metal's ore blocks in the nine columns the registry measured, each with its
    /// index in, and the units of, the grades its host rock allows (Interesting Ore Gen's lists).</summary>
    private List<OreBlock> OreBlocks(string metal, DepositCandidate candidate)
    {
        var byId = new Dictionary<int, (int Index, double[] Units)>();
        var gens = Sapi.ModLoader.Systems.OfType<GenDeposits>().SelectMany(g => g.Deposits ?? [])
            .Select(v => v.GeneratorInst)
            .Where(g => g != null && OreCellPlacement.GeneratorType!.IsInstanceOfType(g));
        foreach (var gen in gens)
        {
            var dict = (IDictionary)AccessTools.Field(gen.GetType(), GradePocketPlacement.FieldName).GetValue(gen)!;
            foreach (var entry in dict.Values)
            {
                if (entry == null || AccessTools.Field(entry.GetType(), "Blocks").GetValue(entry) is not Block[] blocks) continue;
                if (GradePocketPlacement.Units(blocks) is not { } units) continue;
                for (int i = 0; i < blocks.Length; i++) byId.TryAdd(blocks[i].BlockId, (i, units));
            }
        }
        var list = new List<OreBlock>();
        var spot = OreCells.ChunkOf(candidate.X, candidate.Z);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        for (int cy = 0; cy < Sapi.WorldManager.MapSizeY / 32; cy++)
        {
            int cx = spot.X + dx, cz = spot.Z + dz;
            if (Sapi.WorldManager.GetChunk(cx, cy, cz) is not { } chunk) continue;
            chunk.Unpack();
            for (int i = 0; i < 32 * 32 * 32; i++)
            {
                int id = chunk.Data.GetBlockIdUnsafe(i);
                if (id <= 0 || !byId.TryGetValue(id, out var at)) continue;
                if (Sapi.World.Blocks[id].Code is not { } code || OreTally.Parse(code.Path)?.Metal != metal) continue;
                list.Add(new OreBlock(cx * 32 + i % 32, cy * 32 + i / (32 * 32), cz * 32 + i / 32 % 32, at.Index, at.Units));
            }
        }
        return list;
    }
}
