using Atlas.Api;
using Vintagestory.API.Config;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Joins a player in a standard world, which <c>World.JoinPlayer</c> alone can fail at (#666). A
/// standard world has a <c>spawnRadius</c>, and the engine places a new player within it on a
/// "delayed join": it sends the server identification first and spawns the player's entity only once
/// it has found a spot, which may need chunks generated. Atlas' client sends its join request on
/// the identification, so if the spot is still being searched for, the engine's
/// <c>HandleRequestJoin</c> names an entity not yet spawned (<c>EntityPlayer.SetName</c> throws on its
/// missing nametag), the join stops there, and <c>JoinPlayer</c> times out waiting for its inventories.
/// With the radius at 0 and the spawn's chunk loaded the engine places the player at once, before the
/// request. No scenario relies on where in the radius a player starts.
/// </summary>
public static class SpawnJoin
{
    public static async Task<ITestPlayer> JoinAtSpawn(this IWorldSession world, string name)
    {
        world.Api.World.Config.SetString("spawnRadius", "0");
        var spawn = world.Api.World.DefaultSpawnPosition.AsBlockPos;
        if (world.Api.World.BlockAccessor.GetChunkAtBlockPos(spawn) == null)
        {
            world.Api.WorldManager.LoadChunkColumnPriority(spawn.X / GlobalConstants.ChunkSize, spawn.Z / GlobalConstants.ChunkSize);
            await world.Until(() => world.Api.World.BlockAccessor.GetChunkAtBlockPos(spawn) != null, 30_000);
        }
        return await world.JoinPlayer(name);
    }
}
