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
/// drew a different grade or tore the vein), and a deposit large enough, in a rock with more than one
/// grade, is no longer one grade. Prospect veins (always poor) and chimney cores (rich) are the expected exceptions, so a
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
        int varied = 0;
        var oneGrade = new List<string>();
        var keys = new[] { "copper", "iron", "tin", "zinc", "lead", "bismuth" }
            .SelectMany(m => new[] { (12_000, -12_000), (-12_000, 12_000), (12_000, 12_000), (-12_000, -12_000) }
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

            // Each chunk column (its own chunk pass) on its own: the drawn grade its blocks match best,
            // less the poorest grade where pockets can't make it (two or more grades above it in the
            // block's rock), since a prospect vein's trail, always poor, can fill a column of its own.
            double Fit(IEnumerable<OreBlock> blocks, int d)
            {
                var counted = blocks.Where(o => o.Index > 0 || Math.Min(d, o.Units.Length - 1) < 2).ToList();
                return counted.Count == 0 ? 0 : counted.Count(o => pockets.GradeAt(o.Units, d, o.X, o.Y, o.Z) == o.Index) / (double)counted.Count;
            }
            var columns = ores.GroupBy(o => (o.X / 32, o.Z / 32)).Where(g => g.Count() >= 100)
                .Select(c => (c.Key, Blocks: c.Count(), Ores: c.ToList(),
                    Best: Enumerable.Range(0, 4).Select(d => (Drawn: d, Share: Fit(c, d))).MaxBy(t => t.Share)))
                .ToList();
            var fits = columns.Where(c => c.Best.Share >= 0.8).ToList();
            output.WriteLine($"{key}: {result.OreBlocks} blocks, {result.Ingots:0} ingots, {makeup.Mix()?.Code}; {shares}");
            foreach (var c in columns)
                output.WriteLine($"  column {c.Key}: {c.Blocks} blocks, best drawn {OreTally.Grades[c.Best.Drawn]} at {c.Best.Share:0.00}");
            // A deposit mostly in no column that fits is not one of IOG's veins but district ore,
            // whose grade is drawn per block (90 % poor, 10 % medium off a rich band), left alone.
            if (fits.Sum(c => c.Blocks) * 2 < ores.Count)
            {
                output.WriteLine($"  not a pocketed vein (district ore?), skipped");
                continue;
            }
            // Every column that fits, fits the largest's drawn grade: no pass drew another or tore the
            // vein (in a rock that caps it, several drawn grades place alike).
            int drawn = fits.MaxBy(c => c.Blocks).Best.Drawn;
            var torn = fits.Where(c => Fit(c.Ores, drawn) < 0.8).ToList();
            Assert.True(torn.Count == 0,
                $"{key}: columns {string.Join(", ", torn.Select(c => $"{c.Key} (best {OreTally.Grades[c.Best.Drawn]})"))} don't fit drawn {OreTally.Grades[drawn]}");
            // The whole deposit, less the same poorest-grade trail blocks the columns leave out.
            double share = Fit(ores, drawn);
            output.WriteLine($"  drawn {OreTally.Grades[drawn]}, {share:0.00} of the blocks match it");
            Assert.True(share >= 0.8, $"{key}: only {share:0.00} of its blocks match its drawn grade");

            // In a rock with more than one grade, no longer one grade (the ends move by the end
            // spread, 0.3), once it spans enough pockets for their shares to show (a 650-block
            // deposit is a pocket or two).
            var common = ores.GroupBy(o => o.Units.Length).MaxBy(g => g.Count())!;
            if (common.Key > 1 && result.OreBlocks >= MinBlocks)
            {
                varied++;
                long top = makeup.Grades.Values.Max();
                double topShare = top / (double)makeup.Grades.Values.Sum();
                if (topShare >= 0.8 || makeup.Mix()!.Kind == GradeMix.Only)
                    oneGrade.Add($"{key}: drawn {OreTally.Grades[drawn]}, but {topShare:0.00} of it is one grade");
            }
        }
        // A vein crosses a few dozen pockets at most, so one can still come out mostly its drawn
        // grade by chance; before #944 every deposit did.
        Assert.True(oneGrade.Count * 4 <= varied, $"{oneGrade.Count} of {varied} deposits are one grade:\n" + string.Join("\n", oneGrade));
        Assert.True(varied >= 3, $"only {varied} deposits of {MinBlocks} blocks or more among the cells tried; pick another seed");
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
