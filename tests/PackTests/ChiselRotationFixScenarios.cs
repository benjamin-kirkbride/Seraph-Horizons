using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/chiselrotationfix: a chiseled block in a rotated schematic keeps its materials
/// (https://github.com/anegostudios/VintageStory-Issues/issues/9495). Rotating a schematic
/// resolves the materials to world ids, and placement then maps them through BlockCodes a second
/// time. Without the mod the first scenario fails; when the game fixes the bug both pass without
/// it, and the mod can go. The /chiselfix scenarios place a ruin as the unpatched game does and
/// repair it.
/// </summary>
[AtlasWorld]
public class ChiselRotationFixScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private Block BlockOf(string code) =>
        W.GetBlock(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no block {code}");

    // A one-block schematic whose BlockCodes holds the collision: the chiseled block's material
    // is granite, and granite's world id is also a key, naming overlay-damagedstone. This is the
    // shape of the BetterRuins ruins that render see-through in this pack.
    [AtlasScenario]
    public void Rotated_chiseled_block_keeps_its_material_when_ids_collide()
    {
        var chisel = BlockOf("game:chiseledblock");
        var granite = BlockOf("game:rock-granite");
        var decoy = BlockOf("game:overlay-damagedstone");
        const int chiselKey = 1, graniteKey = 2;
        Assert.DoesNotContain(granite.Id, new[] { chiselKey, graniteKey, chisel.Id });

        var schematic = new BlockSchematic { SizeX = 1, SizeY = 1, SizeZ = 1 };
        schematic.BlockCodes[chiselKey] = chisel.Code;
        schematic.BlockCodes[graniteKey] = granite.Code;
        schematic.BlockCodes[granite.Id] = decoy.Code;
        schematic.Indices.Add(0);
        schematic.BlockIds.Add(chiselKey);
        var tree = new TreeAttribute();
        tree.SetString("blockCode", chisel.Code.ToShortString());
        tree["materials"] = new IntArrayAttribute(new[] { graniteKey });
        // One full 16³ cuboid of material 0: x2, y2 and z2 at 15.
        tree["cuboids"] = new IntArrayAttribute(new[] { (15 << 12) | (15 << 16) | (15 << 20) });
        schematic.BlockEntities[0] = schematic.StringEncodeTreeAttribute(tree);

        foreach (int angle in new[] { 90, 180, 270 })
        {
            var rotated = schematic.ClonePacked();
            rotated.TransformWhilePacked(W, EnumOrigin.BottomCenter, angle);
            var placed = Assert.Single(rotated.BlockEntities.Values);
            Assert.Equal(new[] { granite.Code }, PlacedMaterials(rotated, placed));
        }
    }

    // Every chiseled block in every BetterRuins schematic, at each rotation worldgen uses, ends
    // up made of the blocks it is made of unrotated (each turned the same way).
    [AtlasScenario(TimeoutMs = 600_000)]
    public void BetterRuins_chiseled_blocks_keep_their_materials_when_rotated()
    {
        var assets = W.Api.Assets.GetMany("worldgen/schematics/", "betterruins", true);
        Assert.NotEmpty(assets);

        var wrong = new List<string>();
        var untransformable = new List<string>();
        int checkedMaterials = 0;
        foreach (var asset in assets)
        {
            var schematic = asset.ToObject<BlockSchematic>();
            if (schematic?.BlockEntities == null || schematic.BlockEntities.Count == 0)
                continue;
            schematic.Remap();
            var unrotated = schematic.BlockEntities.Values
                .SelectMany(data => PlacedMaterials(schematic, data))
                .ToHashSet();
            if (unrotated.Count == 0)
                continue;

            foreach (int angle in new[] { 90, 180, 270 })
            {
                var expected = unrotated
                    .Select(code => W.GetBlock(code)?.GetRotatedBlockCode(angle) ?? code)
                    .ToHashSet();
                var rotated = schematic.ClonePacked();
                try
                {
                    rotated.TransformWhilePacked(W, EnumOrigin.BottomCenter, angle);
                }
                catch (KeyNotFoundException)
                {
                    // The game can't rotate a schematic with a block entity where no block is
                    // (TransformWhilePacked looks the position up in BlocksUnpacked). Not this bug.
                    untransformable.Add(asset.Location.Path);
                    break;
                }
                foreach (var data in rotated.BlockEntities.Values)
                {
                    foreach (var code in PlacedMaterials(rotated, data))
                    {
                        checkedMaterials++;
                        if (!expected.Contains(code))
                            wrong.Add($"{asset.Location.Path} at {angle}°: {code}");
                    }
                }
            }
        }

        Console.WriteLine($"[chiselrotationfix] checked {checkedMaterials} materials; not rotatable: "
                          + string.Join(", ", untransformable));
        Assert.True(checkedMaterials > 0, "no chiseled block materials found in BetterRuins schematics");
        Assert.True(wrong.Count == 0,
            $"{wrong.Count} rotated chiseled block material(s) are not a material of the unrotated schematic:\n  "
            + string.Join("\n  ", wrong.Distinct().Take(40)));
    }

    /// <summary>
    /// The blocks a stored chiseled block is made of once placed: its "materials", looked up in
    /// the schematic's BlockCodes as BlockEntityMicroBlock.OnLoadCollectibleMappings does when
    /// BlockSchematic.PlaceEntitiesAndBlockEntities places it. Ids it can't resolve are skipped.
    /// </summary>
    private IEnumerable<AssetLocation> PlacedMaterials(BlockSchematic schematic, string blockEntityData)
    {
        var tree = schematic.DecodeBlockEntityData(blockEntityData);
        if ((tree["materials"] as IntArrayAttribute)?.value is not int[] materials)
            yield break;
        foreach (int id in materials)
            if (schematic.BlockCodes.TryGetValue(id, out var code) && W.GetBlock(code) is Block block)
                yield return block.Code;
    }

    // Ruins whose chiseled blocks the bug gives other materials at 90° in this pack: one placed
    // underground (PlaceReplacingBlocks), one on the surface (PlaceRespectingBlockLayers).
    private static readonly (string Structure, string File) Underground =
        ("undergroundruins", "worldgen/schematics/underground/underground-verycommon/femursnapper-underground1.json");
    private static readonly (string Structure, string File) Surface =
        ("mediumruins", "worldgen/schematics/overground/main/mediumruins/mediumruins-femursnapper-o2-1009.json");
    private const int RuinAngle = 90;

    [AtlasScenario]
    public async Task Chiselfix_repair_gives_a_corrupted_ruin_the_materials_of_a_correct_one()
    {
        var ruin = PlaceRuinPair(Underground, heightAboveSpawn: 30);
        try
        {
            var corrupted = ruin.Differing();
            Assert.True(corrupted.Count > 0, "the unpatched placement corrupted no chiseled block");

            var result = await World.ExecuteCommand($"/chiselfix i_backed_up_my_world_first_repair 8 {ruin.Center}");
            Assert.True(result.Ok, result.Message);
            Assert.Contains($"{corrupted.Count} of {corrupted.Count} affected chiseled block(s) repaired", result.Message);
            Assert.Empty(ruin.Differing());
        }
        finally
        {
            ruin.Forget();
        }
    }

    [AtlasScenario]
    public async Task Chiselfix_check_changes_nothing()
    {
        var ruin = PlaceRuinPair(Underground, heightAboveSpawn: 50);
        try
        {
            var before = ruin.Corrupted.ToDictionary(kv => kv.Key, kv => (int[])kv.Value.BlockIds.Clone());
            int corrupted = ruin.Differing().Count;

            var result = await World.ExecuteCommand($"/chiselfix check 8 {ruin.Center}");
            Assert.True(result.Ok, result.Message);
            Assert.Contains($"{corrupted} of {corrupted} affected chiseled block(s) to repair", result.Message);
            Assert.Contains("Back up your world before running /chiselfix i_backed_up_my_world_first_repair", result.Message);
            foreach (var (offset, be) in ruin.Corrupted)
                Assert.Equal(before[offset], be.BlockIds);
        }
        finally
        {
            ruin.Forget();
        }
    }

    [AtlasScenario]
    public async Task Chiselfix_leaves_a_chiseled_block_with_other_materials_alone()
    {
        var ruin = PlaceRuinPair(Underground, heightAboveSpawn: 70);
        try
        {
            var corrupted = ruin.Differing();
            var edited = ruin.Corrupted[corrupted[0]];
            var editedIds = (int[])edited.BlockIds.Clone();
            editedIds[0] = BlockOf("game:rock-granite").Id == editedIds[0] ? BlockOf("game:rock-basalt").Id : BlockOf("game:rock-granite").Id;
            edited.BlockIds = (int[])editedIds.Clone();
            edited.MarkDirty();

            var result = await World.ExecuteCommand($"/chiselfix i_backed_up_my_world_first_repair 8 {ruin.Center}");
            Assert.True(result.Ok, result.Message);
            Assert.Contains("1 changed since (left alone)", result.Message);
            Assert.Equal(editedIds, edited.BlockIds);
            Assert.Equal(new[] { corrupted[0] }, ruin.Differing());
        }
        finally
        {
            ruin.Forget();
        }
    }

    [AtlasScenario]
    public async Task Chiselfix_repairs_a_surface_ruin_placed_over_block_layers()
    {
        var ruin = PlaceRuinPair(Surface, heightAboveSpawn: 90);
        try
        {
            var corrupted = ruin.Differing();
            Assert.True(corrupted.Count > 0, "the unpatched placement corrupted no chiseled block");

            var result = await World.ExecuteCommand($"/chiselfix i_backed_up_my_world_first_repair 8 {ruin.Center}");
            Assert.True(result.Ok, result.Message);
            Assert.Contains($"{corrupted.Count} of {corrupted.Count} affected chiseled block(s) repaired", result.Message);
            Assert.Empty(ruin.Differing());
        }
        finally
        {
            ruin.Forget();
        }
    }

    // The ruin with the see-through wayshrine pillars. It looks much the same at 0° as at 270°: in
    // the world it was found in, 270° matched 94% of its blocks and 0° 88%, and the command skipped
    // it as ambiguous until its chiseled blocks were used to tell them apart.
    private static readonly (string Structure, string File) Wayshrine =
        ("mediumruins", "worldgen/schematics/overground/main/mediumruins/mediumruins-femursnapper-o5-1005.json");

    [AtlasScenario]
    public async Task Chiselfix_repairs_the_wayshrine_ruin_at_270_degrees()
    {
        var ruin = PlaceRuinPair(Wayshrine, heightAboveSpawn: 110, angle: 270);
        try
        {
            var corrupted = ruin.Differing();
            Assert.True(corrupted.Count > 0, "the unpatched placement corrupted no chiseled block");

            var result = await World.ExecuteCommand($"/chiselfix i_backed_up_my_world_first_repair 8 {ruin.Center}");
            Assert.True(result.Ok, result.Message);
            Console.WriteLine($"[chiselrotationfix] wayshrine: {result.Message}");
            Assert.Contains("at 270°", result.Message);
            Assert.Contains($"{corrupted.Count} of {corrupted.Count} affected chiseled block(s) repaired", result.Message);
            Assert.Empty(ruin.Differing());
        }
        finally
        {
            ruin.Forget();
        }
    }

    /// <summary>
    /// The same ruin placed twice side by side at <paramref name="angle"/>, as its structure
    /// places it (underground: in granite's place andesite): once as the unpatched game places it
    /// (recorded in the map region, as worldgen does), once as the patched game does.
    /// </summary>
    private RuinPair PlaceRuinPair((string Structure, string File) which, int heightAboveSpawn, int angle = RuinAngle)
    {
        var (structure, schematic) = Ruin(which.Structure, which.File);
        var remaps = AccessTools.FieldRefAccess<WorldGenStructure, Dictionary<int, Dictionary<int, int>>>("resolvedRockTypeRemaps")(structure);
        var layerIds = AccessTools.FieldRefAccess<WorldGenStructure, int[]>("replacewithblocklayersBlockids")(structure);
        var rock = BlockOf("game:rock-andesite");
        var start = World.Spawn.AddCopy(-20, heightAboveSpawn, -20);
        var collisions = ForceCollisions(schematic);

        var corrupted = (BlockSchematicStructure)schematic.ClonePacked();
        RunUnpatched(() => corrupted.TransformWhilePacked(W, EnumOrigin.BottomCenter, angle));
        var correct = (BlockSchematicStructure)schematic.ClonePacked();
        correct.TransformWhilePacked(W, EnumOrigin.BottomCenter, angle);
        var correctStart = start.AddCopy(correct.SizeX + 4, 0, 0);
        foreach (var (placing, at) in new[] { (corrupted, start), (correct, correctStart) })
        {
            placing.blockLayerConfig = schematic.blockLayerConfig;
            if (structure.Placement == EnumStructurePlacement.Underground)
                placing.PlaceReplacingBlocks(W.BlockAccessor, W, at, placing.ReplaceMode, remaps, rock.Id);
            else
                placing.PlaceRespectingBlockLayers(W.BlockAccessor, W, at, 0x808080, 0x808080, 0x808080, 0x808080,
                    remaps, layerIds, Vintagestory.ServerMods.NoObf.GlobalConfig.ReplaceMetaBlocks);
        }

        var generated = new GeneratedStructure
        {
            Code = schematic.FromFile.GetNameWithDomain() + "/" + structure.Code,
            Group = structure.Group,
            Location = new Cuboidi(start.X, start.Y, start.Z, start.X + corrupted.SizeX, start.Y + corrupted.SizeY, start.Z + corrupted.SizeZ),
        };
        int regionSize = World.Api.WorldManager.RegionSize;
        var region = World.Api.WorldManager.GetMapRegion(start.X / regionSize, start.Z / regionSize)
                     ?? throw new Xunit.Sdk.XunitException("the map region at spawn isn't loaded");
        region.AddGeneratedStructure(generated);

        return new RuinPair(ChiseledBlocks(corrupted, start), ChiseledBlocks(correct, correctStart),
            $"={start.X + corrupted.SizeX / 2} ={start.Y} ={start.Z + corrupted.SizeZ / 2}",
            () =>
            {
                region.GeneratedStructures.Remove(generated);
                foreach (int key in collisions)
                    schematic.BlockCodes.Remove(key);
            });
    }

    /// <summary>
    /// Which materials the bug corrupts depends on how the pack numbers its blocks, which changes
    /// whenever a mod is added or updated. So the scenarios make the collision themselves: for each
    /// chiseled-block material whose world id isn't a key of the ruin's BlockCodes, a key there
    /// naming overlay-damagedstone, as the ruins in the world the bug was found in have. Grid blocks
    /// don't use these keys. This edits GenStructures' own copy of the schematic, which
    /// /chiselfix reads, so the returned keys must be removed again.
    /// </summary>
    private List<int> ForceCollisions(BlockSchematic schematic)
    {
        var decoy = BlockOf("game:overlay-damagedstone").Code;
        var added = new List<int>();
        foreach (var data in schematic.BlockEntities.Values)
        {
            if ((schematic.DecodeBlockEntityData(data)["materials"] as IntArrayAttribute)?.value is not int[] ids)
                continue;
            foreach (int id in ids)
            {
                if (!schematic.BlockCodes.TryGetValue(id, out var code) || W.GetBlock(code) is not Block material
                    || material.Code.Equals(decoy) || schematic.BlockCodes.ContainsKey(material.Id))
                    continue;
                schematic.BlockCodes[material.Id] = decoy;
                added.Add(material.Id);
            }
        }
        return added;
    }

    private sealed record RuinPair(Dictionary<Vec3i, BlockEntityMicroBlock> Corrupted, Dictionary<Vec3i, BlockEntityMicroBlock> Correct,
                                   string Center, Action Forget)
    {
        /// <summary>Offsets where the first ruin's chiseled block isn't made of what the second one's is.</summary>
        public List<Vec3i> Differing()
        {
            Assert.Equal(Correct.Keys.OrderBy(k => (k.X, k.Y, k.Z)), Corrupted.Keys.OrderBy(k => (k.X, k.Y, k.Z)));
            return Corrupted.Where(kv => !kv.Value.BlockIds.SequenceEqual(Correct[kv.Key].BlockIds)).Select(kv => kv.Key)
                .OrderBy(k => (k.X, k.Y, k.Z)).ToList();
        }
    }

    private Dictionary<Vec3i, BlockEntityMicroBlock> ChiseledBlocks(BlockSchematic placed, BlockPos start)
    {
        var found = new Dictionary<Vec3i, BlockEntityMicroBlock>();
        foreach (uint index in placed.BlockEntities.Keys)
        {
            var offset = new Vec3i((int)(index & 0x3FF), (int)((index >> 20) & 0x3FF), (int)((index >> 10) & 0x3FF));
            if (W.BlockAccessor.GetBlockEntity(start.AddCopy(offset.X, offset.Y, offset.Z)) is BlockEntityMicroBlock be)
                found[offset] = be;
        }
        Assert.NotEmpty(found);
        return found;
    }

    /// <summary>
    /// The BetterRuins ruin as GenStructures loaded it, and the structure that places it. The
    /// Atlas world is superflat, which GenStructures doesn't load structures for: load them as
    /// for a standard world.
    /// </summary>
    private (WorldGenStructure, BlockSchematicStructure) Ruin(string code, string file)
    {
        var gen = World.Api.ModLoader.GetModSystem<GenStructures>();
        var scfg = AccessTools.FieldRefAccess<GenStructures, WorldGenStructuresConfig?>("scfg");
        if (scfg(gen) == null)
            gen.initWorldGen();
        var config = scfg(gen) ?? throw new Xunit.Sdk.XunitException("GenStructures loaded no structures");
        foreach (var structure in config.Structures.Where(s => s.Code == code))
        {
            var schematics = AccessTools.FieldRefAccess<WorldGenStructure, BlockSchematicStructure[][]>("schematicDatas")(structure);
            var schematic = schematics.Select(r => r[0]).FirstOrDefault(s => s.FromFile.Path == file);
            if (schematic != null)
                return (structure, schematic);
        }
        throw new Xunit.Sdk.XunitException($"GenStructures has no {code} structure with {file}");
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the mod's patch skipped, through the copy of the mod the
    /// game loaded (this project's reference to it is a separate copy, with its own state).
    /// </summary>
    private void RunUnpatched(Action action)
    {
        var system = World.Api.ModLoader.GetModSystem("SeraphHorizons.ChiselRotationFix.ChiselRotationFixSystem")
                     ?? throw new Xunit.Sdk.XunitException("chiselrotationfix isn't loaded");
        var method = system.GetType().Assembly.GetType("SeraphHorizons.ChiselRotationFix.OnTransformedPatch")
            ?.GetMethod("RunUnpatched", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Xunit.Sdk.XunitException("OnTransformedPatch.RunUnpatched not found");
        method.Invoke(null, [action]);
    }
}
