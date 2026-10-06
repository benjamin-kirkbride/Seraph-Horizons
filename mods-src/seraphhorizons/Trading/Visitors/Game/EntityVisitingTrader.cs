using SeraphHorizons.Mod.Trading.Visitors.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Trading.Visitors;

/// <summary>
/// A travelling merchant or curio dealer at an inn (#456), entity class
/// <c>SeraphHorizons.VisitingTrader</c> (<c>seraphhorizons:visitor-{gender}-{type}-{climate}</c>,
/// made by Trading/tools/make_entities.py with no revive and no fighting back). The pack's trader in
/// every other way: its list, prices, supply and standing (its trader id is
/// <c>visitor:&lt;kind&gt;</c>, set before it spawns). It takes no damage, and once a second asks
/// <see cref="InnSystem"/> whether its visit is over; it leaves (despawns) when it is, when its inn
/// is gone, or when the feature is off. Its visit (inn, kind, the day it leaves, the tier it stocks
/// for) is kept in <see cref="VisitAttr"/>, saved with it.
/// </summary>
public class EntityVisitingTrader : EntitySeraphTrader
{
    public new const string ClassName = "SeraphHorizons.VisitingTrader";
    public const string VisitAttr = "seraphhorizons:visit";

    private float _sinceCheck;

    private ITreeAttribute Visit
    {
        get
        {
            if (WatchedAttributes.GetTreeAttribute(VisitAttr) is { } tree) return tree;
            var created = new TreeAttribute();
            WatchedAttributes[VisitAttr] = created;
            return created;
        }
    }

    public string Kind => VisitorKinds.KindOf(TraderType) ?? VisitorKinds.General;

    public double LeaveDay
    {
        get => Visit.GetDouble("leaveday");
        set { Visit.SetDouble("leaveday", value); WatchedAttributes.MarkPathDirty(VisitAttr); }
    }

    public BlockPos InnPos
    {
        get => new(Visit.GetInt("innx"), Visit.GetInt("inny"), Visit.GetInt("innz"));
        set
        {
            Visit.SetInt("innx", value.X);
            Visit.SetInt("inny", value.Y);
            Visit.SetInt("innz", value.Z);
            WatchedAttributes.MarkPathDirty(VisitAttr);
        }
    }

    public override int StockTier => Visit.GetInt("stocktier");

    public void SetStockTier(int tier)
    {
        Visit.SetInt("stocktier", tier);
        WatchedAttributes.MarkPathDirty(VisitAttr);
    }

    /// <summary>Invulnerable while visiting: only healing gets through.</summary>
    public override bool ReceiveDamage(DamageSource damageSource, float damage) =>
        damageSource.Type == EnumDamageType.Heal && base.ReceiveDamage(damageSource, damage);

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (World.Side != EnumAppSide.Server || !Alive) return;
        _sinceCheck += dt;
        if (_sinceCheck < 1) return;
        _sinceCheck = 0;
        if (InnSystem.Of(Api) is { Active: true } inns) inns.CheckVisitor(this);
        else Die(EnumDespawnReason.Removed);
    }
}
