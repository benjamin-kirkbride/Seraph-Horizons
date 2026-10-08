using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Handcar.Core;

/// <summary>A seat's riding: its animations (the grip, the seat's own, and the pump eased over it)
/// and which way its rider faces, turned about the vertical by <see cref="Turn"/> degrees from
/// the car's front.</summary>
public sealed record HandcarSeat(string Id, string Grip, string Pump, float Turn)
{
    /// <summary>+1 if the rider faces the car's front, -1 if its back.</summary>
    public int Facing => Math.Cos(Turn * Math.PI / 180) >= 0 ? 1 : -1;
}

/// <summary>
/// What the gameplay reads from the handcar's rig (<c>assets/seraphhorizons/config/handcar-rig.json</c>,
/// written by <c>Handcar/tools/make_shape.py</c>): the pump's cycle (its body animation, frames per
/// stroke and the travel a stroke takes) and the seats' riding. The model's parts and anchors are
/// the generator's and the viewer's; the shared rig maths (<see cref="RigParts"/>) reads the parts.
/// </summary>
public sealed record HandcarRig(string BodyAnimation, int Frames, double DistancePerCycle, double WheelRadius, double AxleTurns,
                                IReadOnlyDictionary<string, HandcarSeat> Seats)
{
    /// <summary>Parses the rig; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static HandcarRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("cycle", out var cycle) || cycle.ValueKind != JsonValueKind.Object)
            throw new FormatException("the rig has no cycle");
        string anim = Str(cycle, "animation");
        int frames = cycle.TryGetProperty("frames", out var f) && f.TryGetInt32(out int n) && n > 1 ? n : throw new FormatException("cycle.frames must be above 1");
        double distance = Num(cycle, "distancePerCycle");
        double radius = Num(cycle, "wheelRadius");
        double turns = Num(cycle, "axleTurns");
        if (Math.Abs(distance - 2 * Math.PI * radius * turns) > 1e-4)
            throw new FormatException($"cycle.distancePerCycle {distance} is not 2 pi x wheelRadius x axleTurns");
        if (!root.TryGetProperty("riders", out var riders) || riders.ValueKind != JsonValueKind.Object)
            throw new FormatException("the rig has no riders");
        var seats = new Dictionary<string, HandcarSeat>();
        foreach (var rider in riders.EnumerateObject())
        {
            string key = "seat" + char.ToUpperInvariant(rider.Name[0]) + rider.Name[1..];
            if (!root.TryGetProperty(key, out var seat) || seat.ValueKind != JsonValueKind.Object)
                throw new FormatException($"riders.{rider.Name} has no {key}");
            float turn = (float)Num(seat, "turn");
            seats[rider.Name] = new HandcarSeat(rider.Name, Str(rider.Value, "grip"), Str(rider.Value, "pump"), turn);
        }
        if (seats.Count == 0)
            throw new FormatException("the rig has no seats");
        return new HandcarRig(anim, frames, distance, radius, turns, seats);
    }

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s : throw new FormatException($"{key} must be a non-empty string");

    private static double Num(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && double.IsFinite(v.GetDouble())
            ? v.GetDouble() : throw new FormatException($"{key} must be a number");
}
