using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SeraphHorizons.ChiselRotationFix;

/// <summary>
/// /chiselfix check|i_backed_up_my_world_first_repair [radius] [position]: repairs chiseled blocks
/// that worldgen placed with the wrong materials before the patch was installed. Only the server
/// console or a player with controlserver (as for /wgen) can run it. "check" changes nothing; the
/// repair's name asks for a backup first, since it rewrites the save.
/// </summary>
public static class ChiselRepairCommand
{
    public const int DefaultRadius = 256;
    public const string Repair = "i_backed_up_my_world_first_repair";

    public const string BackupWarning =
        $"Back up your world before running /chiselfix {Repair}: it rewrites blocks in the save, and there is no undo. "
        + "Strongly recommended, every time.";

    public static void Register(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("chiselfix")
            .WithDescription("Find, or repair, chiseled blocks that rotated worldgen structures placed with the wrong "
                             + "materials (VintageStory-Issues#9495). Looks at structures within radius blocks "
                             + $"(horizontally) of the position, or of you. Run check first. {BackupWarning}")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("check")
                .WithDescription("Report what the repair would change, without changing anything. Run this first.")
                .WithArgs(parsers.OptionalInt("radius", DefaultRadius), parsers.OptionalWorldPosition("position"))
                .HandleWith(args => Run(api, args, apply: false))
            .EndSubCommand()
            .BeginSubCommand(Repair)
                .WithDescription("Rewrite the materials of chiseled blocks that are exactly as the bug left them. "
                                 + BackupWarning)
                .WithArgs(parsers.OptionalInt("radius", DefaultRadius), parsers.OptionalWorldPosition("position"))
                .HandleWith(args => Run(api, args, apply: true))
            .EndSubCommand();
    }

    private static TextCommandResult Run(ICoreServerAPI api, TextCommandCallingArgs args, bool apply)
    {
        int radius = (int)args[0];
        if (args[1] is not Vec3d at)
            return TextCommandResult.Error("No position: give one, or run this as a player");
        if (radius < 0)
            return TextCommandResult.Error("The radius can't be negative");
        if (!OnTransformedPatch.Applied)
            return TextCommandResult.Error("The chiselrotationfix patch isn't applied (see the server log), "
                                           + "so the right materials can't be worked out");

        var report = new ChiselRepair(api).Run(at.AsBlockPos, radius, apply);
        string text = report.Describe(api, apply);
        if (!apply && report.Repairable > 0)
            text += "\n" + BackupWarning;
        api.Logger.Notification("[chiselrotationfix] /chiselfix {0}:\n{1}", apply ? Repair : "check", text);
        return TextCommandResult.Success(text);
    }
}

/// <summary>
/// Works out, for each worldgen structure near a position, which schematic and rotation placed it
/// and what every chiseled block in it was made of: as the unpatched game placed it, and as it
/// should be. A chiseled block whose stored materials are exactly the first is rewritten to the
/// second; anything else (edited by a player, another structure, a schematic since changed) is left
/// as it is. Nothing besides those materials is written to the save.
///
/// The map region records each structure as "domain:file.json/structurecode" plus its bounding box,
/// but not the rotation. GenStructures (scfg.Structures) still holds the loaded schematics: each
/// rotation whose size fits the box is compared with the blocks in the world, and only a clear best
/// match is used. Story structures and villages come from other systems and are reported as unknown.
/// </summary>
public class ChiselRepair
{
    /// <summary>Share of a schematic's ordinary blocks that must be found in place.</summary>
    public const double MinMatch = 0.6;

    /// <summary>How far the best reading of a structure must be ahead of any other.</summary>
    public const double MinLead = 0.1;

    private const int MaxCompared = 20_000;
    private const int MinCompared = 20;

