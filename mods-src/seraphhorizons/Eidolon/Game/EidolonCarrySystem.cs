using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's carry job (#676; README "Eidolon", carrying): the load behaviour
/// (<see cref="EntityBehaviorEidolonCarry"/>), the <c>carry</c> and <c>setdown</c> orders
/// (<see cref="CarryOrder"/>, <see cref="SetDownOrder"/>), their command tool modes (wheel order 30
/// and 40, each marking a block; both refuse without Carry On, <see cref="EidolonCarryOn"/>). The
/// load is drawn at the shape's <c>Carry</c> point by the eidolon's renderer
/// (<see cref="EidolonShapeRenderer"/>).
/// </summary>
public class EidolonCarrySystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorEidolonCarry.Code, typeof(EntityBehaviorEidolonCarry));
        EidolonOrders.Register(CarryOrder.OrderCode, (_, args) => CarryOrder.From(args));
        EidolonOrders.Register(SetDownOrder.OrderCode, (_, args) => SetDownOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = "carry",
            Order = 30,
            Icon = new AssetLocation("game", "textures/icons/moveud.svg"),
            Mark = EidolonMarkKind.Block,
            Command = CommandCarry,
        });
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = "setdown",
            Order = 40,
            Icon = new AssetLocation("game", "textures/icons/pointsouth.svg"),
            Mark = EidolonMarkKind.Block,
            Command = CommandSetDown,
        });
    }

    private static EidolonCommand CommandCarry(EidolonCommandContext c)
    {
        if (c.Target is not { } target)
            return EidolonCommand.Refuse("eidoloncommander-mark-block");
        var api = c.Eidolon.Api;
        if (!EidolonCarryOn.Available(api))
            return EidolonCommand.Refuse("eidolon-carry-nocarryon");
        if (c.Eidolon.GetBehavior<EntityBehaviorEidolonCarry>() is not { } carry)
            return EidolonCommand.Refuse("eidolon-carry-nocarryon");
        if (carry.LoadStack is { } held)
            return EidolonCommand.Refuse("eidolon-carry-full", held.GetName());
        var pos = Origin(c.Eidolon.World.BlockAccessor, target);
        if (!EidolonCarryOn.IsCarryable(api, c.Eidolon.World.BlockAccessor.GetBlock(pos)))
            return EidolonCommand.Refuse("eidolon-carry-notcarryable");
        if (c.Player.Entity is not { } player || !EidolonCarryOn.MayTake(player, pos))
            return EidolonCommand.Refuse("eidolon-carry-noperm");
        return EidolonCommand.Order(CarryOrder.OrderCode, CarryOrderBase.Args(pos, c.Player));
    }

    private static EidolonCommand CommandSetDown(EidolonCommandContext c)
    {
        if (c.Target is not { } target)
            return EidolonCommand.Refuse("eidoloncommander-mark-block");
        if (c.Eidolon.GetBehavior<EntityBehaviorEidolonCarry>() is not { Carrying: true } carry)
            return EidolonCommand.Refuse("eidolon-setdown-empty");
        if (SetDownOrder.PlaceFor(carry, target) is not { } place)
            return EidolonCommand.Refuse("eidolon-setdown-noroom");
        if (c.Eidolon.World.Claims.TestAccess(c.Player, place, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted)
            return EidolonCommand.Refuse("eidolon-carry-noperm");
        return EidolonCommand.Order(SetDownOrder.OrderCode, CarryOrderBase.Args(place, c.Player));
    }

    /// <summary>A multiblock's part stands for the block it is part of (as Carry On takes it).</summary>
    private static BlockPos Origin(IBlockAccessor blocks, BlockPos pos) =>
        blocks.GetBlock(pos) is Vintagestory.GameContent.BlockMultiblock part ? pos.AddCopy(part.OffsetInv) : pos.Copy();
}
