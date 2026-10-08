using System.Text;
using SeraphHorizons.Mod.Handcar.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Handcar;

/// <summary>
/// The handcar's own behaviour, on Yang's standard-gauge vehicle (modelled on Yang's
/// <c>SteamPowered</c>, which drives its engine carts): its riders' keys drive it.
///
/// <para>Server: each tick it reads both seats' keys (the game hands a mounted player's keys to the
/// seat): forward or back pumps, left or right moves the branch selector, Yang's turn lever, one
/// place (edge-triggered: each press is one step). The drive (<see cref="Drive"/>) is asked for by
/// Yang's own tick through <see cref="HandcarPatches.DrivePrefix"/>. A pumping rider in survival
/// spends satiety as sprinting does. Which seats pump is synced (<see cref="PumpingKey"/>, a bit per
/// seat) for the clients' animation.</para>
///
/// <para>Client: the branch selector's lever is redrawn when it moves (Yang's tesselation shows the
/// <c>TNL_*</c> element of the place), and the car is handed to the <see cref="HandcarAnimator"/>.</para>
/// </summary>
public sealed class EntityBehaviorHumanPowered(Entity entity) : EntityBehavior(entity)
{
    public const string Code = "seraphhorizons.HumanPowered";
    /// <summary>Watched attribute: bit i set while seat i's rider pumps.</summary>
    public const string PumpingKey = "seraphhorizons:handcarPumping";
    /// <summary>Yang's turn lever (<c>RailwayVehicleShared.AttributeTurnLeverPhase</c>), read by its path
    /// tape from the convoy's lead.</summary>
    public const string TurnKey = "turnLeverPhase";

    private HandcarSystem? _system;
    private EntityBehaviorSeatable? _seatable;
    private int _selfWeight = 1;
    private SeatInput[] _inputs = [];
    private bool[] _left = [], _right = [];
    private double[] _pumpSeconds = [];
    private float _satietyClock;

    public override string PropertyName() => Code;

    /// <summary>The seats' state from the last tick (server).</summary>
    public IReadOnlyList<SeatInput> Inputs => _inputs;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        _system = HandcarSystem.Of(entity.Api);
        _selfWeight = Math.Max(1, properties.Attributes?["SGLocomotive"]?["SelfWeight"].AsInt(1) ?? 1);
        if (entity.Api.Side == EnumAppSide.Server)
        {
            int phase = HandcarTurn.Move(entity.Attributes.GetInt(TurnKey, HandcarTurn.Straight), 0);
            entity.Attributes.SetInt(TurnKey, phase);
            entity.WatchedAttributes.SetInt(TurnKey, phase);
            entity.WatchedAttributes.SetInt(PumpingKey, 0);
        }
        else
        {
            entity.WatchedAttributes.RegisterModifiedListener(TurnKey, entity.MarkShapeModified);
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        _seatable = entity.GetBehavior<EntityBehaviorSeatable>();
        int n = _seatable?.Seats?.Length ?? 0;
        _inputs = new SeatInput[n];
        _left = new bool[n];
        _right = new bool[n];
        _pumpSeconds = new double[n];
        if (entity.Api is ICoreClientAPI)
            _system?.Animator?.Track(entity);
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (entity.Api is ICoreClientAPI)
            _system?.Animator?.Untrack(entity);
        base.OnEntityDespawn(despawn);
    }

    /// <summary>The seat's facing (+1 the car's front) from the rig's seats, by seat id; a seat the
    /// rig does not know faces the front.</summary>
    private int FacingOf(IMountableSeat seat) =>
        _system?.Rig is { } rig && seat.SeatId is { } id && rig.Seats.TryGetValue(id, out var s) ? s.Facing : 1;

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);
        if (entity.Api.Side != EnumAppSide.Server || _seatable?.Seats is not { } seats || _system == null)
            return;
        if (seats.Length != _inputs.Length)
            AfterInitialized(false);
        int mask = 0, turn = entity.WatchedAttributes.GetInt(TurnKey, HandcarTurn.Straight), was = turn;
        for (int i = 0; i < seats.Length; i++)
        {
            var seat = seats[i];
            var rider = seat.Passenger as EntityAgent;
            var keys = seat.Controls;
            bool on = rider is { Alive: true } && keys != null;
            int facing = FacingOf(seat);
            _inputs[i] = new SeatInput(on, on && keys!.Forward, on && keys!.Backward, on && keys!.Left, on && keys!.Right, facing);
            bool left = _inputs[i].Left, right = _inputs[i].Right;
            turn = HandcarTurn.Move(turn, HandcarTurn.Step(left && !_left[i], right && !_right[i], facing));
            (_left[i], _right[i]) = (left, right);
            if (HandcarDrive.Pumping(_inputs[i]))
            {
                mask |= 1 << i;
                _pumpSeconds[i] += deltaTime;
            }
        }
        if (turn != was)
        {
            entity.Attributes.SetInt(TurnKey, turn);
            entity.WatchedAttributes.SetInt(TurnKey, turn);
        }
        if (entity.WatchedAttributes.GetInt(PumpingKey) != mask)
            entity.WatchedAttributes.SetInt(PumpingKey, mask);
        _satietyClock += deltaTime;
        if (_satietyClock >= 1f)
        {
            _satietyClock = 0f;
            SpendSatiety(seats);
        }
    }

    /// <summary>What the riders pumped since last time costs them, as sprinting would (survival only).</summary>
    private void SpendSatiety(IMountableSeat[] seats)
    {
        var world = entity.World;
        var config = _system!.Config;
        for (int i = 0; i < seats.Length && i < _pumpSeconds.Length; i++)
        {
            double seconds = _pumpSeconds[i];
            _pumpSeconds[i] = 0;
            if (seconds <= 0 || seats[i].Passenger is not EntityPlayer player)
                continue;
            if (world.PlayerByUid(player.PlayerUID)?.WorldData?.CurrentGameMode != EnumGameMode.Survival)
                continue;
            double satiety = HandcarDrive.Satiety(seconds, config.SatietyPerPumpSecond, world.Calendar.SpeedOfTime, world.Calendar.CalendarSpeedMul);
            if (satiety > 0)
                player.GetBehavior<EntityBehaviorHunger>()?.ConsumeSaturation((float)satiety);
        }
    }

    /// <summary>The drive for this tick, asked for by Yang's tick when this car leads its convoy
    /// (server): <paramref name="speed"/> and <paramref name="motion"/> are the car's, the weight the
    /// convoy's.</summary>
    public DriveCommand Drive(double speed, int motion, int convoyWeight) =>
        _system == null ? default : HandcarDrive.Command(_inputs, speed, motion, convoyWeight, _selfWeight, _system.Config);

    public override void GetInfoText(StringBuilder infotext)
    {
        base.GetInfoText(infotext);
        int mask = entity.WatchedAttributes.GetInt(PumpingKey);
        int pumping = 0;
        for (int b = mask; b != 0; b &= b - 1)
            pumping++;
        int seats = _seatable?.Seats?.Length ?? 0;
        int riding = _seatable?.Seats?.Count(s => s.Passenger != null) ?? 0;
        infotext.AppendLine(Lang.Get("seraphhorizons:handcar-info-riders", riding, seats, pumping));
        string branch = entity.WatchedAttributes.GetInt(TurnKey, HandcarTurn.Straight) switch
        {
            HandcarTurn.Left => "left",
            HandcarTurn.Right => "right",
            _ => "straight",
        };
        infotext.AppendLine(Lang.Get("seraphhorizons:handcar-info-branch", Lang.Get("seraphhorizons:handcar-branch-" + branch)));
    }
}
