using SeraphHorizons.Mod.Handcar.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Handcar;

/// <summary>
/// Client: moves everything the pump drives from one number, the distance the car has rolled
/// (<see cref="TravelTracker"/>), every frame: the car's <c>pump</c> animation (wheels, gears, crank,
/// pitman and beam) and both riders' animations (the seat's grip and the pump's effort), which have
/// a frame per sixtieth of the beam's stroke. Their frame is set from the stroke's phase, so the
/// riders' hands stay on the handles at any speed, rolling or stopped, forwards or back.
///
/// <para>The animations are written with a tiny speed, so the game never runs them on its own (their
/// frame moves by a few hundred-thousandths a second), and ease speeds scaled to match, so the game
/// still eases them by itself at about 1 a second. Their easing is set here every frame, ahead of
/// the game's own easing step (<see cref="AnimationEasing.Before"/>), so the grip's and the effort's always add up to
/// the rider's own fade: in over <see cref="MountFadeSeconds"/> when they get on, out when they get
/// off; and between the two the effort comes in over <see cref="HandcarConfig.PumpFadeSeconds"/>
/// while the seat's rider pumps (the server says which, <see cref="EntityBehaviorHumanPowered.PumpingKey"/>)
/// and goes out when they stop. Both animations put the hands on the handle, so the hands never
/// leave it in between.</para>
///
/// <para>Runs before the entities' own frame (render stage Before, order 0.35: after the game
/// interpolates entity positions at 0 and before it renders and animates the entities at 0.4), so
/// the car, its riders and their animators all see this frame's numbers.</para>
/// </summary>
public sealed class HandcarAnimator : IRenderer
{
    /// <summary>Seconds a rider's grip takes to come in when they get on, and to go when they get off.</summary>
    public const double MountFadeSeconds = 0.25;

    private sealed class Rider(long id, HandcarSeat riding)
    {
        public long Id { get; } = id;
        public HandcarSeat Riding { get; } = riding;
        public double Mount;
        public readonly EffortFade Effort = new();
        public bool PumpStarted;
    }

    private sealed class Car(Entity entity)
    {
        public Entity Entity { get; } = entity;
        public readonly TravelTracker Travel = new();
        public readonly Dictionary<string, Rider> Riders = new();
        public float Frame;
    }

    private readonly ICoreClientAPI _capi;
    private readonly HandcarSystem _system;
    private readonly Dictionary<long, Car> _cars = new();
    /// <summary>Riders who got off, their grip and effort easing out (with the frame they left at).</summary>
    private readonly List<(Rider Rider, float Frame)> _leaving = new();
    private readonly List<long> _gone = new();

    public double RenderOrder => 0.35;
    public int RenderRange => 999;

