using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Rocker.Core;

/// <summary>A point of the rig, riding <see cref="Part"/> (a part id) or fixed when it is null.</summary>
public readonly record struct RockerPoint(Float3 Pos, string? Part);

/// <summary>
/// The rocker's cell, anchors, rock and moving parts, from assets/seraphhorizons/config/rocker-rig.json
/// (written by Rocker/tools/make_shape.py). Native, south-facing frame, one cell (0,0,0), blocks. The
/// keys are the model's contract with gameplay (Rocker/README.md, "Rig schema"); unknown keys are
/// ignored. A hand station watered by bucket, with no automated input or output: a rig naming a power
/// cell or face, a water cell or face, or an infeed or output side is refused, and it has no anchors for
/// the shared machine base (<see cref="IMachineRig"/>). Throws <see cref="FormatException"/> naming what is
/// wrong.
/// </summary>
public sealed class RockerRig : IMachineRig
{
    public const string CradlePart = "cradle";

    /// <summary>Keys a hand station watered by bucket never has.</summary>
    public static readonly IReadOnlyList<string> Refused =
        ["powerCell", "powerFace", "waterCell", "waterFace", "infeedSide", "outputSide", "work", "trunkPath"];

    public RigCell Cell { get; }
    /// <summary>Where the player stands, at the handle.</summary>
    public Side OperatorSide { get; }
    /// <summary>The riddle plate's top: the charge and the bucket go in here.</summary>
    public RockerPoint Pour { get; }
    /// <summary>The lip at the open foot: water and tailings leave here.</summary>
    public RockerPoint Outflow { get; }
    /// <summary>Behind the upper riffle: the concentrate is taken out here.</summary>
    public RockerPoint Concentrate { get; }
    /// <summary>On the ground in the cell beyond the foot: where the tailings fall.</summary>
    public Float3 Tailings { get; }
    /// <summary>The rock's amplitude, degrees each way.</summary>
    public float RockDegrees { get; }
    public RigParts MovingParts { get; }

    /// <summary>Its one cell, the controller's.</summary>
    public IReadOnlyList<RigCell> Cells => [Cell];

    RigParts IMachineRig.Parts => MovingParts;

    /// <summary>None: a hand station takes no axle, chute or pipe.</summary>
    public IReadOnlyDictionary<string, MachineAnchor> Anchors { get; } = new Dictionary<string, MachineAnchor>();

    public MachineAnchor? Anchor(string name) => null;

    public RockerRig(RigCell cell, Side operatorSide, RockerPoint pour, RockerPoint outflow, RockerPoint concentrate,
                     Float3 tailings, float rockDegrees, RigParts movingParts)
    {
        if (cell.Pos != Int3.Zero)
            throw new FormatException("the rocker's one cell must be [0,0,0]");
        if (cell.Hollow)
            throw new FormatException("the rocker's cell is not hollow");
        foreach (var (name, p) in new[] { ("pour", pour), ("outflow", outflow), ("concentrate", concentrate) })
            if (p.Part != null && movingParts.IndexOf(p.Part) < 0)
                throw new FormatException($"{name}.part \"{p.Part}\" is not a part of the rig");
        if (movingParts.IndexOf(CradlePart) < 0)
            throw new FormatException("the rig has no \"cradle\" part");
        if (!(rockDegrees > 0 && rockDegrees < 45))
            throw new FormatException("rock.degrees must be between 0 and 45");
        Cell = cell;
        OperatorSide = operatorSide;
        Pour = pour;
        Outflow = outflow;
        Concentrate = concentrate;
        Tailings = tailings;
        RockDegrees = rockDegrees;
        MovingParts = movingParts;
    }

    /// <summary>The cell the tailings fall into, relative to the rocker (native frame).</summary>
    public Int3 TailingsCell =>
        new((int)MathF.Floor(Tailings.X), (int)MathF.Floor(Tailings.Y), (int)MathF.Floor(Tailings.Z));

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>: in
    /// the native frame the operator stands north looking south, so the block's side is the way they
    /// look (as for the press brake and the amalgam pan).</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>A point posed by the rig at θ: a riding point by its part's matrix, a fixed one as is.</summary>
    public Float3 Posed(RockerPoint point, double theta)
    {
        if (point.Part == null)
            return point.Pos;
        int i = MovingParts.IndexOf(point.Part);
        return Mat4.Apply(MovingParts.Matrices(new RigInput(theta))[i], point.Pos);
    }

    public static RockerRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        foreach (var key in Refused)
            if (root.TryGetProperty(key, out _))
                throw new FormatException($"the rocker is a hand station with no automated input or output: the rig has no {key}");
        var cells = Cells(root);
        if (cells.Count != 1)
            throw new FormatException("the rocker has exactly one cell");
        RockerPoint Point(string key)
        {
            var o = Required(root, key, JsonValueKind.Object);
            return new RockerPoint(Float3Of(Required(o, "pos", JsonValueKind.Array), key + ".pos"), Str(o, "part"));
        }
        var parts = RigParts.Parse(Required(root, "parts", JsonValueKind.Array), RockerRequires.Known, null);
        var rock = Required(root, "rock", JsonValueKind.Object);
        return new RockerRig(
            cells[0],
            SideOf(Required(root, "operatorSide", JsonValueKind.String), "operatorSide"),
            Point("pour"),
            Point("outflow"),
            Point("concentrate"),
            Point("tailings").Pos,
            Required(rock, "degrees", JsonValueKind.Number).GetSingle(),
            parts);
    }
}

/// <summary>The rig's <c>requires</c> values: the rocker's three states (a hand station has no build stages).</summary>
public static class RockerRequires
{
    public const string Water = "water";
    public const string Charge = "charge";
    public const string Concentrate = "concentrate";

    public static readonly IReadOnlySet<string> Known = new HashSet<string> { Water, Charge, Concentrate };
}
