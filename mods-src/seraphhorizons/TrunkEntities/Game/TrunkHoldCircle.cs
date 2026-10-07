using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Client side: Carry On's filling circle for the trunk pick-up holds, off the ground and off
/// Logging Expanded's stations (sawhorses, Trunk Storage Rack, heating rack). Both are timed on the
/// server (<see cref="EntityBehaviorTrunkCarry"/>, <see cref="StationTake"/>), which sends nothing
/// back, so the client counts the same hold itself (<see cref="HoldProgress"/>) under the same
/// conditions: right button pressed and held on a trunk entity while sneaking, or on a station
/// holding a trunk (<see cref="TrunkStations.Offer"/>); both hands empty, nothing carried,
/// within reach; for <see cref="TrunkCarry.PickUpSeconds"/>. It shows it on Carry On's own
/// <c>HudOverlayRenderer</c> (<c>CarrySystem.HudOverlayRenderer</c>: <c>CircleProgress</c>,
/// <c>CircleVisible</c>), the circle Carry On's own pick-up and put-down fill, so the two look the
/// same. Carry On's interaction leaves the circle alone while it has no action of its own, which
/// it has none for a trunk entity. Without that renderer (one warning) there is no circle.
/// </summary>
public sealed class TrunkHoldCircle : IDisposable
{
    /// <summary>How often the hold is checked, ms.</summary>
    public const int TickMs = 20;

    /// <summary>The server's reach for the hold (<see cref="EntityBehaviorTrunkCarry"/>), blocks.</summary>
    public const double Reach = 6;

    private readonly ICoreClientAPI _api;
    private readonly HoldProgress _hold = new();
    private readonly long _listener;
    private object? _hud;
    private PropertyInfo? _progress, _visible;
    private bool _resolved, _shown, _wasDown;

    public TrunkHoldCircle(ICoreClientAPI api)
    {
        _api = api;
        _listener = api.Event.RegisterGameTickListener(Tick, TickMs);
    }

    private void Tick(float dt)
    {
        var player = _api.World.Player;
        bool down = _api.Input.InWorldMouseButton.Right;
        bool pressed = down && !_wasDown;
        _wasDown = down;
        var by = player?.Entity;
        if (by == null || !down || !TrunkCarry.HandsEmpty(by) || !TrunkCarry.Available(_api) || TrunkCarry.Carried(player!) != null
            || Target(player!, by) is not { } aim
            // the server starts the hold on the click itself: holding the button onto a trunk is not one
            || !(_hold.Active && _hold.Target == aim.Target || pressed))
        {
            if (_hold.Active)
                Hide();
            _hold.Reset();
            return;
        }
        _hold.Advance(aim.Target, aim.Seconds, dt);
        Show(_hold.Fraction);
    }

    /// <summary>What a right-click hold now would take, and its length: a trunk entity looked at
    /// while sneaking (its id), or a station holding a trunk (<see cref="StationTarget"/> of its
    /// cell); null for neither, or out of reach.</summary>
    private (long Target, float Seconds)? Target(IClientPlayer player, EntityPlayer by)
    {
        if (player.CurrentEntitySelection?.Entity is EntityTrunk { Alive: true, Trunk: { } stack } trunk)
            return (by.Controls.ShiftKey || by.Controls.Sneak) && by.Pos.DistanceTo(trunk.Pos) <= Reach
                ? (trunk.EntityId, TrunkCarry.PickUpSeconds(_api, stack.Block)) : null;
        if (player.CurrentBlockSelection?.Position is not { } pos || by.Pos.DistanceTo(pos.ToVec3d().Add(0.5, 0.5, 0.5)) > Reach
            || TrunkStations.Offer(_api.World, pos) is not { } offer)
            return null;
        return (StationTarget(pos), TrunkCarry.PickUpSeconds(_api, offer.Block));
    }

    /// <summary>A hold target for a station's cell, never an entity id (those are positive).</summary>
    private static long StationTarget(BlockPos pos) => -1 - (((long)pos.X & 0xFFFFFF) << 32 | ((long)pos.Z & 0xFFFFFF) << 8 | ((long)pos.Y & 0xFF));

    private void Show(float fraction)
    {
        if (Hud() is not { } hud)
            return;
        // CircleProgress shows the circle as it is set
        _progress!.SetValue(hud, Math.Max(fraction, 0.001f));
        _shown = true;
    }

    private void Hide()
    {
        if (_shown && Hud() is { } hud)
            _visible!.SetValue(hud, false);
        _shown = false;
    }

    private object? Hud()
    {
        if (_hud != null)
            return _hud;
        if (_resolved)
            return null;
        var system = _api.ModLoader.GetModSystem(TrunkCarry.CarrySystemName);
        if (system == null)
            return null;   // Carry On not yet started: try again next time
        _resolved = true;
        var hud = AccessTools.Property(system.GetType(), "HudOverlayRenderer")?.GetValue(system);
        _progress = hud == null ? null : AccessTools.Property(hud.GetType(), "CircleProgress");
        _visible = hud == null ? null : AccessTools.Property(hud.GetType(), "CircleVisible");
        if (hud == null || _progress?.CanWrite != true || _visible?.CanWrite != true)
        {
            _api.Logger.Warning("[seraphhorizons] Trunk entities: Carry On's HudOverlayRenderer is not as expected, so the trunk pick-up hold shows no circle");
            return null;
        }
        return _hud = hud;
    }

    public void Dispose()
    {
        Hide();
        _api.Event.UnregisterGameTickListener(_listener);
    }
}
