using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The AI task that carries out the eidolon's current order (<see cref="EntityBehaviorEidolonOrders"/>):
/// it runs while there is one and the eidolon can work, and hands each tick to the order. A task of
/// higher priority (self-defence, #675) cancels it and the order is started again after; a slump stops
/// it (<see cref="EntityLaborEidolon.StopWork"/>). Its priority is the entity type's (1.5).
/// </summary>
public class AiTaskEidolonOrder(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
    : AiTaskBase(entity, taskConfig, aiConfig)
{
    public const string Code = "seraphhorizons-eidolonorder";

    private IEidolonOrder? _order;

    private EntityLaborEidolon? Eidolon => entity as EntityLaborEidolon;

    public override bool ShouldExecute() =>
        Eidolon is { CanWork: true, Orders: { } orders } && orders.Current != null;

    public override void StartExecute()
    {
        base.StartExecute();
        _order = Eidolon?.Orders?.Current;
        if (_order != null && Eidolon != null)
            _order.Start(Eidolon);
    }

    public override bool ContinueExecute(float dt)
    {
        if (Eidolon is not { } eidolon || _order == null || !eidolon.CanWork || !ReferenceEquals(_order, eidolon.Orders?.Current))
            return false;
        if (_order.Continue(eidolon, dt))
            return true;
        var done = _order;
        _order = null;
        done.Stop(eidolon, cancelled: false);
        eidolon.Orders?.Done(done);
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        if (_order != null && Eidolon != null)
            _order.Stop(Eidolon, cancelled);
        _order = null;
    }
}
