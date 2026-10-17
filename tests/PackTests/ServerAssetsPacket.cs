using System.Reflection;
using Vintagestory.Server;

namespace SeraphHorizons.PackTests;

/// <summary>The packet of every block, item, entity type and recipe registry the server sends a
/// joining client (ServerMain.BuildServerAssetsPacket, kept in a private field). It is built on a
/// pool thread from the WorldReady phase on, and as it encodes each block and item it calls their
/// FreeRAMServer, which drops what only a client draws with (the hand, gui and ground transforms,
/// textures, inventory shapes). So a scenario must not read those fields off a Block: while the
/// build runs, the blocks it has passed have them and the rest do not (#1123). It reads them here,
/// as the client gets them, instead.</summary>
internal static class ServerAssetsPacket
{
    /// <summary>The packet, once built; waits for the build as a joining player does.</summary>
    public static Packet_ServerAssets Built(ServerMain server)
    {
        typeof(ServerMain).GetMethod("WaitOnBuildServerAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(server, null);
        var (packet, bytes, length) = Raw(server);
        // A dedicated server serializes it at boot; an embedded one keeps the object.
        var sent = packet ?? (bytes == null ? null : Packet_ServerSerializer.DeserializeBuffer(bytes, length, new Packet_Server()));
        Assert.NotNull(sent?.Assets);
        return sent!.Assets;
    }

    /// <summary>The packet after a trip through the wire format, serialized here if the server
    /// kept the object.</summary>
    public static Packet_ServerAssets AsSent(ServerMain server)
    {
        typeof(ServerMain).GetMethod("WaitOnBuildServerAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(server, null);
        var (packet, bytes, length) = Raw(server);
        if (bytes == null)
        {
            Assert.NotNull(packet);
            bytes = Packet_ServerSerializer.SerializeToBytes(packet);
            length = bytes.Length;
        }
        var sent = Packet_ServerSerializer.DeserializeBuffer(bytes, length, new Packet_Server());
        Assert.NotNull(sent.Assets);
        return sent.Assets;
    }

    private static (Packet_Server? Packet, byte[]? Bytes, int Length) Raw(ServerMain server)
    {
        var boxed = typeof(ServerMain).GetField("serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(server)!;
        var length = (int)boxed.GetType().GetField("Length")!.GetValue(boxed)!;
        if (length > 0)
            return (null, (byte[])boxed.GetType().GetField("buffer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(boxed)!, length);
        return ((Packet_Server?)boxed.GetType().GetField("packet", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(boxed), null, 0);
    }
}
