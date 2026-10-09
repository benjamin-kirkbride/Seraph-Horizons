using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Self-defence (#675; README "Eidolon", self-defence): when a creature hurts the eidolon (the
/// cause of the damage, so an archer too), this task, above the order task (priority 1.6 to its 1.5),
/// goes for it at a run by the wide pathfinder and strikes it with a punch, a kick and a slam in turn
/// (<see cref="EidolonDefence.Blow"/>), each for <see cref="EidolonConfig.DefenceDamage"/> (a slam
/// <see cref="EidolonConfig.SlamDamage"/>), until it
/// is dead, gone out of range, or has not hurt it for a while; the order task then starts its order
/// again. Never a player or another eidolon (<see cref="EidolonDefence.Engages"/>). Like every task it
/// starts only while the eidolon can work.
/// </summary>
public class AiTaskEidolonDefend(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
    : AiTaskBase(entity, taskConfig, aiConfig)
{
    public const string Code = "seraphhorizons-eidolondefend";

    /// <summary>How long it keeps after a creature that hurt it (each blow it lands starts it again).</summary>
    public const double MemorySeconds = 12;

    /// <summary>How far it goes after one, in blocks.</summary>
    public const double Range = 24;

    /// <summary>The gap between their boxes, in blocks, within which a blow reaches.</summary>
    public const double Reach = 1.6;

    private const double GiveUpSeconds = 45;

    private EntityAgent? _attacker;
    private double _hurtAt = double.MinValue;
    private EidolonNavigator? _nav;
    private int _blows;
    private string? _animation;
    private double _blowEnds;
    private double _hitAt;
    private bool _struck;
    private double _giveUpAt;
    private double _nextPath;
    private bool _slam;
    private Vec3d? _leashPoint;
    private double _leash;

    /// <summary>The creature it is after, or null.</summary>
    public EntityAgent? Attacker => _attacker;

    /// <summary>Blows landed since it was made (for the scenarios).</summary>
    public int BlowsLanded { get; private set; }

    private double Now => entity.World.ElapsedMilliseconds / 1000.0;

    private EidolonConfig Settings => EidolonSystem.Of(entity.Api)?.Config ?? EidolonConfig.Defaults;

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        if (entity.Api.Side == EnumAppSide.Server && source.Type != EnumDamageType.Heal && source.GetCauseEntity() is EntityAgent cause
            && cause != entity)
            Engage(cause);
    }

    /// <summary>Goes after <paramref name="target"/> as if it had just hurt the eidolon, unless it is
    /// a player or an eidolon (the guard order's seam, #680). With a <paramref name="leashPoint"/> it
    /// lets the creature go once it is more than <paramref name="leash"/> blocks from that point (the
    /// guard's: it does not chase off its post); a creature that hurts it is fought without one.</summary>
    public bool Engage(EntityAgent target, Vec3d? leashPoint = null, double leash = 0)
    {
        if (target is EntityPlayer or EntityLaborEidolon || !target.Alive)
            return false;
        _attacker = target;
        _hurtAt = Now;
        _leashPoint = leashPoint?.Clone();
        _leash = leash;
        return true;
    }

    private bool Engaging(EntityAgent? a) =>
        a != null && a.Pos.Dimension == entity.Pos.Dimension
                  && EidolonDefence.Engages(a is EntityPlayer, a is EntityLaborEidolon, a.Alive, Now - _hurtAt, a.Pos.DistanceTo(entity.Pos.XYZ),
                      MemorySeconds, Range)
                  && (_leashPoint == null || a.Pos.DistanceTo(_leashPoint) <= _leash);

    public override bool ShouldExecute()
    {
        if (_attacker == null)
            return false;
        if (Engaging(_attacker))
            return true;
        _attacker = null;
        return false;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        _nav ??= new EidolonNavigator((EntityLaborEidolon)entity);
        _animation = null;
        _giveUpAt = Now + GiveUpSeconds;
        _nextPath = 0;
    }

    public override bool ContinueExecute(float dt)
    {
        if (_attacker is not { } target || _nav == null || Now > _giveUpAt)
            return false;
        if (_animation != null)
        {
            Face(target);
            if (!_struck && Now >= _hitAt)
            {
                _struck = true;
                if (target.Alive && Gap(target) <= Reach + 0.5)
                    Strike(target);
            }
            if (Now < _blowEnds)
                return true;
            entity.AnimManager.StopAnimation(_animation);
            _animation = null;
        }
        if (!Engaging(target))
        {
            _attacker = null;
            return false;
        }
        if (Gap(target) <= Reach)
        {
            if (_nav.Active)
                _nav.Stop();
            Face(target);
            var (animation, seconds, hitAt, slam) = EidolonDefence.Blow(_blows++);
            _slam = slam;
            entity.AnimManager.StartAnimation(animation);
            _animation = animation;
            _blowEnds = Now + seconds;
            _hitAt = Now + hitAt;
            _struck = false;
            return true;
        }
        if (Now >= _nextPath || !_nav.Active)
        {
            _nextPath = Now + 1;
            _nav.GoTo(target.Pos.XYZ, true, () => { }, () => { }, tolerance: 1);
        }
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        _nav?.Stop();
        if (_animation != null)
            entity.AnimManager.StopAnimation(_animation);
        _animation = null;
    }

    private void Strike(EntityAgent target)
    {
        target.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = entity,
            Type = EnumDamageType.BluntAttack,
            DamageTier = 3,
            KnockbackStrength = 1,
        }, (_slam ? Settings.SlamDamage : Settings.DefenceDamage) * GlobalConstants.CreatureDamageModifier);
        BlowsLanded++;
        _hurtAt = Now;
    }

    /// <summary>The horizontal gap between the two boxes, in blocks (large when one is far above the other).</summary>
    private double Gap(Entity target)
    {
        double dy = target.Pos.Y - entity.Pos.Y;
        if (dy > entity.CollisionBox.Y2 || dy < -target.CollisionBox.Y2 - 1)
            return double.MaxValue;
        double dx = target.Pos.X - entity.Pos.X, dz = target.Pos.Z - entity.Pos.Z;
        return Math.Sqrt(dx * dx + dz * dz) - entity.CollisionBox.XSize / 2 - target.CollisionBox.XSize / 2;
    }

    private void Face(Entity target) =>
        entity.Pos.Yaw = (float)Math.Atan2(target.Pos.X - entity.Pos.X, target.Pos.Z - entity.Pos.Z);
}
