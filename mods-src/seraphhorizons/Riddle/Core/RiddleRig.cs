using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Riddle.Core;

/// <summary>
/// The riddle's cell, anchors, contents, work and moving parts, from
/// assets/seraphhorizons/config/riddle-rig.json (written by Riddle/tools/make_shape.py). Native frame, the
/// operator to the north, one cell, the controller's (0,0,0), blocks. The keys are the model's contract
/// with gameplay (Riddle/README.md, "Rig schema"); unknown keys are ignored. A hand station with no
/// automated input or output: a rig naming a power cell or face, or an infeed or output side, face, cell
/// or point, is refused, and it has no anchors for the shared machine base (<see cref="IMachineRig"/>).
/// Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class RiddleRig : IMachineRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    /// <summary>Where the operator stands.</summary>
    public Side OperatorSide { get; }
    /// <summary>On the fines in the box: where they are taken out.</summary>
    public Float3 Fines { get; }
    /// <summary>The middle of the charge on the riddle: the riddling sound and dust, and the oversize.</summary>
    public Float3 Charge { get; }
    public WorkQuantity Work { get; }
    /// <summary>A charge's crushed ore, as the model draws it (<c>riddling.lumpsPerCharge</c>).</summary>
    public int LumpsPerCharge { get; }
    /// <summary>Shakes a charge, as the model is drawn (<c>riddling.shakesPerCharge.thin</c>).</summary>
    public float ShakesPerCharge { get; }
    public RigParts MovingParts { get; }

    RigParts IMachineRig.Parts => MovingParts;

    /// <summary>None: a hand station takes no axle, chute or pipe.</summary>
    public IReadOnlyDictionary<string, MachineAnchor> Anchors { get; } = new Dictionary<string, MachineAnchor>();

    public MachineAnchor? Anchor(string name) => null;

    public RiddleRig(IReadOnlyList<RigCell> cells, Side operatorSide, Float3 fines, Float3 charge, WorkQuantity work,
                     int lumpsPerCharge, float shakesPerCharge, RigParts? movingParts = null)
    {
        if (cells.Count != 1 || cells[0].Pos != Int3.Zero)
            throw new FormatException("the riddle is one cell, the controller's, [0,0,0]");
        if (lumpsPerCharge < 1)
            throw new FormatException("riddling.lumpsPerCharge must be 1 or more");
        if (!(shakesPerCharge > 0) || !float.IsFinite(shakesPerCharge))
            throw new FormatException("riddling.shakesPerCharge.thin must be above 0");
        if (work.Ends.Count < 2 || work.Ends[1] != 1)
            throw new FormatException("work.end must be {thin: 1}, one charge's riddling");
        Cells = cells;
        OperatorSide = operatorSide;
        Fines = fines;
        Charge = charge;
        Work = work;
        LumpsPerCharge = lumpsPerCharge;
        ShakesPerCharge = shakesPerCharge;
        MovingParts = movingParts ?? new RigParts([], work);
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>: the
    /// operator's side (native north) toward the player, as the other hand stations are placed.</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The keys a hand station never has: power, and any point of automated input or output.</summary>
    private static readonly string[] Refused =
        ["powerCell", "powerFace", "infeedSide", "infeedFace", "infeedCell", "infeed", "outputSide", "outputFace", "outputCell", "output"];

    /// <summary>Parses riddle-rig.json.</summary>
    public static RiddleRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        foreach (var key in Refused)
            if (root.TryGetProperty(key, out _))
                throw new FormatException($"the riddle is a hand station with no automated input or output: the rig has no {key}");
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var riddling = Required(root, "riddling", JsonValueKind.Object);
        int lumps = Required(riddling, "lumpsPerCharge", JsonValueKind.Number).GetInt32();
        float shakes = Required(Required(riddling, "shakesPerCharge", JsonValueKind.Object), "thin", JsonValueKind.Number).GetSingle();
        var contents = Required(root, "contents", JsonValueKind.Object);
        foreach (var key in new[] { RiddleRequires.Charge, RiddleRequires.Fines })
            if (Str(contents, key) != key)
                throw new FormatException($"contents.{key} must be \"{key}\"");
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, RiddleRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new RiddleRig(
            Cells(root),
            SideOf(Required(root, "operatorSide", JsonValueKind.String), "operatorSide"),
            Anchor("fines"),
            Anchor("charge"),
            work,
            lumps,
            shakes,
            parts);
    }
}