    private static readonly AccessTools.FieldRef<GenStructures, WorldGenStructuresConfig?> Scfg =
        AccessTools.FieldRefAccess<GenStructures, WorldGenStructuresConfig?>("scfg");
    private static readonly AccessTools.FieldRef<WorldGenStructure, BlockSchematicStructure[][]?> SchematicDatas =
        AccessTools.FieldRefAccess<WorldGenStructure, BlockSchematicStructure[][]?>("schematicDatas");
    private static readonly AccessTools.FieldRef<WorldGenStructure, Dictionary<int, Dictionary<int, int>>?> RockRemaps =
        AccessTools.FieldRefAccess<WorldGenStructure, Dictionary<int, Dictionary<int, int>>?>("resolvedRockTypeRemaps");
    private static readonly AccessTools.FieldRef<WorldGenStructure, int[]?> LayerBlockIds =
        AccessTools.FieldRefAccess<WorldGenStructure, int[]?>("replacewithblocklayersBlockids");

    /// <summary>
    /// How WorldGenStructure placed a schematic, which decides how its chiseled blocks' materials
    /// were mapped (BlockSchematicStructure.PlaceRespectingBlockLayers, PlaceReplacingBlocks, or
    /// BlockSchematic.Place).
    /// </summary>
    private enum Placement { Layers, ReplacingBlocks, Plain }

    private sealed record Source(string Code, BlockSchematicStructure Schematic, Placement Mode,
                                 Dictionary<int, Dictionary<int, int>>? Remaps, HashSet<int> LayerIds, EnumOrigin Origin);

    /// <summary>A schematic rotated as the patched game (Correct) and the unpatched one (Buggy) rotate it.</summary>
    private sealed record Rotated(BlockSchematic? Correct, BlockSchematic? Buggy, string? Error);

    private sealed record Reading(Source Source, int Angle, BlockPos Start, double Score, Dictionary<int, int> RockVotes);

    private readonly ICoreServerAPI _api;
    private readonly IWorldAccessor _world;
    private readonly Dictionary<(BlockSchematicStructure, int), Rotated> _rotated = new();
    private List<Source>? _sources;

    public ChiselRepair(ICoreServerAPI api)
    {
        _api = api;
        _world = api.World;
    }

    /// <summary>Checks, and with <paramref name="apply"/> repairs, every recorded structure within
    /// <paramref name="radius"/> blocks of <paramref name="center"/> horizontally.</summary>
    public RepairReport Run(BlockPos center, int radius, bool apply)
    {
        var report = new RepairReport();
        foreach (var structure in Nearby(center, radius, report))
            report.Structures.Add(Repair(structure, apply));
        return report;
    }

    /// <summary>Checks, and with <paramref name="apply"/> repairs, one recorded structure.</summary>
    public StructureResult Repair(GeneratedStructure structure, bool apply)
    {
        var result = new StructureResult { Code = structure.Code, Location = structure.Location.Clone() };
        if (!Loaded(structure.Location))
            return result.Skip("not all of it is loaded; go closer and run this again");

        var sources = SourcesFor(structure.Code);
        if (sources.Count == 0)
            return result.Skip("not a structure GenStructures knows (a story structure, a village, or from a mod no longer installed)");

        var readings = sources
            .SelectMany(source => new[] { 0, 90, 180, 270 }.SelectMany(angle => Read(source, angle, structure.Location)))
            .OrderByDescending(r => r.Score)
            .ToList();
        if (readings.Count == 0)
            return result.Skip("no schematic of it has this size at any rotation");
        var best = readings[0];
        result.File = best.Source.Schematic.FromFile?.ToShortString();
        result.Angle = best.Angle;
        result.Score = best.Score;
        if (best.Score < MinMatch)
            return result.Skip($"no schematic matches the blocks there (best {best.Score:P0})");

        // A ruin that looks much the same turned around can leave several readings too close to
        // call. Each is kept, and a chiseled block is only rewritten if it is exactly as one of them
        // says the bug left it and every such reading agrees on what it should be.
        var contenders = new List<Reading>();
        foreach (var r in readings.TakeWhile(r => r.Score > best.Score - MinLead && r.Score >= MinMatch))
            if (!contenders.Any(c => Same(c, r)))
                contenders.Add(r);
        if (contenders.All(r => r.Angle == 0))
            return result.Skip("not rotated, so not affected");
        if (contenders.Count > 1)
            result.ChosenBy = "too close to call between " + string.Join(", ", contenders.Select(r => $"{r.Angle}° ({r.Score:P0})"))
                              + ", so only chiseled blocks they agree on are repaired";

        CheckChiseledBlocks(contenders.Select(r => Predict(r)).ToList(), result, apply);
        return result;
    }

