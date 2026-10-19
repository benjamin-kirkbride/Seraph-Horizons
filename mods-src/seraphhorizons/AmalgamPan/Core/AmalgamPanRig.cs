using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.AmalgamPan.Core;

/// <summary>
/// The amalgam pan's cell, power face, anchors and moving parts, from
/// assets/seraphhorizons/config/amalgampan-rig.json (written by AmalgamPan/tools/make_shape.py; the
/// model's contract, AmalgamPan/README.md "Rig schema"). One cell, the controller's; the axle comes
/// up into its down face; no work and no item transfer anchor. For the shared machine base
/// (<see cref="IMachineRig"/>) its one anchor is <c>power</c>, the controller's down face. Throws
/// <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class AmalgamPanRig : IMachineRig
{
    public RigCell Cell { get; }
    /// <summary>The face the axle comes in at, the same in every facing: always <c>down</c>.</summary>
    public const string PowerFace = "down";
    /// <summary>The middle of the pan's floor: its sounds and particles.</summary>
    public Float3 Pan { get; }
    public Side OperatorSide { get; }
    public RigParts MovingParts { get; }

    /// <summary>Its one cell, the controller's.</summary>
    public IReadOnlyList<RigCell> Cells => [Cell];

    RigParts IMachineRig.Parts => MovingParts;

    /// <summary>The axle's: the controller's down face.</summary>
    public IReadOnlyDictionary<string, MachineAnchor> Anchors { get; } =
        new Dictionary<string, MachineAnchor> { [MachineRigs.Power] = new(MachineRigs.Power, Int3.Zero, CellFace.Down) };

    public MachineAnchor? Anchor(string name) => Anchors.TryGetValue(name, out var a) ? a : null;

    public AmalgamPanRig(RigCell cell, Float3 pan, Side operatorSide, RigParts parts)
    {
        if (cell.Pos != Int3.Zero)
            throw new FormatException("the pan's one cell must be the controller's, [0,0,0]");
        Cell = cell;
        Pan = pan;
        OperatorSide = operatorSide;
        MovingParts = parts;
    }

    public static AmalgamPanRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("work", out _))
            throw new FormatException("the pan has no work quantity");
        var cells = Cells(root);
        if (cells.Count != 1)
            throw new FormatException($"the pan is one cell, the rig lists {cells.Count}");
        var powerCell = Required(root, "powerCell", JsonValueKind.Array).EnumerateArray().Select(v => v.GetInt32()).ToArray();
        if (powerCell is not [0, 0, 0])
            throw new FormatException("powerCell must be [0,0,0], the pan's own cell");
        if (Required(root, "powerFace", JsonValueKind.String).GetString() is var face && face != PowerFace)
            throw new FormatException($"the pan takes its axle from below: powerFace is {face}, not {PowerFace}");
        var parts = Required(root, "parts", JsonValueKind.Array);
        return new AmalgamPanRig(
            cells[0],
            Float3Of(Required(Required(root, "pan", JsonValueKind.Object), "pos", JsonValueKind.Array), "pan.pos"),
            SideOf(Required(root, "operatorSide", JsonValueKind.String), "operatorSide"),
            RigParts.Parse(parts, AmalgamPanRequires.Known, null));
    }
}

/// <summary>
/// The renderer's muller angle θ. Turned from an axle below, θ is the axle's angle as the network
/// gives it (the quern's top stone takes it as it is); by hand it advances at the hand's pace while
/// someone holds right-click on a pan with work in it, and stays where it was let go.
/// </summary>
public sealed class AmalgamPanClock
{
    public double Theta { get; private set; }

    /// <summary>Steps the clock: <paramref name="axleAngle"/> the network's angle while an axle turns
    /// the pan (null when none does), else <paramref name="handHeld"/> at
    /// <paramref name="handTurnsPerSecond"/>.</summary>
    public void Advance(float dt, double? axleAngle, bool handHeld, double handTurnsPerSecond)
    {
        if (axleAngle is { } angle && double.IsFinite(angle))
            Theta = angle;
        else if (handHeld && dt > 0)
            Theta += handTurnsPerSecond * 2 * Math.PI * dt;
        if (Math.Abs(Theta) > 1e6)
            Theta %= 2 * Math.PI;
    }

    public RigInput Input() => new(Theta);
}
