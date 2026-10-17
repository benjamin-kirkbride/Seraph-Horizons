using System.Net;
using Atlas.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Joins test players, which <c>World.JoinPlayer</c> alone can fail at on a slow runner (#666).
/// Every scenario joins through <see cref="JoinRetrying"/> or <see cref="JoinAtSpawn"/>, never
/// <c>World.JoinPlayer</c> itself.
/// <para>The engine places a player at once only when it needn't look for a spot: with no
/// <c>spawnRadius</c> and the chunk it joins in loaded. Otherwise it is a "delayed join", the
/// identification answered first and the entity spawned later, once a spot within the radius is
/// found or the chunk has loaded (both may need chunks generated). Atlas sends its join request as
/// soon as the entity object exists, so on a delayed join the engine's <c>HandleRequestJoin</c>
/// can name an entity not yet spawned (<c>EntityPlayer.SetName</c> throws on its missing nametag)
/// and the join stops there; Atlas times out waiting for its inventories (100 ticks).</para>
/// <para>A join that times out leaves its client registered on the server: Atlas frees its own
/// claims (the name, the socket) but not the engine's, and a server that isn't dedicated doesn't
/// drop a client whose packet threw. The next join under that name finds the stale client by name,
/// and registering its UDP endpoint again throws "An item with the same key has already been
/// added. Key: 127.0.0.1:&lt;id&gt;", failing every later scenario in the class. So a timed-out
/// join's client is disconnected here and the join tried again on a fresh connection, by which
/// time what was slow (the chunk load) has finished.</para>
/// <para><see cref="JoinRetrying"/> leaves the world as it is: where a player starts is something
/// scenarios see (MapRevealScenarios needs its players' joins to load more than one player's view
/// of columns, which players all standing on the spawn don't).</para>
/// </summary>
public static class PlayerJoin
{
    private const int Attempts = 3;

    public static async Task<ITestPlayer> JoinRetrying(this IWorldSession world, string name)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await world.JoinPlayer(name);
            }
            catch (Exception e) when (attempt < Attempts && IsTimedOutJoin(e))
            {
                world.Api.Logger.Warning("[PackTests] Join of {0} timed out (attempt {1} of {2}), retrying: {3}", name, attempt, Attempts, e.Message);
                await DropStaleClients(world, name);
            }
        }
    }

    /// <summary>
    /// Joins a player at the spawn itself, avoiding the delayed join in a standard world, whose
    /// radius search may need chunks generated: with the radius at 0 and the spawn's chunk loaded
    /// the engine places the player at once. For scenarios that don't rely on where in the radius a
    /// player starts.
    /// </summary>
    public static async Task<ITestPlayer> JoinAtSpawn(this IWorldSession world, string name)
    {
        world.Api.World.Config.SetString("spawnRadius", "0");
        var spawn = world.Api.World.DefaultSpawnPosition.AsBlockPos;
        if (world.Api.World.BlockAccessor.GetChunkAtBlockPos(spawn) == null)
        {
            world.Api.WorldManager.LoadChunkColumnPriority(spawn.X / GlobalConstants.ChunkSize, spawn.Z / GlobalConstants.ChunkSize);
            await world.Until(() => world.Api.World.BlockAccessor.GetChunkAtBlockPos(spawn) != null, 30_000);
        }
        return await world.JoinRetrying(name);
    }

    /// <summary>Atlas' own timeouts on a join step: no entity in time (its "did not finish joining"
    /// setup error), no inventories in time (a plain tick timeout), or never <c>Playing</c>. Not its
    /// other setup errors (a name already joined, a bad name), which a retry would only repeat.</summary>
    private static bool IsTimedOutJoin(Exception e) =>
        e is ScenarioTimeoutException
        || e is AtlasSetupException && (e.Message.Contains("did not finish joining") || e.Message.Contains("never reached the Playing"));

    /// <summary>Disconnects every client still registered under <paramref name="name"/>, as the
    /// engine would on a real client's socket closing, then lets a few ticks pass: Atlas' kick
    /// cleanup for the client (armed once its entity existed) releases its name claim on the next
    /// pass, which has to happen before the retry claims the name again.</summary>
    private static async Task DropStaleClients(IWorldSession world, string name)
    {
        var server = (ServerMain)world.Api.World;
        foreach (var client in server.Clients.Values.Where(c => c.PlayerName == name).ToList())
        {
            // The teardown's UDP pass removes the client's dummy endpoint unguarded, and a join that
            // timed out before its entity spawned never registered one (Atlas keys it by client id).
            var endpoint = new IPEndPoint(IPAddress.Loopback, client.Id);
            if (client.Player != null && server.UdpSockets[0] is DummyUdpNetServer udp && !udp.EndPoints.ContainsKey(endpoint))
                udp.Add(endpoint, client.Id);
            try
            {
                server.DisconnectPlayer(client);
            }
            catch (Exception e)
            {
                world.Api.Logger.Warning("[PackTests] Disconnecting {0}'s stale client {1} threw: {2}", name, client.Id, e);
            }
            // A teardown that threw partway still must not leave the client to be found by name.
            if (server.Clients.TryGetValue(client.Id, out var left) && ReferenceEquals(left, client))
            {
                server.Clients.Remove(client.Id, out _);
                if (client.Entityplayer is { } entity && world.Api.World.GetEntityById(entity.EntityId) == entity)
                    server.DespawnEntity(entity, new EntityDespawnData { Reason = EnumDespawnReason.Disconnect });
            }
        }
        await world.Ticks(20);
    }
}