    private IEnumerable<GeneratedStructure> Nearby(BlockPos center, int radius, RepairReport report)
    {
        int regionSize = _api.WorldManager.RegionSize;
        int minX = center.X - radius, maxX = center.X + radius, minZ = center.Z - radius, maxZ = center.Z + radius;
        var seen = new HashSet<(string, int, int, int)>();
        for (int rx = minX / regionSize; rx <= maxX / regionSize; rx++)
        {
            for (int rz = minZ / regionSize; rz <= maxZ / regionSize; rz++)
            {
                var region = _api.WorldManager.GetMapRegion(rx, rz);
                if (region == null)
                {
                    report.UnloadedRegions++;
                    continue;
                }
                // The game replaces the list rather than adding to it, so this one is a snapshot.
                foreach (var structure in region.GeneratedStructures)
                {
                    var loc = structure.Location;
                    if (loc.X2 < minX || loc.X1 > maxX || loc.Z2 < minZ || loc.Z1 > maxZ)
                        continue;
                    if (seen.Add((structure.Code, loc.X1, loc.Y1, loc.Z1)))
                        yield return structure;
                }
            }
        }
    }

    private bool Loaded(Cuboidi loc)
    {
        for (int cx = loc.X1 / GlobalConstants.ChunkSize; cx <= (loc.X2 - 1) / GlobalConstants.ChunkSize; cx++)
            for (int cy = loc.Y1 / GlobalConstants.ChunkSize; cy <= (loc.Y2 - 1) / GlobalConstants.ChunkSize; cy++)
                for (int cz = loc.Z1 / GlobalConstants.ChunkSize; cz <= (loc.Z2 - 1) / GlobalConstants.ChunkSize; cz++)
                    if (_api.WorldManager.GetChunk(cx, cy, cz) == null)
                        return false;
        return true;
    }

    /// <summary>
    /// The schematics a recorded code can stand for. GenStructures records
    /// <c>FromFile.GetNameWithDomain() + "/" + structure code</c>, and several structures can share
    /// a code (BetterRuins' undergroundruins) or a file.
    /// </summary>
    private List<Source> SourcesFor(string code)
    {
        int slash = code.LastIndexOf('/');
        if (slash <= 0)
            return [];
        string file = code[..slash], structureCode = code[(slash + 1)..];
        return AllSources()
            .Where(s => s.Code == structureCode && s.Schematic.FromFile?.GetNameWithDomain() == file)
            .ToList();
    }

    private List<Source> AllSources()
    {
        if (_sources != null)
            return _sources;
        _sources = [];
        var config = _api.ModLoader.GetModSystem<GenStructures>() is { } gen ? Scfg(gen) : null;
        foreach (var structure in config?.Structures ?? [])
        {
            var remaps = RockRemaps(structure);
            var mode = structure.Placement != EnumStructurePlacement.Underground ? Placement.Layers
                : remaps != null ? Placement.ReplacingBlocks : Placement.Plain;
            var layerIds = (LayerBlockIds(structure) ?? []).ToHashSet();
            foreach (var rotations in SchematicDatas(structure) ?? [])
                if (rotations?.Length > 0 && rotations[0] != null)
                    _sources.Add(new Source(structure.Code, rotations[0], mode, remaps, layerIds, structure.Origin));
        }
        return _sources;
    }

    private static bool Same(Reading a, Reading b) =>
        a.Source.Schematic == b.Source.Schematic && a.Angle == b.Angle && a.Start.Equals(b.Start)
        && a.Source.Mode == b.Source.Mode && a.Source.Remaps == b.Source.Remaps;

