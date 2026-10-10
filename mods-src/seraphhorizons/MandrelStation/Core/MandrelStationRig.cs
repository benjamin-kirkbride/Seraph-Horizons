using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.MandrelStation.Core;

/// <summary>
/// The mandrel station's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/mandrelstation-rig.json (written by MandrelStation/tools/make_shape.py).
/// Native, south-facing frame, controller cell at (0,0,0) (the stump, nearest the player), blocks.
/// The keys are the model's contract with gameplay (mandrelstation-contract, "What gameplay reads from
/// the rig"); unknown keys are ignored. A hand station: a rig naming a power cell or face is refused.
/// Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class MandrelStationRig
{
    /// <summary>The part that carries the hammer's clock (the contract's stable id).</summary>
    public const string MandrelPartId = "mandrel";

    public IReadOnlyList<RigCell> Cells { get; }
    /// <summary>The face the pipe sections leave by (beyond the mandrel's tip).</summary>
    public Side OutputSide { get; }
    /// <summary>Where the two sections lie at W = 1.</summary>
    public Float3 Output { get; }
    /// <summary>Where the hammer lands: the blow's sound and sparks.</summary>
    public Float3 Strike { get; }
    public WorkQuantity Work { get; }
    /// <summary>Blows per hollow of each class, [none, lead, copper], as the model is drawn.</summary>
    public IReadOnlyList<int> BlowsPerHollow { get; }
    public RigParts MovingParts { get; }

    public MandrelStationRig(IReadOnlyList<RigCell> cells, Side outputSide, Float3 output, Float3 strike,
                             WorkQuantity work, int blowsLead, int blowsCopper, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the mandrel station has no hollow cells");
        if (blowsLead < 1 || blowsCopper < 1)
            throw new FormatException("forge.blowsPerHollow must be at least 1 for both metals");
        if (work.Ends[1] != 1 || work.Ends[2] != 1)
            throw new FormatException("work.end must be {thin: 1, thick: 1}, the forging of one hollow");
        Cells = cells;
        OutputSide = outputSide;
        Output = output;
        Strike = strike;
        Work = work;
        BlowsPerHollow = [0, blowsLead, blowsCopper];
        MovingParts = movingParts ?? new RigParts([], work);
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>, so
    /// the station extends away from them with the stump (the controller) nearest and the mandrel
    /// pointing away: it runs along native south, which a facing turns to itself (the press brake's
    /// and the draw bench's rule).</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell, where the pipe sections
    /// are dropped.</summary>
    public Int3 OutputNeighbour()
    {
        var cell = new Int3((int)MathF.Floor(Output.X), (int)MathF.Floor(Output.Y), (int)MathF.Floor(Output.Z));
        var step = OutputSide.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        while (occupied.Contains(cell))
            cell += step;
        return cell;
    }

    /// <summary>Where the sections are dropped: <see cref="Output"/>
    /// moved out through the output face to <paramref name="beyond"/> blocks past it.</summary>
    public Float3 OutputDrop(float beyond = 0.15f)
    {
        var step = OutputSide.Normal();
        var face = OutputNeighbour();
        float Plane(int along, int n) => n > 0 ? along : along + 1;
        return step switch
        {
            { X: not 0 } => Output with { X = Plane(face.X, step.X) + step.X * beyond },
            { Z: not 0 } => Output with { Z = Plane(face.Z, step.Z) + step.Z * beyond },
            _ => Output,
        };
    }

    /// <summary>Parses mandrelstation-rig.json.</summary>
    public static MandrelStationRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        foreach (var key in new[] { "powerCell", "powerFace" })
            if (root.TryGetProperty(key, out _))
                throw new FormatException($"the mandrel station is a hand station: the rig has no {key}");
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var forge = Required(root, "forge", JsonValueKind.Object);
        var blows = Required(forge, "blowsPerHollow", JsonValueKind.Object);
        if (forge.TryGetProperty("sectionsPerHollow", out var per) && (per.ValueKind != JsonValueKind.Number || per.GetDouble() != Forging.SectionsPerHollow))
            throw new FormatException($"forge.sectionsPerHollow must be {Forging.SectionsPerHollow}");
        void Expect(string group, string name, string? expected)
        {
            if (forge.TryGetProperty(group, out var g) && Str(g, name) is { } got && got != expected)
                throw new FormatException($"forge.{group}.{name} is {got}, the station's is {expected}");
        }
        Expect("hollows", "thin", Forging.HollowFor(1));
        Expect("hollows", "thick", Forging.HollowFor(2));
        Expect("sections", "thin", Forging.SectionFor(1));
        Expect("sections", "thick", Forging.SectionFor(2));
        int Blows(string name)
        {
            var v = Required(blows, name, JsonValueKind.Number).GetDouble();
            return v == Math.Floor(v) && v is >= 1 and <= 1000 ? (int)v : throw new FormatException($"forge.blowsPerHollow.{name} must be a whole number of blows");
        }
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, MandrelRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new MandrelStationRig(
            Cells(root),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("strike"),
            work,
            Blows("thin"),
            Blows("thick"),
            parts);
    }
}
