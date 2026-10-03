using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.MapReveal;
using SeraphHorizons.Mod.MapReveal.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, Map Reveal: the server half of <c>/revealmap</c>. A test player runs it;
/// what the server sends the player is decoded as the client would, and compared with what the
/// loaded chunks show, so the savegame reader (its own connection, the saved chunk's protobuf
/// fields, the game's internal decompression) reads the same blocks the game does. The client half
/// (drawing the pieces into the world map and its database) needs a game client and is not run here.
/// </summary>
[AtlasWorld]
public class MapRevealScenarios : AtlasScenarioBase
{
    private ICoreServerAPI Api => World.Api;

    private async Task<ITestPlayer> Admin(string name)
    {
        var player = await World.JoinPlayer(name);
        var role = await World.ExecuteCommand($"/player {name} role admin");
        Assert.True(role.Ok, role.Message);
        return player;
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Reveals_the_saved_terrain_as_the_loaded_chunks_show_it_and_generates_nothing()
    {
        var player = await Admin("revealer");
        const int radius = 24;
        int cx = (int)player.Position.X / 32, cz = (int)player.Position.Z / 32;
        var area = RevealArea.Columns(cx, cz, radius, Api.WorldManager.MapSizeX / 32, Api.WorldManager.MapSizeZ / 32);
        // Columns at the rim, well outside what the player's join loaded: not generated yet.
        var far = area.Where(c => Distance(c, cx, cz) > radius - 2).ToList();
        var farBefore = await Existing(far);
        // Finished and loaded now, so in the save the command makes first. (Columns that finish
        // loading after it are not in the savegame yet, and rightly not revealed.)
        var loadedBefore = area.Where(c => new LoadedChunks(Api).Heights(c) is not null).ToList();

        var start = await player.ExecuteCommand($"/revealmap {radius}");
        Assert.True(start.Ok, start.Message);
        await World.Until(() => player.Client.ChatLines().Any(l => l.StartsWith("Map reveal done")), 30_000);
        string done = player.Client.ChatLines().Last(l => l.StartsWith("Map reveal done"));

        var samples = player.Client.Packets<MapRevealPacket>(MapRevealSystem.ChannelName)
            .SelectMany(p => RevealCodec.Decode(p.Columns)).ToList();
        var revealed = samples.Select(s => s.Pos).ToHashSet();
        Assert.Equal(samples.Count, revealed.Count);
        Assert.Subset(area.ToHashSet(), revealed);
        Assert.Equal($"Map reveal done: {samples.Count} chunk columns revealed, {area.Count - samples.Count} skipped (not generated yet).", done);

        // Nothing was generated: the rim columns that did not exist still don't, and none was sent.
        Assert.NotEmpty(far.Except(farBefore));
        Assert.Equal(farBefore, await Existing(far));
        Assert.Empty(far.Except(farBefore).Where(revealed.Contains));
        // Every column loaded and finished before the command was revealed.
        Assert.NotEmpty(loadedBefore);
        Assert.All(loadedBefore, c => Assert.Contains(c, revealed));

        // Where the column and its neighbours are loaded, the savegame and the loaded chunks agree.
        var loaded = new LoadedChunks(Api);
        var traits = MapRevealSystem.MapTraits(Api.World.Blocks);
        int sectionsY = Api.WorldManager.MapSizeY / 32, compared = 0, cellsDiffering = 0;
        foreach (var sample in samples)
        {
            if (!Neighbourhood(sample.Pos).All(c => loaded.Heights(c) is not null)) continue;
            var expected = ColumnSampler.Sample(loaded, sample.Pos, traits, sectionsY, colorAccurate: false);
            Assert.NotNull(expected);
            for (int k = 0; k < TerrainShade.Area; k++)
                if (expected.BlockIds[k] != sample.BlockIds[k] || expected.Kinds[k] != sample.Kinds[k] || expected.Shadow[k] != sample.Shadow[k])
                    cellsDiffering++;
            compared++;
        }
        Assert.True(compared >= 50, $"only {compared} columns had their neighbours loaded");
        // The world keeps running after the save (grass spreads, snow settles), so allow a few cells.
        Assert.True(cellsDiffering <= compared * TerrainShade.Area / 1000,
            $"{cellsDiffering} of {compared * TerrainShade.Area} cells differ between the savegame and the loaded chunks");
    }

    [AtlasScenario]
    public async Task Needs_creative_mode_or_the_controlserver_privilege()
    {
        var player = await World.JoinPlayer("visitor");
        var role = await World.ExecuteCommand("/player visitor role suplayer");
        Assert.True(role.Ok, role.Message);
        var visitor = (IServerPlayer)Api.World.AllOnlinePlayers.Single(p => p.PlayerName == "visitor");
        Assert.False(visitor.HasPrivilege(Privilege.controlserver));

        visitor.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var refused = await player.ExecuteCommand("/revealmap 2");
        Assert.False(refused.Ok, refused.Message);
        Assert.StartsWith("Map reveal is for creative mode or server admins", refused.Message);
        await World.Ticks(20);
        Assert.Empty(player.Client.Packets<MapRevealPacket>(MapRevealSystem.ChannelName));

        visitor.WorldData.CurrentGameMode = EnumGameMode.Creative;
        var allowed = await player.ExecuteCommand("/revealmap 2");
        Assert.True(allowed.Ok, allowed.Message);
        var stop = await player.ExecuteCommand("/revealmap stop");
        Assert.True(stop.Ok, stop.Message);
    }

    [AtlasScenario]
    public async Task Stop_stops_a_reveal_and_the_radius_is_capped()
    {
        var player = await Admin("stopper");
        var none = await player.ExecuteCommand("/revealmap stop");
        Assert.False(none.Ok, none.Message);
        Assert.Equal("No map reveal is running for you.", none.Message);

        foreach (string bad in new[] { $"{RevealArea.MaxRadius + 1}", "0", "far" })
        {
            var refused = await player.ExecuteCommand($"/revealmap {bad}");
            Assert.False(refused.Ok, refused.Message);
            Assert.StartsWith("Give a radius in chunks from 1 to 256", refused.Message);
        }

        var start = await player.ExecuteCommand($"/revealmap {RevealArea.MaxRadius}");
        Assert.True(start.Ok, start.Message);
        await World.Ticks(5);
        var stop = await player.ExecuteCommand("/revealmap stop");
        Assert.True(stop.Ok, stop.Message);
        await World.Ticks(20);
        int sent = player.Client.Packets<MapRevealPacket>(MapRevealSystem.ChannelName).Count;
        await World.Ticks(60);
        Assert.Equal(sent, player.Client.Packets<MapRevealPacket>(MapRevealSystem.ChannelName).Count);
        Assert.DoesNotContain(player.Client.ChatLines(), l => l.StartsWith("Map reveal done"));
    }

    [AtlasScenario]
    public void The_shading_helpers_match_the_games()
    {
        var random = new Random(11);
        for (int round = 0; round < 50; round++)
        {
            var data = new byte[1024];
            random.NextBytes(data);
            var ours = (byte[])data.Clone();
            BlurTool.Blur(data, 32, 32, 2);
            TerrainShade.Blur(ours, 32, 32, 2);
            Assert.Equal(data, ours);

            int color = random.Next(int.MinValue, int.MaxValue);
            float multiplier = (float)(random.NextDouble() * 2.5 - 0.25);
            Assert.Equal(ColorUtil.ColorMultiply3Clamped(color, multiplier), TerrainShade.ColorMultiply3Clamped(color, multiplier));
        }
    }

    [AtlasScenario]
    public void The_map_traits_match_the_map_layers_colours()
    {
        // ChunkMapLayer colours water and ice "lake" and snow "glacier" by default: the traits pick
        // out the same blocks.
        var traits = MapRevealSystem.MapTraits(Api.World.Blocks);
        var water = Api.World.GetBlock(new AssetLocation("game:water-still-7"));
        var glacier = Api.World.GetBlock(new AssetLocation("game:glacierice"));
        var snow = Api.World.GetBlock(new AssetLocation("game:snowlayer-1"));
        Assert.True(traits.Lake(water.Id));
        Assert.False(traits.Lake(glacier.Id));
        Assert.True(traits.Snow(snow.Id));
        Assert.Equal("lake", ChunkMapLayer.defaultMapColorCodes[water.BlockMaterial]);
    }

    private static double Distance(ColumnPos c, int cx, int cz) => Math.Sqrt((c.X - cx) * (c.X - cx) + (c.Z - cz) * (c.Z - cz));

    private static IEnumerable<ColumnPos> Neighbourhood(ColumnPos c)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            yield return new ColumnPos(c.X + dx, c.Z + dz);
    }