    /// <summary>
    /// The schematic rotated by <paramref name="angle"/> as worldgen rotates it
    /// (BlockSchematicStructure.Unpack: a packed clone of the loaded schematic, turned around its
    /// bottom center), once with the patch and once without.
    /// </summary>
    private Rotated Rotate(BlockSchematicStructure schematic, int angle)
    {
        if (_rotated.TryGetValue((schematic, angle), out var done))
            return done;
        Rotated rotated;
        if (angle == 0)
        {
            rotated = new Rotated(schematic, schematic, null);
        }
        else
        {
            try
            {
                var correct = schematic.ClonePacked();
                correct.TransformWhilePacked(_world, EnumOrigin.BottomCenter, angle);
                var buggy = schematic.ClonePacked();
                OnTransformedPatch.RunUnpatched(() => buggy.TransformWhilePacked(_world, EnumOrigin.BottomCenter, angle));
                rotated = new Rotated(correct, buggy, null);
            }
            catch (KeyNotFoundException)
            {
                // A block entity where no block is: the game can't rotate it either.
                rotated = new Rotated(null, null, "the game can't rotate this schematic");
            }
        }
        _rotated[(schematic, angle)] = rotated;
        return rotated;
    }

    /// <summary>Where the schematic could have been placed from, given the recorded box.</summary>
    private static IEnumerable<BlockPos> Starts(Source source, BlockSchematic schematic, Cuboidi loc)
    {
        var start = new BlockPos(loc.X1, loc.Y1, loc.Z1);
        yield return start;
        // TryGenerateUnderground records the box from AdjustStartPos but, without rock remaps,
        // places the schematic at the unadjusted position.
        if (source.Mode == Placement.Plain)
        {
            var unadjusted = schematic.AdjustStartPos(start.Copy(), source.Origin);
            var placed = new BlockPos(2 * start.X - unadjusted.X, 2 * start.Y - unadjusted.Y, 2 * start.Z - unadjusted.Z);
            if (!placed.Equals(start))
                yield return placed;
        }
    }

    private IEnumerable<Reading> Read(Source source, int angle, Cuboidi loc)
    {
        var schematic = Rotate(source.Schematic, angle).Correct;
        if (schematic == null || schematic.SizeX != loc.X2 - loc.X1 || schematic.SizeY != loc.Y2 - loc.Y1
            || schematic.SizeZ != loc.Z2 - loc.Z1)
            yield break;
        foreach (var start in Starts(source, schematic, loc))
        {
            var votes = new Dictionary<int, int>();
            double score = Score(source, schematic, start, votes);
            yield return new Reading(source, angle, start, score, votes);
        }
    }

    /// <summary>
    /// The share of the schematic's ordinary blocks found at their place in the world, sampled. A
    /// block counts if it is there as is, or as one of its rock variants (each counts as a vote for
    /// that rock). Air, meta blocks, liquids and soil that block layers replace are not compared.
    /// </summary>
    private double Score(Source source, BlockSchematic schematic, BlockPos start, Dictionary<int, int> votes)
    {
        var accessor = _world.BlockAccessor;
        var pos = new BlockPos(start.dimension);
        int step = Math.Max(1, schematic.Indices.Count / MaxCompared);
        int compared = 0, matched = 0;
        for (int i = 0; i < schematic.Indices.Count; i += step)
        {
            if (!schematic.BlockCodes.TryGetValue(schematic.BlockIds[i], out var code)
                || _world.GetBlock(code) is not Block block || block.Id == 0 || block.ForFluidsLayer
                || block.Code.Path.StartsWith("meta-")
                || (source.Mode == Placement.Layers && (source.LayerIds.Contains(block.Id) || block.CustomBlockLayerHandler)))
                continue;
            uint index = schematic.Indices[i];
            pos.Set(start.X + (int)(index & 0x3FF), start.Y + (int)((index >> 20) & 0x3FF), start.Z + (int)((index >> 10) & 0x3FF));
            int found = accessor.GetBlock(pos, BlockLayersAccess.Solid).Id;
            compared++;
            bool asRock = false;
            if (source.Remaps != null && source.Remaps.TryGetValue(block.Id, out var byRock))
            {
                foreach (var (rock, id) in byRock)
                {
                    if (id != found)
                        continue;
                    votes[rock] = votes.GetValueOrDefault(rock) + 1;
                    asRock = true;
                }
            }
            if (found == block.Id || asRock)
                matched++;
        }
        return compared < MinCompared ? 0 : (double)matched / compared;
    }

