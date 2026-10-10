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
/// goes for it at a run and strikes it (<see cref="EidolonStrikes"/>), each blow for
/// <see cref="EidolonConfig.DefenceDamage"/> (every third <see cref="EidolonConfig.SlamDamage"/>), until it
/// is dead, gone out of range, or has not hurt it for a while; the order task then starts its order
/// again. It closes by the wide pathfinder from afar and straight at the creature close by, aiming
/// where the creature's motion takes it, and keeps closing while it strikes on the move; it strikes
/// standing only at a creature standing in reach. Each blow is judged where the creature is when it
/// lands. Never a player or another eidolon (<see cref="EidolonDefence.Engages"/>). Like every task it
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

    /// <summary>Within this distance (centres, across, in blocks) and nearly level it walks straight at
    /// the creature instead of searching a path.</summary>
    public const double SteerRange = 8;

    /// <summary>How fast it turns to face the creature, in radians a second.</summary>
    public const float TurnRadPerSecond = 4.5f;

    /// <summary>Beyond this gap, or after a creature faster than this many blocks a second, it runs.</summary>
    private const double RunGap = 2, RunAfterSpeed = 1.5;

    private const double GiveUpSeconds = 45;

    private EntityAgent? _attacker;
    private double _hurtAt = double.MinValue;
    private EidolonNavigator? _nav;
    private int _blows;
    private Strike? _strike;
    private int _strikeNumber;
    private double _strikeEnds;
    private double _hitAt;
    private bool _struck;
    private double _giveUpAt;
    private double _nextPath;
    private double _pathOnlyUntil;
    private Vec3d? _leashPoint;
    private double _leash;
    private readonly Motion _own = new(), _theirs = new();

    /// <summary>The creature it is after, or null.</summary>
    public EntityAgent? Attacker => _attacker;

    /// <summary>Blows landed since it was made (for the scenarios).</summary>
    public int BlowsLanded { get; private set; }

    /// <summary>Blows that missed since it was made (for the scenarios).</summary>
    public int BlowsMissed { get; private set; }

    /// <summary>Blows landed on the move since it was made (for the scenarios).</summary>
    public int MovingBlowsLanded { get; private set; }

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
        _strike = null;
        _giveUpAt = Now + GiveUpSeconds;
        _nextPath = 0;
        _pathOnlyUntil = 0;
        _own.Reset();
        _theirs.Reset();
    }

    public override bool ContinueExecute(float dt)
    {
        if (_attacker is not { } target || _nav == null || Now > _giveUpAt)
            return false;
        _own.Track(entity.Pos, Now);
        _theirs.Track(target.Pos, Now);
        if (_strike is { } strike)
        {
            if (!_struck && Now >= _hitAt)
            {
                _struck = true;
                if (target.Alive && EidolonStrikes.Lands(strike, Gap(target), OffFacingDegrees(target), Level(target)))
                    Strike(target, strike);
                else
                    BlowsMissed++;
            }
            if (strike.Moving && target.Alive)
                Close(target);
            Face(target, dt);
            if (Now < _strikeEnds)
                return true;
            entity.AnimManager.StopAnimation(strike.Animation);
            _strike = null;
        }
        if (!Engaging(target))
        {
            _attacker = null;
            return false;
        }
        double gap = Gap(target);
        if (gap < double.MaxValue)
        {
            var radii = (entity.CollisionBox.XSize + target.CollisionBox.XSize) / 2;
            var next = EidolonStrikes.Choose(_blows, target.Pos.X - entity.Pos.X, target.Pos.Z - entity.Pos.Z, radii,
                _theirs.Vx, _theirs.Vz, _own.Vx, _own.Vz, target.CollisionBox.Y2 < EidolonStrikes.LowTarget);
            if (next != null)
            {
                Begin(next);
                if (next.Moving)
                    Close(target);
                else
                    _nav.Stop();
                Face(target, dt);
                return true;
            }
        }
        Close(target);
        if (gap < SteerRange)
            Face(target, dt);
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        _nav?.Stop();
        if (_strike != null)
            entity.AnimManager.StopAnimation(_strike.Animation);
        _strike = null;
    }

    private void Begin(Strike strike)
    {
        _strike = strike;
        _strikeNumber = _blows++;
        entity.AnimManager.StartAnimation(strike.Animation);
        _strikeEnds = Now + strike.Seconds;
        _hitAt = Now + strike.HitAt;
        _struck = false;
    }

    /// <summary>Goes on closing on the creature: straight at where its motion takes it when close and
    /// level (searching a path again after a stuck), by the wide pathfinder otherwise; it stands once
    /// within <see cref="EidolonStrikes.StandOff"/>.</summary>
    private void Close(EntityAgent target)
    {
        double gap = Gap(target);
        if (gap <= EidolonStrikes.StandOff)
        {
            if (_nav!.Active)
                _nav.Stop();
            return;
        }
        double speed = Math.Sqrt(_own.Vx * _own.Vx + _own.Vz * _own.Vz);
        double lead = gap < double.MaxValue ? EidolonStrikes.LeadSeconds(gap, speed) : 0;
        var aim = new Vec3d(target.Pos.X + _theirs.Vx * lead, target.Pos.Y, target.Pos.Z + _theirs.Vz * lead);
        bool run = gap > RunGap || Math.Sqrt(_theirs.Vx * _theirs.Vx + _theirs.Vz * _theirs.Vz) > RunAfterSpeed;
        double across = Math.Sqrt(Sq(target.Pos.X - entity.Pos.X) + Sq(target.Pos.Z - entity.Pos.Z));
        if (across <= SteerRange && Math.Abs(target.Pos.Y - entity.Pos.Y) < 1.2 && Now >= _pathOnlyUntil)
        {
            _nav!.Steer(aim, run, () => _pathOnlyUntil = Now + 3);
            return;
        }
        if (Now >= _nextPath || !_nav!.Active || _nav.Steering)
        {
            _nextPath = Now + 1;
            _nav!.GoTo(aim, true, () => { }, () => { }, tolerance: 1);
        }
    }

    private void Strike(EntityAgent target, Strike strike)
    {
        target.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = entity,
            Type = EnumDamageType.BluntAttack,
            DamageTier = 3,
            KnockbackStrength = 1,
        }, (EidolonStrikes.Heavy(_strikeNumber) ? Settings.SlamDamage : Settings.DefenceDamage) * GlobalConstants.CreatureDamageModifier);
        BlowsLanded++;
        if (strike.Moving)
            MovingBlowsLanded++;
        _hurtAt = Now;
    }

    /// <summary>The horizontal gap between the two boxes, in blocks (large when they are not level).</summary>
    private double Gap(Entity target)
    {
        if (!Level(target))
            return double.MaxValue;
        double dx = target.Pos.X - entity.Pos.X, dz = target.Pos.Z - entity.Pos.Z;
        return Math.Sqrt(dx * dx + dz * dz) - entity.CollisionBox.XSize / 2 - target.CollisionBox.XSize / 2;
    }

    /// <summary>Neither is above the other's box (a creature down to a block below its feet still counts).</summary>
    private bool Level(Entity target)
    {
        double dy = target.Pos.Y - entity.Pos.Y;
        return dy <= entity.CollisionBox.Y2 && dy >= -target.CollisionBox.Y2 - 1;
    }

    private double OffFacingDegrees(Entity target)
    {
        float toward = (float)Math.Atan2(target.Pos.X - entity.Pos.X, target.Pos.Z - entity.Pos.Z);
        return GameMath.AngleRadDistance(entity.Pos.Yaw, toward) * GameMath.RAD2DEG;
    }

    /// <summary>Turns toward the creature, at most <see cref="TurnRadPerSecond"/>.</summary>
    private void Face(Entity target, float dt)
    {
        float toward = (float)Math.Atan2(target.Pos.X - entity.Pos.X, target.Pos.Z - entity.Pos.Z);
        float turn = GameMath.AngleRadDistance(entity.Pos.Yaw, toward);
        float most = TurnRadPerSecond * dt;
        entity.Pos.Yaw = GameMath.Mod(entity.Pos.Yaw + GameMath.Clamp(turn, -most, most), GameMath.TWOPI);
    }

    private static double Sq(double v) => v * v;

    /// <summary>An entity's motion across, in blocks a second, from where it was each tick, smoothed
    /// (a knockback or a step does not swing the aim); a jump of more than a few blocks starts it again.</summary>
    private sealed class Motion
    {
        private double _x, _z, _at = double.NaN;

        public double Vx { get; private set; }

        public double Vz { get; private set; }

        public void Reset()
        {
            _at = double.NaN;
            Vx = Vz = 0;
        }

        public void Track(EntityPos pos, double now)
        {
            double dt = now - _at;
            if (double.IsNaN(_at) || dt > 1 || Sq(pos.X - _x) + Sq(pos.Z - _z) > 16)
            {
                (_x, _z, _at, Vx, Vz) = (pos.X, pos.Z, now, 0, 0);
                return;
            }
            if (dt < 0.04)
                return;
            const double keep = 0.5;
            Vx = keep * Vx + (1 - keep) * (pos.X - _x) / dt;
            Vz = keep * Vz + (1 - keep) * (pos.Z - _z) / dt;
            (_x, _z, _at) = (pos.X, pos.Z, now);
        }
    }
}
