using System.Text.Json.Nodes;
using Atlas.XUnit;
using SeraphHorizons.Mod.Admin;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, the ore admin tools (#458): every <c>/sh ore</c> subcommand answers on a
/// new standard world and its <c>--json</c> answer parses, <c>count</c> finds the ore a verified
/// deposit holds, the registry export reads back, a live survey writes the survey tool's files, and
/// the admin map overlay reaches admins only.
/// <para>A partial file of <see cref="OreMapsScenarios"/>, on its boot (seed 515151): see there for
/// the rules both files keep.</para>
/// </summary>
public partial class OreMapsScenarios
{
    /// <summary>Runs a command and checks its answer: no formatter errors, chat-safe, ok, not empty.</summary>
    private async Task<string> RunChecked(string command, Atlas.Api.ITestPlayer? player = null)
    {
        using var watch = new FormatErrorWatch(World.Api.Logger);
        var result = player == null ? await World.ExecuteCommand(command) : await player.ExecuteCommand(command);
        Assert.True(watch.Errors.Count == 0, $"{command} hit the translation formatter:\n" + string.Join("\n", watch.Errors));
        FormatErrorWatch.AssertChatSafe(command, result.Raw);
        output.WriteLine($"{command}: {(result.Ok ? "ok" : "FAILED")}:\n{result.Message}");
        Assert.True(result.Ok, $"{command}: {result.Message}");
        Assert.False(string.IsNullOrWhiteSpace(result.Message), $"{command} answered nothing");
        return result.Message!;
    }

    /// <summary>Runs a command with --json and checks the shape every answer has.</summary>
    private async Task<JsonObject> Json(string command, Atlas.Api.ITestPlayer? player = null)
    {
        string text = await RunChecked(command + " --json", player);
        var json = JsonNode.Parse(text) as JsonObject;
        Assert.True(json != null, $"{command} --json is not a JSON object: {text}");
        Assert.True((bool)json!["ok"]!);
        Assert.StartsWith("ore", (string)json["command"]!);
        Assert.False(string.IsNullOrEmpty((string?)json["summary"]));
        Assert.IsType<JsonArray>(json["lines"]);
        return json;
    }

    private async Task<Atlas.Api.ITestPlayer> Admin(string name)
    {
        var player = await World.JoinPlayer(name);
        Assert.True((await World.ExecuteCommand($"/player {name} role admin")).Ok);
        return player;
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Every_subcommand_answers_in_text_and_json()
    {
        var spawn = World.Spawn;
        var admin = await Admin("oreadmin");
        foreach (var command in new[]
                 {
                     "/sh ore cells", "/sh ore cells 6000", "/sh ore count", "/sh ore districts", "/sh ore districts 40000",
                     $"/sh ore cell {spawn.X} {spawn.Z} copper", "/sh ore list", "/sh ore list copper 9000 --unsold", "/sh ore gravel 3000",
                     "/sh ore log on", "/sh ore log off", "/sh ore registry export oreadmin-all",
                 })
        {
            await RunChecked(command);
            await Json(command);
        }
        foreach (var command in new[] { "/sh ore here", "/sh ore markers 4000", "/sh ore markers clear", "/sh ore map on", "/sh ore map off" })
        {
            await RunChecked(command, admin);
            await Json(command, admin);
        }

        var cells = await Json("/sh ore cells 6000");
        var rows = cells["cells"]!.AsArray();
        Assert.Contains(rows, r => (string)r!["kind"]! == "copper");
        Assert.Contains(rows, r => (string)r!["kind"]! == PlacerCells.Kind);
        var list = await Json("/sh ore list copper 9000");
        var deposits = list["deposits"]!.AsArray();
        Assert.Equal(Deposits.Candidates(spawn.X, spawn.Z, 9000, "copper").Count, deposits.Count);
        Assert.All(deposits, d => Assert.StartsWith("copper:", (string)d!["id"]!));
        var districts = await Json("/sh ore districts 40000");
        Assert.True((int)districts["tileSize"]! >= 4000);
        Assert.NotEmpty(districts["tiles"]!.AsArray());

        // An error answers as JSON too, with ok false.
        var bad = await World.ExecuteCommand("/sh ore list mithril --json");
        Assert.False(bad.Ok);
        Assert.False((bool)JsonNode.Parse(bad.Message!)!["ok"]!);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Only_admins_may_and_only_admins_get_the_overlay()
    {
        var admin = await Admin("overlayadmin");
        var visitor = await World.JoinPlayer("overlayvisitor");
        // A joined test player is admin by default (Atlas); a plain player has suplayer.
        visitor.Player.SetRole("suplayer");
        Assert.False(visitor.Player.HasPrivilege(Privilege.controlserver));
        var denied = await visitor.ExecuteCommand("/sh ore cells");
        Assert.False(denied.Ok);
        var deniedMap = await visitor.ExecuteCommand("/sh ore map on");
        Assert.False(deniedMap.Ok);
        Assert.Empty(visitor.Client.Packets<AdminMapPacket>(AdminSystem.ChannelName));

        await RunChecked("/sh ore map on 9000", admin);
        await World.Ticks(5);
        var packet = admin.Client.Packets<AdminMapPacket>(AdminSystem.ChannelName).LastOrDefault(p => p.Key == "ore");
        Assert.True(packet != null, "no ore overlay arrived");
        var overlay = MapOverlay.FromJson(packet!.Json);
        output.WriteLine($"overlay: {overlay.Rects.Count} rects, {overlay.Marks.Count} marks, {overlay.Rings.Count} rings");
        Assert.NotEmpty(overlay.Rects);
        Assert.NotEmpty(overlay.Marks);
        await RunChecked("/sh ore map off", admin);
        await World.Ticks(5);
        Assert.Equal("", admin.Client.Packets<AdminMapPacket>(AdminSystem.ChannelName).Last().Json);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Count_finds_the_ore_of_a_verified_deposit()
    {
        var spawn = World.Spawn;
        var admin = await Admin("orecounter");
        // The nearest deposits of a few metals: the first one measured with ore.
        VerifyResult? found = null;
        foreach (var candidate in new[] { "copper", "iron", "tin", "lead", "zinc" }
                     .SelectMany(m => Deposits.Candidates(spawn.X, spawn.Z, 9000, m).Take(1)).OrderBy(c => c.Distance).Take(4))
        {
            VerifyResult? result = null;
            Deposits.Verify(candidate.Key, r => result = r);
            await World.Until(() => result != null, 6000);
            output.WriteLine($"verify {candidate.Key}: {result}");
            if (result is { Status: VerifyStatus.Measured, OreBlocks: > 0 })
            {
                found = result;
                break;
            }
        }
        Assert.True(found != null, "none of the nearest deposits holds ore");
        var c = found!.Candidate!;
        admin.TeleportTo(new BlockPos(c.X, Sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(c.X, 0, c.Z)) + 2, c.Z));
        await World.Ticks(20);
        var json = await Json("/sh ore count 64", admin);
        long blocks = (long)json["blocks"]!;
        output.WriteLine($"count: {blocks} ore blocks; verify measured {found.OreBlocks}");
        var metal = c.Key.Kind;
        long ofMetal = json["rows"]!.AsArray().Where(r => (string)r!["metal"]! == metal).Sum(r => (long)r!["blocks"]!);
        Assert.True(ofMetal > 0, $"count found no {metal} around {c.Key}");
        Assert.True((double)json["rows"]!.AsArray().First(r => (string)r!["metal"]! == metal)!["ingots"]! >= 0);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task The_registry_exports_and_imports()
    {
        var spawn = World.Spawn;
        // Reset and MarkSoldOut set each record whatever the other file left in it.
        var key = Deposits.Candidates(spawn.X, spawn.Z, 9000, "copper").First().Key;
        var gravel = Deposits.GravelFields(spawn.X, spawn.Z, 3000).First().Key;
        Deposits.Registry.Reset(key);
        Assert.True(Deposits.Registry.MarkSold(key, "uid-x", "Exporter", 3));
        Deposits.Registry.MarkSoldOut(gravel);
        var exported = await Json("/sh ore registry export oreadmin-registry");
        string path = (string)exported["file"]!;
        Assert.Equal(Path.Combine(GamePaths.DataPath, AdminFiles.Folder, "oreadmin-registry.json"), path);
        Assert.True(File.Exists(path));

        await RunChecked("/sh ore registry clear all");
        Assert.Empty(Deposits.Registry.All());
        var imported = await Json("/sh ore registry import oreadmin-registry");
        Assert.True((int)imported["records"]! >= 2);
        Assert.Equal("Exporter", Deposits.Registry.Get(key).SoldToName);
        Assert.Equal(DepositState.SoldOut, Deposits.Registry.Get(gravel).State);

        await RunChecked("/sh ore registry clear gravel");
        Assert.Equal(DepositState.Unsold, Deposits.Registry.Get(gravel).State);
        Assert.Equal(DepositState.Sold, Deposits.Registry.Get(key).State);
        Assert.False((await World.ExecuteCommand("/sh ore registry export ../escape")).Ok);
        Assert.False((await World.ExecuteCommand("/sh ore registry import no-such-file")).Ok);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_live_survey_writes_the_survey_tools_files()
    {
        var json = await Json("/sh ore survey 8 oreadmin-survey");
        string path = (string)json["file"]!;
        await World.Until(() => File.Exists(path + ".cells.csv"), 60_000);
        Assert.True(File.Exists(path), "the survey wrote nothing");
        var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(64, (int)doc["chunkColumns"]!);
        Assert.Equal(Sapi.World.Seed, (int)doc["seed"]!);
        var blocks = doc["blocks"]!.AsObject();
        Assert.Contains(blocks, b => b.Key.Contains(":rock-"));
        foreach (var line in File.ReadLines(path + ".cells.csv").Take(20))
            Assert.Equal(6, line.Split(',').Length);
        Assert.False((await World.ExecuteCommand("/sh ore survey 0 x")).Ok);
    }

    [AtlasScenario]
    public async Task Logging_writes_placement_decisions()
    {
        await RunChecked("/sh ore log on");
        Assert.NotNull(AdminLogs.Ore);
        Assert.Equal(AdminLogs.OreChannels.Order(), AdminLogs.Ore!.On);
        AdminLogs.Ore.Write("placement", "scenario line");
        Assert.Contains("[placement] scenario line", File.ReadAllText(AdminLogs.Ore.Path));
        await RunChecked("/sh ore log off");
        Assert.Empty(AdminLogs.Ore.On);
    }
}
