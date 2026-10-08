using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SeraphHorizons.Mod.Handcar;

/// <summary>
/// What the handcar reaches of Yang's Transport Tycoon (<c>yangtransport</c> 1.0.3), by name: it is
/// not referenced at build time (the mod builds from the game alone), and every class it would
/// extend is sealed, so it rides on Yang's own standard-gauge vehicle class and patches where that
/// class decides. Found once; if anything is missing the handcar is left out with one warning.
/// <list type="bullet">
/// <item><c>EntityStandardGaugeLocomotive.TryComputeConvoyDrive</c>: the drive a convoy's lead gives
/// its convoy each tick, which Yang takes only from the steam engine behaviour.</item>
/// <item>Its private <c>Speed</c> (unsigned, blocks a second) and <c>LeadEnd</c> (<c>SGTrainEnd</c>:
/// EndA, the car's front, or EndB): which way and how fast the car rolls.</item>
/// <item><c>RailVehicleSeat.SeatPosition</c> and <c>RenderTransform</c>: where a rider stands and how
/// they are drawn.</item>
/// </list>
/// </summary>
public sealed class YangBridge
{
    public const string ModId = "yangtransport";
    public const string LocomotiveTypeName = "YangTransport.EntityStandardGaugeLocomotive";
    public const string SeatTypeName = "YangTransport.RailVehicleSeat";
    /// <summary>The class name of Yang's standard-gauge vehicles, in entity JSON.</summary>
    public const string EntityClass = "yangtransport.sglocomotive";

    public Type Locomotive { get; }
    public Type Seat { get; }
    public MethodInfo Drive { get; }
    public MethodInfo SeatPosition { get; }
    public MethodInfo RenderTransform { get; }
    private readonly FieldInfo _speed;
    private readonly FieldInfo _leadEnd;
    private readonly object _endA;

    private YangBridge(Type locomotive, Type seat, MethodInfo drive, MethodInfo seatPosition, MethodInfo renderTransform,
                       FieldInfo speed, FieldInfo leadEnd, object endA)
    {
        Locomotive = locomotive;
        Seat = seat;
        Drive = drive;
        SeatPosition = seatPosition;
        RenderTransform = renderTransform;
        _speed = speed;
        _leadEnd = leadEnd;
        _endA = endA;
    }

    /// <summary>Finds everything, or returns null with the problems.</summary>
    public static YangBridge? Resolve(out List<string> problems)
    {
        problems = [];
        var loco = AccessTools.TypeByName(LocomotiveTypeName);
        var seat = AccessTools.TypeByName(SeatTypeName);
        if (loco == null)
            problems.Add($"no {LocomotiveTypeName}");
        if (seat == null)
            problems.Add($"no {SeatTypeName}");
        if (loco == null || seat == null)
            return null;
        var drive = AccessTools.Method(loco, "TryComputeConvoyDrive");
        if (drive == null || drive.ReturnType != typeof(bool)
            || drive.GetParameters().Select(p => p.Name).ToArray() is not ["leadVehicle", "convoyLength", "convoyWeight", "normalizedThrottle", "accelerationBPSPerSec", "maxSpeedBPS"])
            problems.Add("no TryComputeConvoyDrive(leadVehicle, convoyLength, convoyWeight, out normalizedThrottle, out accelerationBPSPerSec, out maxSpeedBPS)");
        var speed = AccessTools.Field(loco, "Speed");
        if (speed?.FieldType != typeof(double))
            problems.Add("no double Speed field");
        var lead = AccessTools.Field(loco, "LeadEnd");
        object? endA = null;
        if (lead == null || !lead.FieldType.IsEnum || !Enum.GetNames(lead.FieldType).Contains("EndA"))
            problems.Add("no LeadEnd field of an enum with EndA");
        else
            endA = Enum.Parse(lead.FieldType, "EndA");
        var seatPos = AccessTools.PropertyGetter(seat, "SeatPosition");
        if (seatPos == null || seatPos.ReturnType != typeof(EntityPos))
            problems.Add("no RailVehicleSeat.SeatPosition");
        var render = AccessTools.PropertyGetter(seat, "RenderTransform");
        if (render == null || render.ReturnType != typeof(Vintagestory.API.Client.Matrixf))
            problems.Add("no RailVehicleSeat.RenderTransform");
        if (problems.Count > 0)
            return null;
        return new YangBridge(loco, seat, drive!, seatPos!, render!, speed!, lead!, endA!);
    }

    /// <summary>The car's speed, blocks a second, unsigned.</summary>
    public double Speed(Entity car) => _speed.GetValue(car) is double d ? d : 0;

    /// <summary>+1 if the car's drive points to its front (Yang's EndA leads), -1 if to its back.</summary>
    public int Motion(Entity car) => Equals(_leadEnd.GetValue(car), _endA) ? 1 : -1;

    public bool IsLocomotive(Entity? entity) => entity != null && Locomotive.IsInstanceOfType(entity);
}
