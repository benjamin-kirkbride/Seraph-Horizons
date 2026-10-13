using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Mining (README "Eidolon", mining): the <c>mine</c> order (<see cref="MineOrder"/>) and the command
/// tool's "mine an area" mode, an area of at most <see cref="EidolonMining.MaxAreaBlocks"/> blocks and
/// <see cref="EidolonMining.MaxAreaSide"/> a side, right after felling on the wheel; an area covered all
/// over that the eidolon does not fit in (README "Eidolon", mining, tunnels: too low, too narrow;
/// <see cref="MineOrder.TunnelFitOf"/>) is refused as it is marked, one covered in part never. One whose face
/// it cannot reach is not refused: it is planned, and what it cannot reach is reported
/// (<see cref="EidolonMinePlans"/>). And the channel the blocks out of an order's reach go to their owner's
/// command tool on (<see cref="Channel"/>, <see cref="MineOutOfReachPacket"/>). Registered on both sides
/// whatever the switch: with it off the entity and the tool do not exist.
/// </summary>
public class EidolonMineSystem : ModSystem
{
    public const string Channel = "seraphhorizons-eidolonmine";

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(Channel).RegisterMessageType<MineOutOfReachPacket>();
        EidolonOrders.Register(MineOrder.OrderCode, (_, args) => MineOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = MineOrder.OrderCode,
            Order = 55,
            Icon = new AssetLocation("game", "textures/icons/worldmap/pick.svg"),
            Mark = EidolonMarkKind.Area,
            MaxAreaSide = EidolonMining.MaxAreaSide,
            MaxAreaBlocks = EidolonMining.MaxAreaBlocks,
            Command = c => EidolonCommand.Order(MineOrder.OrderCode, MineOrder.Args(c.Area!.Value, c.Eidolon.Pos.XYZ)),
            CheckArea = (world, area, _) =>
                MineOrder.RefusalKey(MineOrder.TunnelFitOf(world, area)) is { } key
                    ? EidolonCommand.Refuse(key, EidolonTunnels.MinHeight, EidolonTunnels.MinWidth, EidolonTunnels.HeadingHeight)
                    : null,
        });
    }
}