    public HandcarAnimator(ICoreClientAPI capi, HandcarSystem system)
    {
        _capi = capi;
        _system = system;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "seraphhorizons:handcar");
    }

    public void Track(Entity car) => _cars[car.EntityId] = new Car(car);

    public void Untrack(Entity car)
    {
        if (!_cars.Remove(car.EntityId, out var state))
            return;
        foreach (var rider in state.Riders.Values)
            _leaving.Add((rider, state.Frame));
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (_system.Rig is not { } rig)
            return;
        _gone.Clear();
        foreach (var (id, car) in _cars)
        {
            var e = car.Entity;
            if (e.State == EnumEntityState.Despawned)
            {
                _gone.Add(id);
                continue;
            }
            car.Travel.Update(e.Pos.X, e.Pos.InternalY, e.Pos.Z, e.Pos.Yaw);
            car.Frame = HandcarPhase.Frame(HandcarPhase.Of(car.Travel.Distance, rig.DistancePerCycle), rig.Frames);
            PoseCar(e, rig.BodyAnimation, car.Frame, dt);
            PoseRiders(car, rig, dt);
        }
        foreach (var id in _gone)
            if (_cars.TryGetValue(id, out var car))
                Untrack(car.Entity);
        FadeLeaving(dt);
    }

    private static void PoseCar(Entity car, string animation, float frame, float dt)
    {
        var manager = car.AnimManager;
        if (manager?.Animator == null)
            return;
        if (!manager.IsAnimationActive(animation))
            manager.StartAnimation(animation);
        Set(manager.Animator, animation, frame, 1f, dt);
    }

    private void PoseRiders(Car car, HandcarRig rig, float dt)
    {
        var seats = car.Entity.GetBehavior<EntityBehaviorSeatable>()?.Seats;
        if (seats == null)
            return;
        int mask = car.Entity.WatchedAttributes.GetInt(EntityBehaviorHumanPowered.PumpingKey);
        for (int i = 0; i < seats.Length; i++)
        {
            var seat = seats[i];
            if (seat.SeatId is not { } seatId || !rig.Seats.TryGetValue(seatId, out var riding))
                continue;
            car.Riders.TryGetValue(seatId, out var rider);
            var agent = seat.Passenger as EntityAgent;
            if (rider != null && (agent == null || !agent.Alive || rider.Id != agent.EntityId))
            {
                _leaving.Add((rider, car.Frame));
                car.Riders.Remove(seatId);
                rider = null;
            }
            if (agent == null || !agent.Alive)
                continue;
            if (rider == null)
            {
                rider = car.Riders[seatId] = new Rider(agent.EntityId, riding);
                _leaving.RemoveAll(l => l.Rider.Id == agent.EntityId);
            }
            rider.Mount = Math.Min(1, rider.Mount + dt / MountFadeSeconds);
            bool pumping = (mask >> i & 1) != 0;
            rider.Effort.Advance(pumping, dt, _system.Config.PumpFadeSeconds);
            Pose(agent, rider, car.Frame, dt, pumping);
        }
    }

    private void Pose(EntityAgent agent, Rider rider, float frame, float dt, bool pumping)
    {
        var (grip, pump) = rider.Effort.Weights(rider.Mount);
        if (pump > 0 && !rider.PumpStarted && agent.Properties.Client.AnimationsByMetaCode.TryGetValue(rider.Riding.Pump, out var meta))
        {
            agent.AnimManager?.StartAnimation(meta);
            rider.PumpStarted = true;
        }
        else if (pump <= 0 && !pumping && rider.PumpStarted)
        {
            agent.AnimManager?.StopAnimation(rider.Riding.Pump);
            rider.PumpStarted = false;
        }
        foreach (var animator in Animators(agent))
        {
            Set(animator, rider.Riding.Grip, frame, grip, dt);
            Set(animator, rider.Riding.Pump, frame, pump, dt);
        }
    }

    /// <summary>Riders who got off: both animations eased out over <see cref="MountFadeSeconds"/>
    /// (the seat has stopped the grip; the effort is stopped here once it is out).</summary>
    private void FadeLeaving(float dt)
    {
        for (int i = _leaving.Count - 1; i >= 0; i--)
        {
            var (rider, frame) = _leaving[i];
            rider.Mount = Math.Max(0, rider.Mount - dt / MountFadeSeconds);
            var agent = _capi.World.GetEntityById(rider.Id) as EntityAgent;
            if (agent != null)
                Pose(agent, rider, frame, dt, false);
            if (rider.Mount <= 0 || agent == null)
            {
                if (agent != null && rider.PumpStarted)
                    agent.AnimManager?.StopAnimation(rider.Riding.Pump);
                _leaving.RemoveAt(i);
            }
        }
    }

    /// <summary>Every animator the game may draw a rider with: a player has its third-person one and,
    /// for this client's own player, the first-person one.</summary>
    private static IEnumerable<IAnimator> Animators(EntityAgent agent)
    {
        if (agent is EntityPlayer player)
        {
            if (player.TpAnimManager?.Animator is { } tp)
                yield return tp;
            if (player.SelfFpAnimManager?.Animator is { } fp && !ReferenceEquals(fp, player.TpAnimManager?.Animator))
                yield return fp;
        }
        else if (agent.AnimManager?.Animator is { } a)
        {
            yield return a;
        }
    }

    private static void Set(IAnimator animator, string code, float frame, float easing, float dt)
    {
        if (animator.GetAnimationState(code) is not { Running: true, meta: { } meta } state)
            return;
        state.CurrentFrame = frame;
        state.EasingFactor = AnimationEasing.Before(easing, state.Active, Math.Abs(dt * meta.GetCurrentAnimationSpeed(1f)) * (state.Active ? meta.EaseInSpeed : meta.EaseOutSpeed));
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        _cars.Clear();
        _leaving.Clear();
    }
}
