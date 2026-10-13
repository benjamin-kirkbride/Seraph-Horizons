using ProtoBuf;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>What of a mine order's area is out of its eidolons' reach (README "Eidolon", mining: outlined
/// in red while the owner holds the command tool in mine mode), server to the owner's client, on
/// <see cref="EidolonMineSystem.Channel"/>: the area's corners and the boxes covering those blocks
/// (<see cref="Core.MineOutlines.Boxes"/>), none once the job is done.</summary>
[ProtoContract]
public class MineOutOfReachPacket
{
    /// <summary>The area: min x, y, z, then max x, y, z.</summary>
    [ProtoMember(1)]
    public int[] Area { get; set; } = [];

    /// <summary>Six a box, as <see cref="Area"/>.</summary>
    [ProtoMember(2)]
    public int[] Boxes { get; set; } = [];
}
