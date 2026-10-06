using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Vintagestory.Server;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeraphHorizons.PackTests;

/// <summary>The pack as a whole: every locked mod loads, at its locked version, cleanly.</summary>
public partial class SharedWorldScenarios
{
    public static TheoryData<string, string> LockedMods()
    {
        var data = new TheoryData<string, string>();
        // Client-only mods never load on a dedicated server, which is what Atlas runs.
        foreach (var (id, version, side) in PackLock.Mods)
            if (side != "client") data.Add(id, version);
        return data;
    }

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(LockedMods))]
    public void Locked_mod_is_loaded_at_locked_version(string modId, string version)
    {
        Assert.True(World.Api.ModLoader.IsModEnabled(modId), $"{modId} is not enabled");
        Assert.Equal(version, World.Api.ModLoader.GetMod(modId).Info.Version);
    }

    [AtlasScenario(TimeoutMs = 120_000), ReadsBootLog]
    public async Task Server_boots_and_ticks_without_errors()
    {
        await World.Ticks(20);
        // Known cross-mod errors (pack/known-errors.json, one issue each) are tolerated.
        var errors = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => !PackLock.KnownErrors.Any(k => k.IsMatch(e.Message)))
            .Select(e => $"[{e.Level}] {e.DescribeSource()}: {e.Message}")
            .ToList();
        Assert.True(errors.Count == 0, "Errors logged:\n" + string.Join("\n", errors));
    }

    /// <summary>The server sends a client each item's and block's behaviours by registered class
    /// name. One added in code without a registration goes out with no name, and the client
    /// crashes reading it (ArgumentNullException in ReadItemTypePacket or ReadBlockTypePacket),
    /// which no scenario sees: Atlas runs no client.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_behaviour_sent_to_a_client_has_a_class_name()
    {
        var registry = World.Api.ClassRegistry;
        var unnamed = World.Api.World.Collectibles
            .Where(c => c?.Code != null)
            .SelectMany(c => c.CollectibleBehaviors.Select(b => (c.Code, Type: b.GetType())))
            .Where(x => (typeof(BlockBehavior).IsAssignableFrom(x.Type)
                ? registry.GetBlockBehaviorClassName(x.Type)
                : registry.GetCollectibleBehaviorClassName(x.Type)) == null)
            .GroupBy(x => x.Type, x => x.Code)
            .Select(g => $"{g.Key.FullName} (on {g.Count()}, e.g. {g.First()})")
            .ToList();
        Assert.True(unnamed.Count == 0, "Behaviours with no registered class:\n" + string.Join("\n", unnamed));
    }

    /// <summary>Every joining client is sent one packet holding every block, item, entity type and
    /// recipe registry, and reads it with code that throws on bad data (#494: a behaviour with no
    /// class name). Atlas runs no client, so this reads the packet the server built at boot, as
    /// serialized for the wire, with the readers the client uses (ClientSystemStartup), one block,
    /// item, entity type and recipe registry at a time so a failure names what it was reading.
    /// The scenario above names the one cause seen so far; this one catches the rest of the class.
    /// Both sides share this process's class registry, so a class a mod registers on the server
    /// only still passes here.</summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public void A_client_can_read_everything_the_server_sends_it_on_joining()
    {
        var server = (ServerMain)World.Api.World;
        var assets = ServerAssetsAsSent(server);
        var registry = ServerMain.ClassRegistry;
        var failures = new List<(string What, string Error)>();
        void Read(string what, Action read)
        {
            try { read(); }
            catch (Exception e) { failures.Add((what, $"{e.GetType().Name}: {e.Message}")); }
        }

        // PopulateBlocks and PopulateItems skip entries with no code (unused ids).
        foreach (var p in assets.Blocks.Take(assets.BlocksCount).Where(p => p.Code != null))
            Read($"block {p.Code}", () => BlockTypeNet.ReadBlockTypePacket(p, server, registry));
        foreach (var p in assets.Items.Take(assets.ItemsCount).Where(p => p.Code != null))
            Read($"item {p.Code}", () => ItemTypeNet.ReadItemTypePacket(p, server, registry));
        // LoadEntityTypes catches and logs these, so a bad one is a missing entity, not a crash;
        // still a bug, and the client reads the shape with a null world as it does here.
        foreach (var p in assets.Entities.Take(assets.EntitiesCount))
            Read($"entity {p.Code}", () => EntityTypeNet.FromPacket(p, null));
        // HandleServerAssets_Step11 reads each registry's bytes into the client's own instance.
        foreach (var p in assets.Recipes.Take(assets.RecipesCount))
            Read($"recipes {p.Code}", () =>
            {
                var fresh = (RecipeRegistryBase)Activator.CreateInstance(server.GetRecipeRegistry(p.Code).GetType())!;
                fresh.FromBytes(server, p.Quantity, p.Data);
            });

        Assert.True(assets.BlocksCount > 0 && assets.ItemsCount > 0 && assets.RecipesCount > 0,
            $"empty assets packet: {assets.BlocksCount} blocks, {assets.ItemsCount} items, {assets.RecipesCount} recipe registries");
        // One unregistered behaviour fails every item carrying it, so group by the error.
        var grouped = failures.GroupBy(f => f.Error, f => f.What)
            .Select(g => $"{g.Key} (on {g.Count()}, e.g. {g.First()})");
        Assert.True(failures.Count == 0, "A client would fail reading:\n" + string.Join("\n", grouped));
    }

    /// <summary>The server's assets packet (ServerMain.BuildServerAssetsPacket, kept in a private
    /// field) after a trip through the wire format. A dedicated server serializes it at boot; an
    /// embedded one keeps the object, so it is serialized here.</summary>
    private static Packet_ServerAssets ServerAssetsAsSent(ServerMain server)
    {
        // Built on a pool thread from the WorldReady phase on; a joining player waits for it the same way.
        typeof(ServerMain).GetMethod("WaitOnBuildServerAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(server, null);
        var boxed = typeof(ServerMain).GetField("serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(server)!;
        var length = (int)boxed.GetType().GetField("Length")!.GetValue(boxed)!;
        byte[] bytes;
        if (length > 0)
        {
            bytes = (byte[])boxed.GetType().GetField("buffer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(boxed)!;
        }
        else
        {
            var packet = (Packet_Server?)boxed.GetType().GetField("packet", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(boxed);
            Assert.NotNull(packet);
            bytes = Packet_ServerSerializer.SerializeToBytes(packet);
            length = bytes.Length;
        }
        var sent = Packet_ServerSerializer.DeserializeBuffer(bytes, length, new Packet_Server());
        Assert.NotNull(sent.Assets);
        return sent.Assets;
    }
}

internal static class PackLock
{
    public static IReadOnlyList<(string Id, string Version, string Side)> Mods { get; } = Load();

    /// <summary>pack/known-errors.json: understood cross-mod problems, one issue each.</summary>
    public static IReadOnlyList<Regex> KnownErrors { get; } = LoadKnown("errors", anchored: false);

    public static IReadOnlyList<Regex> KnownSchematicBlocks { get; } = LoadKnown("schematicBlocks", anchored: true);

    private static List<Regex> LoadKnown(string section, bool anchored)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "known-errors.json")));
        return doc.RootElement.GetProperty(section).EnumerateArray()
            .Select(e => e.GetProperty("pattern").GetString()!)
            .Select(p => new Regex(anchored ? $"^(?:{p})$" : p))
            .ToList();
    }

    private static List<(string, string, string)> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "lock.json")));
        return doc.RootElement.GetProperty("mods").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString()!, m.GetProperty("version").GetString()!,
                          m.GetProperty("side").GetString()!))
            .ToList();
    }
}