    // The columns whose map chunk is loaded or saved, as the server's own lookup tells it.
    private async Task<List<ColumnPos>> Existing(List<ColumnPos> columns)
    {
        var found = new List<ColumnPos>();
        int answered = 0;
        foreach (var c in columns)
            Api.WorldManager.TestMapChunkExists(c.X, c.Z, exists =>
            {
                if (exists) found.Add(c);
                answered++;
            });
        await World.Until(() => answered == columns.Count, 3000);
        return found.OrderBy(c => c.Z).ThenBy(c => c.X).ToList();
    }

    /// <summary>The server's loaded chunks, read the way the game's map reads the client's.</summary>
    private sealed class LoadedChunks(ICoreServerAPI api) : ISurfaceSource
    {
        public ushort[]? Heights(ColumnPos column)
        {
            var mapChunk = api.WorldManager.GetMapChunk(column.X, column.Z);
            if (mapChunk is null || mapChunk.CurrentPass < EnumWorldGenPass.Done) return null;
            for (int cy = 0; cy < api.WorldManager.MapSizeY / 32; cy++)
                if (api.WorldManager.GetChunk(column.X, cy, column.Z) is null) return null;
            return mapChunk.RainHeightMap;
        }

        public bool TryBlock(ColumnPos column, int x, int y, int z, out int blockId)
        {
            blockId = 0;
            if (Heights(column) is null) return false;
            var chunk = api.WorldManager.GetChunk(column.X, y / 32, column.Z);
            if (chunk is null) return false;
            blockId = chunk.UnpackAndReadBlock((y % 32 * 32 + z) * 32 + x, 3);
            return true;
        }
    }
}

/// <summary>
/// Map Reveal with its switch off (<c>"MapReveal": false</c> in ModConfig/seraphhorizons.json, seeded
/// from fixtures/mapreveal-off): there is no <c>/revealmap</c> command. Its own server.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/mapreveal-off", TargetPath = "ModConfig")]
public class MapRevealOffScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public void Switched_off_there_is_no_command()
    {
        Assert.False(World.Api.LoadModConfig("seraphhorizons.json")["MapReveal"].AsBool(true));
        Assert.Null(World.Api.ChatCommands.Get("revealmap"));
    }
}
