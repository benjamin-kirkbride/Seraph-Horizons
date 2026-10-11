using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Guarding a point (#680; README "Eidolon", guarding): registers the <see cref="GuardOrder"/> and the
/// command tool's <c>guard</c> mode (wheel place 80; a block mark: the eidolon guards the top of the
/// marked block), on both sides, as the wheel is chosen by index.
/// </summary>
public class EidolonGuardSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        EidolonOrders.Register(GuardOrder.OrderCode, (_, args) => GuardOrder.From(args));
        EidolonCommandModes.Register(new EidolonCommandMode
        {
            Code = GuardOrder.OrderCode,
            Order = 80,
            Icon = new AssetLocation("game", "textures/icons/character/armor-body.svg"),
            Mark = Core.EidolonMarkKind.Block,
            Command = c => c.Target is { } block
                ? EidolonCommand.Order(GuardOrder.OrderCode, GuardOrder.Args(GuardOrder.PointOn(block)))
                : EidolonCommand.Refuse("seraphhorizons:eidoloncommander-mark-block"),
        });
    }
}
