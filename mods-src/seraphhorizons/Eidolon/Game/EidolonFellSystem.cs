using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Felling (#677; README "Eidolon", felling): the axe in the eidolon's hand
/// (<see cref="EntityBehaviorEidolonAxe"/>), the <c>fell</c> order (<see cref="FellOrder"/>) and the
/// command tool's "fell trees here" mode, an area. Registered on both sides whatever the switch: with
/// it off the entity and the tool do not exist.
/// </summary>
public class EidolonFellSystem : ModSystem
{
    /// <summary>The fell mode's longest side, in blocks.</summary>
    public const int MaxAreaSide = 32;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorEidolonAxe.Code, typeof(EntityBehaviorEidolonAxe));
        EidolonOrders.Register(FellOrder.OrderCode, (_, args) => FellOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = "fell",
            Order = 50,
            Icon = new AssetLocation("game", "textures/icons/hack.svg"),
            Mark = Core.EidolonMarkKind.Area,
            MaxAreaSide = MaxAreaSide,
            Command = c => c.Eidolon.GetBehavior<EntityBehaviorEidolonAxe>()?.Axe == null
                ? EidolonCommand.Refuse("seraphhorizons:eidolon-fell-noaxe")
                : EidolonCommand.Order(FellOrder.OrderCode, FellOrder.Args(c.Area!.Value)),
        });
    }
}
