using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.EidolonGantry.Core;

/// <summary>
/// The eidolon gantry's footprint, anchors and moving parts, from
/// assets/seraphhorizons/config/eidolongantry-rig.json (written by EidolonGantry/tools/make_shape.py).
/// Native frame, blocks, controller cell at (0,0,0): the foot of the front right (north-west) post;
/// the front is west (−x), open, the way the body faces. The keys are the model's contract with
/// gameplay (EidolonGantry/README.md, "The rig"); unknown keys are ignored. Throws
/// <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class GantryRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    /// <summary>Where the eidolon stands once it has woken and stepped off the spine.</summary>
    public Float3 Body { get; }
    /// <summary>Where the ring bears on the spine's peg.</summary>
    public Float3 Hang { get; }
    /// <summary>The hung chest's middle, where the body's parts are fitted.</summary>
    public Float3 Fit { get; }
    /// <summary>The open front's middle, on the ground: the eidolon walks out over it.</summary>
    public Float3 Exit { get; }
    public Side ExitSide { get; }
    /// <summary>The crank's cell, outside the box: hollow, with nothing to collide with.</summary>
    public Int3 CrankCell { get; }
    public Side CrankFace { get; }
    /// <summary>How far the body comes down at depth 1, blocks.</summary>
    public float Drop { get; }
    public RigParts MovingParts { get; }

    public GantryRig(IReadOnlyList<RigCell> cells, Float3 body, Float3 hang, Float3 fit, Float3 exit, Side exitSide,
                     Int3 crankCell, Side crankFace, float drop, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.First(c => c.Pos == Int3.Zero).Hollow)
            throw new FormatException("the controller's cell is hollow");
        if (cells.FirstOrDefault(c => c.Pos == crankCell) is not { Hollow: true })
            throw new FormatException($"crankCell {crankCell} is not a hollow cell of the footprint");
        if (!positions.Contains(CellOf(exit)))
            throw new FormatException($"exit {exit} is not in a cell of the footprint");
        if (!(drop >= 0) || !float.IsFinite(drop))
            throw new FormatException("winch.drop must be 0 or more");
        Cells = cells;
        Body = body;
        Hang = hang;
        Fit = fit;
        Exit = exit;
        ExitSide = exitSide;
        CrankCell = crankCell;
        CrankFace = crankFace;
        Drop = drop;
        MovingParts = movingParts ?? new RigParts([]);
    }

    /// <summary>The cell a native point lies in.</summary>
    public static Int3 CellOf(Float3 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    /// <summary>The cell placed where the player clicks: the open front's middle, on the ground
    /// (<see cref="Exit"/>'s cell), so the gantry stands centred before them.</summary>
    public Int3 PlaceCell => CellOf(Exit);

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>:
    /// the open front (native west) faces them. Native west goes to the side's opposite under
    /// <see cref="Footprint.ToWorld(Int3, Side)"/>'s turn, as the mills' <see cref="Footprint.PlacedFacing"/>
    /// sends it away.</summary>
    public static Side PlacedSide(Side look) => Footprint.PlacedFacing(Opposite(look));

    public static Side Opposite(Side side) => side switch
    {
        Side.North => Side.South,
        Side.South => Side.North,
        Side.East => Side.West,
        _ => Side.East,
    };

    /// <summary>Parses eidolongantry-rig.json.</summary>
    public static GantryRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, GantryRequires.KnownRequires, null)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        var winch = Required(root, "winch", JsonValueKind.Object);
        return new GantryRig(
            Cells(root),
            Anchor("body"),
            Anchor("hang"),
            Anchor("fit"),
            Anchor("exit"),
            SideOf(Required(root, "exitSide", JsonValueKind.String), "exitSide"),
            Int3Of(Required(root, "crankCell", JsonValueKind.Array), "crankCell"),
            SideOf(Required(root, "crankFace", JsonValueKind.String), "crankFace"),
            Required(winch, "drop", JsonValueKind.Number).GetSingle(),
            parts);
    }
}
