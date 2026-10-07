using System.Runtime.CompilerServices;
using ProtoBuf;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>Whether <c>/clear stay</c> holds the sun: sent by the server to every client when the
/// lock starts or ends, and to each player as they join.</summary>
[ProtoContract]
public sealed class ClearSkyPacket
{
    [ProtoMember(1)] public bool Held { get; set; }
}

/// <summary>
/// The sun of <c>/clear stay</c>, held at summer noon while the clock runs on. Every sun position
/// and daylight strength the game works out goes through the calendar's
/// <c>OnGetSolarSphericalCoords</c> (<c>GameCalendar.GetCelestialAngles</c>, which hands it the
/// year and day fractions): the server's for spawning, rifts, beehives and the like, the client's
/// for the sky, the sun and the light. The survival mod installs the real one on each side at load
/// (<c>SurvivalCoreSystem.GetSolarSphericalCoords</c>, from those fractions alone). The hold wraps
/// whatever is installed and calls it with noon and the hemisphere's midsummer
/// (<see cref="ClearSkyPlan.MidsummerYearRel"/>). Kept per calendar: in singleplayer the server's
/// and the client's are two in one process. Nothing of it is saved.
/// </summary>
public static class ClearSkySun
{
    public const string Channel = "seraphhorizons:clearsky";

    private static readonly ConditionalWeakTable<IGameCalendar, DelegateWrap<SolarSphericalCoordsDelegate>> Wraps = new();

    /// <summary>Holds the sun on <paramref name="calendar"/>: wraps the installed delegate unless it
    /// is ours already. Called again and again, so a delegate installed over ours is wrapped too.</summary>
    public static void Hold(IGameCalendar calendar)
    {
        var wrap = Wraps.GetValue(calendar, _ => new DelegateWrap<SolarSphericalCoordsDelegate>());
        if (wrap.Wrap(calendar.OnGetSolarSphericalCoords, inner => Pinned(calendar, wrap, inner)) is { } install)
            calendar.OnGetSolarSphericalCoords = install;
    }

    /// <summary>Lets the sun go on <paramref name="calendar"/>: puts back the delegate it wrapped.</summary>
    public static void Release(IGameCalendar calendar)
    {
        if (Wraps.TryGetValue(calendar, out var wrap) && wrap.Unwrap(calendar.OnGetSolarSphericalCoords) is { } restore)
            calendar.OnGetSolarSphericalCoords = restore;
    }

    /// <summary>Whether the sun is held on <paramref name="calendar"/>.</summary>
    public static bool IsHeld(IGameCalendar calendar) => Wraps.TryGetValue(calendar, out var wrap) && wrap.Held;

    private static SolarSphericalCoordsDelegate Pinned(IGameCalendar calendar,
        DelegateWrap<SolarSphericalCoordsDelegate> wrap, SolarSphericalCoordsDelegate inner)
    {
        SolarSphericalCoordsDelegate? self = null;
        self = (posX, posZ, yearRel, dayRel) =>
        {
            // A wrapper no longer in force (released, or something else still calls it) passes through.
            if (!wrap.IsLive(self!))
                return inner(posX, posZ, yearRel, dayRel);
            bool southern = calendar.OnGetHemisphere?.Invoke(posX, posZ) == EnumHemisphere.South;
            return inner(posX, posZ, ClearSkyPlan.MidsummerYearRel(southern), ClearSkyPlan.NoonDayRel);
        };
        return self;
    }
}

/// <summary>
/// The client's side of <c>/clear stay</c>: the sky is drawn from the client's own calendar, so it
/// holds the sun there too while the server says so. Rechecked on a tick, since the survival mod
/// installs the client's sun at <c>LevelFinalize</c>, after a packet may have come.
/// </summary>
public sealed class ClearSkyClient
{
    private readonly ICoreClientAPI _api;
    private readonly long _listener;
    private bool _held;

    public ClearSkyClient(ICoreClientAPI api)
    {
        _api = api;
        api.Network.GetChannel(ClearSkySun.Channel).SetMessageHandler<ClearSkyPacket>(packet =>
        {
            _held = packet.Held;
            Upkeep();
        });
        _listener = api.Event.RegisterGameTickListener(_ => Upkeep(), 250);
    }

    private void Upkeep()
    {
        if (_api.World?.Calendar is not { } calendar)
            return;
        if (_held)
            ClearSkySun.Hold(calendar);
        else if (ClearSkySun.IsHeld(calendar))
            ClearSkySun.Release(calendar);
    }

    /// <summary>Lets the sun go when the client leaves the world.</summary>
    public void Dispose()
    {
        _held = false;
        try
        {
            _api.Event.UnregisterGameTickListener(_listener);
            if (_api.World?.Calendar is { } calendar)
                ClearSkySun.Release(calendar);
        }
        catch (Exception e)
        {
            // The client may be half torn down already; its calendar goes with it.
            _api.Logger.Debug("[seraphhorizons] /clear stay: could not let the client's sun go on leaving: {0}", e.Message);
        }
    }
}