    /// <summary>
    /// The rock the placement replaced granite with. PlaceRespectingBlockLayers uses the top rock
    /// at the schematic's center, which the map chunk still holds; underground placement samples
    /// a stone block in the area, so take the rock the structure's own blocks were turned into.
    /// Without either, take the rock that explains the most chiseled blocks.
    /// </summary>
    private int PickRock(Reading reading, Rotated rotated)
    {
        var remaps = reading.Source.Remaps;
        if (remaps == null || reading.Source.Mode == Placement.Plain)
            return 0;
        var schematic = rotated.Correct!;
        if (reading.Source.Mode == Placement.Layers)
        {
            var center = new BlockPos(reading.Start.X + schematic.SizeX / 2, reading.Start.Y, reading.Start.Z + schematic.SizeZ / 2);
            var mapChunk = _world.BlockAccessor.GetMapChunkAtBlockPos(center);
            if (mapChunk?.TopRockIdMap is { } topRock)
                return topRock[center.Z % GlobalConstants.ChunkSize * GlobalConstants.ChunkSize + center.X % GlobalConstants.ChunkSize];
        }
        if (reading.RockVotes.Count > 0)
            return reading.RockVotes.MaxBy(v => v.Value).Key;

        var rocks = remaps.Values.SelectMany(byRock => byRock.Keys).Distinct().OrderBy(id => id).ToList();
        return rocks.Count == 0 ? 0 : rocks.MaxBy(rock =>
            Predict(reading, rotated, rock).Values.Count(p =>
                _world.BlockAccessor.GetBlockEntity(p.Pos) is BlockEntityMicroBlock { BlockIds: { } stored }
                && !p.Buggy.SequenceEqual(p.Correct)
                && (Holds(stored, p.Buggy, p.Source) || Holds(stored, p.Correct, p.Source))));
    }

    /// <summary>What a reading says one chiseled block was placed with by the bug, and should hold.</summary>
    private sealed record Prediction(BlockPos Pos, int[] Buggy, int[] Correct, Source Source);

    private Dictionary<(int, int, int), Prediction> Predict(Reading reading)
    {
        var rotated = Rotate(reading.Source.Schematic, reading.Angle);
        return rotated.Correct == null ? [] : Predict(reading, rotated, PickRock(reading, rotated));
    }

    /// <summary>
    /// Every chiseled block of the reading, by position: its materials as the unpatched game placed
    /// them and as they should be.
    /// </summary>
    private Dictionary<(int, int, int), Prediction> Predict(Reading reading, Rotated rotated, int rock)
    {
        var correctSchematic = rotated.Correct!;
        var buggySchematic = rotated.Buggy!;
        var predictions = new Dictionary<(int, int, int), Prediction>();
        foreach (var (index, correctData) in correctSchematic.BlockEntities)
        {
            if (!buggySchematic.BlockEntities.TryGetValue(index, out var buggyData)
                || (correctSchematic.DecodeBlockEntityData(correctData)["materials"] as IntArrayAttribute)?.value is not int[] correctIds
                || (buggySchematic.DecodeBlockEntityData(buggyData)["materials"] as IntArrayAttribute)?.value is not int[] buggyIds
                || correctIds.Length != buggyIds.Length)
                continue;
            var pos = new BlockPos(reading.Start.X + (int)(index & 0x3FF), reading.Start.Y + (int)((index >> 20) & 0x3FF),
                                   reading.Start.Z + (int)((index >> 10) & 0x3FF), reading.Start.dimension);
            predictions[(pos.X, pos.Y, pos.Z)] = new Prediction(pos,
                Placed(buggyIds, buggySchematic.BlockCodes, reading.Source, rock),
                Placed(correctIds, correctSchematic.BlockCodes, reading.Source, rock), reading.Source);
        }
        return predictions;
    }

