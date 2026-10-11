using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Mining (README "Eidolon", mining): the <c>mine</c> order (<see cref="MineOrder"/>) and the command
/// tool's "mine an area" mode, an area of at most <see cref="EidolonMining.MaxAreaBlocks"/> blocks and
/// <see cref="EidolonMining.MaxAreaSide"/> a side, right after felling on the wheel. Registered on both
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
        });
    }
}
