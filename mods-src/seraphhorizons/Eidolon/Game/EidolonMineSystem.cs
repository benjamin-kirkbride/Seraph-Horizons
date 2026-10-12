using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Mining (README "Eidolon", mining): the <c>mine</c> order (<see cref="MineOrder"/>) and the command
/// tool's "mine an area" mode, an area of at most <see cref="EidolonMining.MaxAreaBlocks"/> blocks and
/// <see cref="EidolonMining.MaxAreaSide"/> a side, right after felling on the wheel; an area covered all
/// over that cannot be dug as a tunnel (README "Eidolon", mining, tunnels: too low, too narrow, no open
/// face; <see cref="MineOrder.PlanTunnel"/>) is refused as it is marked, one covered in part never. Registered on both
/// sides whatever the switch: with it off the entity and the tool do not exist.
/// </summary>
public class EidolonMineSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
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
            CheckArea = (world, area, from) =>
                MineOrder.RefusalKey(MineOrder.PlanTunnel(world, area, Box, from.X, from.Z).Fit) is { } key
                    ? EidolonCommand.Refuse(key, EidolonTunnels.MinHeight, EidolonTunnels.MinWidth, EidolonTunnels.HeadingHeight)
                    : null,
        });
    }

    /// <summary>The eidolon's collision box (1.7 wide, 3.75 tall), for the tunnel's face as it is marked.</summary>
    private static readonly Cuboidf Box = new(-(float)EidolonMining.HalfWidth, 0, -(float)EidolonMining.HalfWidth,
        (float)EidolonMining.HalfWidth, (float)EidolonMining.Height, (float)EidolonMining.HalfWidth);
}