    /// <summary>
    /// Rewrites each chiseled block that is exactly as one of the readings says the bug left it,
    /// when every reading that says so agrees on what it should be and none says it was placed right.
    /// With one reading, that is simply: exactly as the bug left it.
    /// </summary>
    private void CheckChiseledBlocks(List<Dictionary<(int, int, int), Prediction>> readings, StructureResult result, bool apply)
    {
        var accessor = _world.BlockAccessor;
        foreach (var key in readings.SelectMany(r => r.Keys).Distinct())
        {
            var said = readings.Where(r => r.ContainsKey(key)).Select(r => r[key]).ToList();
            if (said.All(p => p.Buggy.SequenceEqual(p.Correct)))
                continue;

            if (accessor.GetBlockEntity(said[0].Pos) is not BlockEntityMicroBlock { BlockIds: { } stored } be)
            {
                // Only the best reading's chiseled blocks count as gone: the others' positions are
                // where a rotation the structure probably wasn't placed at would have put one.
                if (readings[0].ContainsKey(key))
                {
                    result.Affected++;
                    result.Gone++;
                }
                continue;
            }
            result.Affected++;
            var explained = said.Where(p => Holds(stored, p.Buggy, p.Source)).ToList();
            if (explained.Count == 0)
            {
                if (said.Any(p => Holds(stored, p.Correct, p.Source)))
                    result.AlreadyCorrect++;
                else
                    result.Edited++;
                continue;
            }
            if (explained.Any(p => p.Buggy.SequenceEqual(p.Correct))
                || explained.Any(p => !p.Correct.SequenceEqual(explained[0].Correct)))
            {
                result.Unclear++;
                continue;
            }
            var (buggy, correct) = (explained[0].Buggy, explained[0].Correct);

            // OnPlacementBySchematic reworks the cuboids of a meta-blocklayer material, so a
            // material that became one, or stopped being one, needs more than new ids.
            bool layerMeta = Enumerable.Range(0, correct.Length).Any(i => correct[i] != buggy[i]
                && (correct[i] == BlockMicroBlock.BlockLayerMetaBlockId || buggy[i] == BlockMicroBlock.BlockLayerMetaBlockId));
            if (layerMeta || correct.Any(id => _world.GetBlock(id) == null))
            {
                result.Unrepairable++;
                continue;
            }
            result.Repairable++;
            if (!apply)
                continue;
            var fixedIds = (int[])stored.Clone();
            Array.Copy(correct, fixedIds, correct.Length);
            be.BlockIds = fixedIds;
            be.MarkDirty(redrawOnClient: true);
            result.Repaired++;
        }
    }

    /// <summary>
    /// A chiseled block's materials once placed: looked up in the rotated schematic's BlockCodes
    /// (BlockEntityMicroBlock.OnLoadCollectibleMappings, through BlockCodesTmpForRemap, which has
    /// the rock replaced), then the rock replaced again on world ids by OnPlacementBySchematic,
    /// which only PlaceRespectingBlockLayers passes the remaps to.
    /// </summary>
    private int[] Placed(int[] ids, Dictionary<int, AssetLocation> codes, Source source, int rock)
    {
        var placed = new int[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            if (codes.TryGetValue(id, out var code) && _world.GetBlock(code) is Block block)
                id = source.Mode == Placement.Plain ? block.Id : Replace(source.Remaps, block.Id, rock);
            if (source.Mode == Placement.Layers)
                id = Replace(source.Remaps, id, rock);
            placed[i] = id;
        }
        return placed;
    }

    private static int Replace(Dictionary<int, Dictionary<int, int>>? remaps, int id, int rock) =>
        remaps != null && remaps.TryGetValue(id, out var byRock) && byRock.TryGetValue(rock, out var replaced) ? replaced : id;

