using System.Text;
using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, Ore/ (epic #435): placer fields (#442), the deposit registry (#443)
/// and ore and gravel maps (#444), in a new standard world with a fixed seed and the defaults. The
/// registry lists deposits from the seed alone, verifying one generates its chunks and counts its
/// ore, <c>/sh ore givemap</c> gives a map that puts a waypoint on the reader's map and reserves
/// the deposit, and the placer field rule resolves a gravel cell.
/// <para>A second partial file, OreAdminScenarios.cs, holds the ore admin tools' scenarios (#458) on
/// the same boot (Atlas boots one server per class, 65-90 s for a seeded standard world in CI). Both
/// share the world and its deposit registry in no set order, so a scenario that needs a deposit or
/// gravel field unsold (or in any other state) puts it there in its own arrange step; each uses its
/// own player names, and <see cref="ReadsBootLogAttribute"/> marks a scenario that reads the boot's
/// log. <see cref="OreCellsScenarios"/> stays a class of its own: it depends on its seed.</para>
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public partial class OreMapsScenarios : AtlasScenarioBase
{
    private const int Seed = 515151;

    private readonly ITestOutputHelper output;

    public OreMapsScenarios(ITestOutputHelper output) => this.output = output;

    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;

    private OreSystem Ore => World.Api.ModLoader.GetModSystem<OreSystem>();

    private DepositService Deposits => Ore.Deposits ?? throw new Xunit.Sdk.XunitException("the deposit service is not bound");

    private (int X, int Z) Spawn
    {
        get
        {
            var p = Sapi.World.DefaultSpawnPosition.AsBlockPos;
            return (p.X, p.Z);
        }
    }

    private async Task<string> Run(string command)
    {
        var result = await World.ExecuteCommand(command);
        output.WriteLine($"{command}: {(result.Ok ? "ok" : "FAILED")}:\n{result.Message}");
        Assert.True(result.Ok, $"{command}: {result.Message}");
        return result.Message ?? "";
    }

    /// <summary>Verifies and waits for the answer.</summary>
    private async Task<VerifyResult> Verify(DepositKey key, int ticks = 6000)
    {
        VerifyResult? result = null;
        Deposits.Verify(key, r => result = r);
        await World.Until(() => result != null, ticks);
        Assert.True(result != null, $"verifying {key} never answered");
        output.WriteLine($"verify {key}: {result}");
        return result!;
    }

    [AtlasScenario, ReadsBootLog]
    public void Placer_fields_are_on_for_the_new_world()
    {
        Assert.True(Ore.World.PlacerFields);
        Assert.Equal(1500, Ore.World.PlacerCellSize);
        Assert.NotNull(Ore.Placer);
        var saved = Sapi.WorldManager.SaveGame.GetData(OreSystem.WorldRecordKey);
        Assert.True(OreWorldRecord.Parse(Encoding.UTF8.GetString(saved)).PlacerFields);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Placer fields", StringComparison.Ordinal) || e.Message.Contains("Deposits", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    [AtlasScenario]
    public void Scattered_rich_gravel_is_cut_to_a_quarter()
    {
        var gravel = World.Api.ModLoader.Systems.OfType<GenDeposits>().SelectMany(g => g.Deposits ?? [])
            .Where(v => v.Code == PlacerFields.ScatteredGravelCode).ToList();
        Assert.NotEmpty(gravel);
        Assert.All(gravel, v => Assert.Equal(75 * 0.25f, v.TriesPerChunk, 3));
    }

    [AtlasScenario]
    public void Every_rocks_rich_gravel_pans_copper()
    {
        var pan = Sapi.World.Blocks.OfType<BlockPan>().First();
        var table = pan.Attributes["panningDrops"];
        foreach (var rock in new[] { "granite", "shale", "limestone", "slate", "basalt" })
        {
            var drops = table["@(richgravel-" + rock + ")"].AsArray();
            Assert.NotEmpty(drops);
            Assert.Contains(drops, d => d["code"].AsString("").EndsWith("nugget-nativecopper", StringComparison.Ordinal));
        }
    }

    [AtlasScenario]
    public async Task The_registry_lists_deposits_from_the_seed()
    {
        var (x, z) = Spawn;
        Assert.Contains("copper", Deposits.Metals);
        Assert.DoesNotContain("coal", Deposits.Metals);
        var copper = Deposits.Candidates(x, z, 6000, "copper");
        Assert.NotEmpty(copper);
        Assert.Equal(copper.OrderBy(c => c.Distance), copper);
        var cells = new OreCells(Seed);
        var home = Deposits.Candidate(new DepositKey("copper", cells.CellOf("copper", x, z)))!;
        Assert.NotNull(home);
        if (!home.Generated) // its primary spot until its chunk is generated
            Assert.Equal(cells.Spots("copper", home.Key.Cell)[home.Spot].X, home.X);
        var listed = await Run("/sh ore list copper 6000");
        foreach (var c in copper) Assert.Contains(c.Key.Id + ":", listed);
        Assert.Contains("unsold", listed);
        var all = await Run("/sh ore list 3000 --unsold");
        Assert.Contains("iron:", all);
        Assert.False((await World.ExecuteCommand("/sh ore list mithril")).Ok);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Verifying_an_ungenerated_deposit_generates_it_and_counts_its_ore()
    {
        // A cell far from the spawn, which nothing in this world has generated.
        var (x, z) = Spawn;
        var key = new DepositKey("iron", new OreCells(Seed).CellOf("iron", x + 20_000, z + 20_000));
        var before = Deposits.Candidate(key);
        Assert.NotNull(before);
        Assert.False(before.Generated);
        var result = await Verify(key);
        Assert.NotEqual(VerifyStatus.Field, result.Status);
        if (result.Status == VerifyStatus.Measured)
        {
            Assert.True(result.OreBlocks >= 0);
            Assert.Equal(result.Ingots, Deposits.Registry.Get(key).Ingots!.Value, 0);
            Assert.True(Deposits.Candidate(key)!.Generated);
            var chunk = OreCells.ChunkOf(result.Candidate!.X, result.Candidate.Z);
            Assert.NotNull(Sapi.WorldManager.GetMapChunk(chunk.X, chunk.Z));
        }
        var answer = await Run($"/sh ore verify {key.Id}");
        Assert.Contains(key.Id, answer);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Givemap_gives_a_map_that_places_a_waypoint()
    {
        var player = await World.JoinAtSpawn("cartographer");
        var sp = (IServerPlayer)player.Player;
        // The admin scenarios may have sold copper deposits near the spawn.
        await Run("/sh ore registry clear copper");
        await Run("/sh ore givemap cartographer copper 2");
        await World.Until(() => MapIn(sp) != null, 12000);
        var slot = MapIn(sp);
        Assert.True(slot != null, "no map arrived");
        var a = slot!.Itemstack.Attributes;
        Assert.Equal("copper", a.GetString(ItemOreMap.AttrMetal));
        Assert.Equal(2, a.GetInt(ItemOreMap.AttrPrecision));
        Assert.True(DepositKey.TryParse(a.GetString(ItemOreMap.AttrDeposit), out var key));
        var record = Deposits.Registry.Get(key);
        Assert.Equal(DepositState.Sold, record.State);
        Assert.Equal(sp.PlayerUID, record.SoldToUid);
        Assert.NotNull(a.GetString(ItemOreMap.AttrSizeTier)); // measured before it was sold
        // The marker is the deposit offset by the precision's seeded offset.
        var (dx, dz) = MapPrecision.Offset(Seed, key, 2);
        Assert.Equal(record.X!.Value + dx, a.GetInt(ItemOreMap.AttrX));
        Assert.Equal(record.Z!.Value + dz, a.GetInt(ItemOreMap.AttrZ));
        // Sold once: the issuer refuses it now.
        Assert.Null(Ore.Maps!.Issue(sp, key, 3));

        // Reading it places a waypoint and keeps the map.
        var layer = Sapi.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single();
        int before = layer.Waypoints.Count(w => w.OwningPlayerUid == sp.PlayerUID);
        EnumHandHandling handling = EnumHandHandling.NotHandled;
        slot.Itemstack.Collectible.OnHeldInteractStart(slot, sp.Entity, null, null, true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
        var mine = layer.Waypoints.Where(w => w.OwningPlayerUid == sp.PlayerUID).ToList();
        Assert.Equal(before + 1, mine.Count);
        var wp = mine.Last();
        output.WriteLine($"waypoint '{wp.Title}' at {wp.Position}");
        Assert.Equal(a.GetInt(ItemOreMap.AttrX) + 0.5, wp.Position.X);
        // Titled by what the ore is (#692): "Malachite and azurite deposit (medium)".
        var ores = OreNames.Split(a.GetString(ItemOreMap.AttrOres));
        Assert.NotEmpty(ores);
        Assert.All(ores, ore => Assert.Equal("copper", OreMetals.MetalOf(ore)));
        Assert.Equal(record.Makeup!.MainOres(), ores);
        Assert.Equal(record.Makeup.HostRock(), a.GetString(ItemOreMap.AttrRock));
        Assert.Equal(record.Makeup.Mix()?.Code, a.GetString(ItemOreMap.AttrGrades));
        Assert.Contains(Lang.Get("seraphhorizons:" + OreNames.LangKey(ores[0])), wp.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" deposit", wp.Title);
        var info = new StringBuilder();
        slot.Itemstack.Collectible.GetHeldItemInfo(slot, info, Sapi.World, false);
        output.WriteLine($"'{slot.Itemstack.GetName()}': {info}");
        Assert.Contains(Lang.Get("seraphhorizons:" + OreNames.LangKey(ores[0])), info.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(slot.Empty);
        // Read twice: no second waypoint.
        slot.Itemstack.Collectible.OnHeldInteractStart(slot, sp.Entity, null, null, true, ref handling);
        Assert.Equal(before + 1, layer.Waypoints.Count(w => w.OwningPlayerUid == sp.PlayerUID));

        Assert.Contains("sold to cartographer", await Run("/sh ore list copper 50000 --sold"));
        await Run($"/sh ore registry reset {key.Id}");
        Assert.Equal(DepositState.Unsold, Deposits.Registry.Get(key).State);
        await Run($"/sh ore registry mark {key.Id} soldout");
        Assert.Equal(DepositState.SoldOut, Deposits.Registry.Get(key).State);
    }

    private static ItemSlot? MapIn(IServerPlayer player)
    {
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
            if (inv.ClassName != GlobalConstants.creativeInvClassName)
            foreach (var slot in inv)
                if (slot.Itemstack?.Collectible?.Code == MapIssuer.OreMapCode && slot.Itemstack.Attributes.HasAttribute(ItemOreMap.AttrX))
                    return slot;
        return null;
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_gravel_cell_resolves_and_its_map_marks_the_field()
    {
        var (x, z) = Spawn;
        // The admin scenarios may have sold out a gravel field near the spawn.
        await Run("/sh ore registry clear gravel");
        var listed = await Run("/sh ore gravel 3000");
        var fields = Deposits.GravelFields(x, z, 3000);
        Assert.NotEmpty(fields);
        foreach (var f in fields) Assert.Contains(f.Key.Id + ":", listed);
        var cells = new PlacerCells(Seed);

        // Resolve cells until one holds a field (most do; some have no spot by water).
        PlacedField? placed = null;
        DepositKey key = default;
        foreach (var candidate in fields.Take(4))
        {
            var result = await Verify(candidate.Key);
            Assert.NotEqual(VerifyStatus.Measured, result.Status);
            var state = Ore.Placer!.StateOf(candidate.Key.Cell);
            Assert.True(state.Placed || state.None, $"{candidate.Key} did not resolve");
            if (result.Status != VerifyStatus.Field) continue;
            placed = result.Field;
            key = candidate.Key;
            break;
        }
        Assert.True(placed != null, "none of the four nearest gravel cells holds a field");
        output.WriteLine($"{key}: {placed}");
        Assert.InRange(placed!.Blocks, 50, PlacerCells.MaxBlocks);
        var spot = cells.Spots(key.Cell)[placed.Spot];
        Assert.Equal(spot.Chunk, OreCells.ChunkOf(placed.X, placed.Z));
        // Verifying generated it, but nothing keeps it loaded.
        bool loaded = false;
        Sapi.WorldManager.LoadChunkColumnPriority(placed.X / 32, placed.Z / 32, new ChunkLoadOptions { OnLoaded = () => loaded = true });
        await World.Until(() => loaded, 3000);
        var ba = Sapi.World.BlockAccessor;
        int gravelCount = 0;
        for (int gx = -16; gx <= 16; gx++)
            for (int gz = -16; gz <= 16; gz++)
                for (int gy = -12; gy <= 12; gy++)
                    if (ba.GetBlock(new Vintagestory.API.MathTools.BlockPos(placed.X + gx, placed.Y + gy, placed.Z + gz)).Code.Path.StartsWith("richgravel-"))
                        gravelCount++;
        output.WriteLine($"rich gravel around the field: {gravelCount}");
        Assert.True(gravelCount >= placed.Blocks, $"{gravelCount} rich gravel blocks around a field of {placed.Blocks}");
        var block = ba.GetBlock(new Vintagestory.API.MathTools.BlockPos(placed.X, placed.Y, placed.Z));
        Assert.Equal("richgravel-" + placed.Rock, block.Code.Path);
        Assert.Contains($"blocks of {placed.Rock} rich gravel", await Run($"/sh ore verify {key.Id}"));

        var map = Ore.Maps!.Issue(null, key, MapPrecision.Rough);
        Assert.NotNull(map);
        Assert.Equal(MapIssuer.GravelMapCode, map.Collectible.Code);
        Assert.Equal(MapPrecision.Exact, map.Attributes.GetInt(ItemOreMap.AttrPrecision)); // gravel maps are exact
        Assert.Equal(placed.X, map.Attributes.GetInt(ItemOreMap.AttrX));
        Assert.Equal(placed.Rock, map.Attributes.GetString(ItemOreMap.AttrRock));
        // What it pans (#692): the metals of the rock's rich gravel table, native copper at least.
        var metals = OreNames.Split(map.Attributes.GetString(ItemOreMap.AttrMetals));
        output.WriteLine($"{placed.Rock} rich gravel pans {string.Join(", ", metals)}: '{map.GetName()}'");
        Assert.Contains("copper", metals);
        Assert.Equal(DepositState.Sold, Deposits.Registry.Get(key).State);
    }
}
