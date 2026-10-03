using ProtoBuf;

namespace SeraphHorizons.Mod.MapReveal;

/// <summary>A batch of revealed chunk columns, server to client, on
/// <see cref="MapRevealSystem.ChannelName"/>.</summary>
[ProtoContract]
public class MapRevealPacket
{
    /// <summary>The columns, encoded by <see cref="Core.RevealCodec"/>.</summary>
    [ProtoMember(1)]
    public byte[] Columns { get; set; } = [];
}