    /// <summary>
    /// Whether stored materials are exactly <paramref name="placed"/>. PlaceRespectingBlockLayers
    /// can append the block-layer block when one is a meta-blocklayer.
    /// </summary>
    private static bool Holds(int[] stored, int[] placed, Source source)
    {
        if (stored.Length == placed.Length)
            return stored.SequenceEqual(placed);
        return source.Mode == Placement.Layers && stored.Length == placed.Length + 1
            && placed.Contains(BlockMicroBlock.BlockLayerMetaBlockId) && stored.Take(placed.Length).SequenceEqual(placed);
    }
}

public class RepairReport
{
    public List<StructureResult> Structures { get; } = [];

    /// <summary>Map regions in range that aren't loaded, so their structures weren't looked at.</summary>
    public int UnloadedRegions { get; set; }

    public int Repairable => Structures.Sum(s => s.Repairable);
    public int Repaired => Structures.Sum(s => s.Repaired);

    public string Describe(ICoreServerAPI api, bool applied)
    {
        var spawn = api.World.DefaultSpawnPosition.AsBlockPos;
        var text = new StringBuilder();
        foreach (var s in Structures)
        {
            text.Append($"{s.Code} at {s.Location.X1 - spawn.X} {s.Location.Y1} {s.Location.Z1 - spawn.Z}: ");
            if (s.Skipped != null)
                text.Append("skipped, ").Append(s.Skipped);
            else
                text.Append(s.Describe(applied));
            text.Append('\n');
        }
        int skipped = Structures.Count(s => s.Skipped != null);
        text.Append($"{Structures.Count} structure(s), {skipped} skipped; ")
            .Append(applied ? $"{Repaired} chiseled block(s) repaired" : $"{Repairable} chiseled block(s) to repair");
        if (UnloadedRegions > 0)
            text.Append($"; {UnloadedRegions} map region(s) in range not loaded");
        return text.ToString();
    }
}

public class StructureResult
{
    public string Code { get; set; } = "";
    public Cuboidi Location { get; set; } = new();
    public string? File { get; set; }
    public int Angle { get; set; }
    public double Score { get; set; }

    /// <summary>How a close call between readings was settled, or null.</summary>
    public string? ChosenBy { get; set; }

    /// <summary>Why the structure wasn't checked, or null.</summary>
    public string? Skipped { get; private set; }

    /// <summary>Chiseled blocks the bug gives different materials.</summary>
    public int Affected { get; set; }

    /// <summary>Of those, exactly as the bug left them.</summary>
    public int Repairable { get; set; }
    public int Repaired { get; set; }
    public int AlreadyCorrect { get; set; }

    /// <summary>Holding other materials than either: left alone.</summary>
    public int Edited { get; set; }

    /// <summary>No chiseled block there any more.</summary>
    public int Gone { get; set; }

    /// <summary>As the bug left them, but not repairable by rewriting the ids.</summary>
    public int Unrepairable { get; set; }

    /// <summary>As the bug left them by one reading, but the close readings disagree on what they should be.</summary>
    public int Unclear { get; set; }

    internal StructureResult Skip(string reason)
    {
        Skipped = reason;
        return this;
    }

    public string Describe(bool applied)
    {
        var parts = new List<string> { $"{File} at {Angle}°, {Score:P0} of its blocks in place" };
        if (ChosenBy != null) parts.Add(ChosenBy);
        if (Affected == 0)
            parts.Add("no chiseled block affected");
        else
            parts.Add(applied ? $"{Repaired} of {Affected} affected chiseled block(s) repaired"
                              : $"{Repairable} of {Affected} affected chiseled block(s) to repair");
        if (AlreadyCorrect > 0) parts.Add($"{AlreadyCorrect} already right");
        if (Edited > 0) parts.Add($"{Edited} changed since (left alone)");
        if (Gone > 0) parts.Add($"{Gone} gone");
        if (Unrepairable > 0) parts.Add($"{Unrepairable} not repairable (block-layer material)");
        if (Unclear > 0) parts.Add($"{Unclear} left alone (the close rotations disagree on them)");
        return string.Join(", ", parts);
    }
}
