using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>Where bark and sticks come out: a native-frame point, and the face they are pushed
/// out of.</summary>
public readonly record struct RosserChute(Float3 Pos, Side Side);

/// <summary>
/// The feed train's constants (rig.json's <c>feed</c>). The feed is geared and never slips, so the
/// feed input φ is defined by the trunk's travel: the trunk moves <see cref="BlocksPerRadian"/> blocks
/// per radian of φ, for both classes. <see cref="Gear"/> is the drawn two-speed change, dφ/dψ per
/// class ([0, thin, thick]); the gameplay's ratio is <see cref="RosserPace.FeedRatio"/>, and a test
/// holds the two together at the default settings.
/// </summary>
public sealed record RosserFeed(float BlocksPerRadian, IReadOnlyList<float> Gear);

/// <summary>
/// The rosser's footprint and anchors, from assets/seraphhorizons/config/rosser-rig.json (written
/// by Rosser/tools/make_shape.py). Native, south-facing frame, controller cell at (0,0,0), blocks.
/// The keys are documented in build/rosser/w2d-rig-keys.md and the rosser's README; unknown keys
/// are ignored. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class RosserRig
{
    public const string BreakerStation = "breaker";
    public const string RingStation = "ring";
    public const string DripStation = "drip";
    public const string TreadleStation = "treadle";

    public IReadOnlyList<RigCell> Cells { get; }
    public Int3 PowerCell { get; }
    public Side PowerFace { get; }
    public Int3 WaterCell { get; }
    public Side WaterFace { get; }
    /// <summary>The end a trunk goes in at (a rack must touch it).</summary>
    public Side InfeedSide { get; }
    /// <summary>The end a finished trunk leaves by (a rack or the in-line mill).</summary>
    public Side OutputSide { get; }
    public RosserChute Chute { get; }
    public TrunkPath Path { get; }
    public RosserFeed Feed { get; }
    /// <summary>The model's moving parts; empty when the file has none.</summary>
    public RigParts MovingParts { get; }

    public RosserRig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Int3 waterCell, Side waterFace,
                     Side infeedSide, Side outputSide, RosserChute chute, TrunkPath path, RosserFeed feed, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Pos == Int3.Zero && c.Hollow))
            throw new FormatException("the controller's cell cannot be hollow");
        CheckAnchor("powerCell", powerCell, powerFace, positions, cells);
        CheckAnchor("waterCell", waterCell, waterFace, positions, cells);
        if (waterCell == powerCell)
            throw new FormatException("waterCell is the power cell");
        if (path.Axis == Axis.Y)
            throw new FormatException("trunkPath.axis must be x or z");
        var forward = path.Axis == Axis.X ? Side.East : Side.South;
        if (outputSide != forward || infeedSide != Opposite(forward))
            throw new FormatException($"along a trunkPath on {path.Axis} the trunk goes in at {Opposite(forward).Code()} and out at {forward.Code()}");
        if (chute.Side != Opposite(powerFace))
            throw new FormatException($"chute.side {chute.Side.Code()} must be opposite powerFace {powerFace.Code()}");
        if (!path.Stations.TryGetValue(BreakerStation, out float breaker) || !path.Stations.TryGetValue(RingStation, out float ring))
            throw new FormatException("trunkPath.stations needs \"breaker\" and \"ring\"");
        if (!(path.Nose0 < breaker && breaker < ring && ring <= path.TailStop))
            throw new FormatException($"trunkPath needs nose0 < breaker < ring <= tailStop, got {path.Nose0}, {breaker}, {ring}, {path.TailStop}");
        if (!(feed.BlocksPerRadian > 0) || !float.IsFinite(feed.BlocksPerRadian))
            throw new FormatException("feed.blocksPerRadian must be above 0");
        if (feed.Gear.Count != 3 || !(feed.Gear[1] > 0) || !(feed.Gear[2] > 0) || !float.IsFinite(feed.Gear[1]) || !float.IsFinite(feed.Gear[2]))
            throw new FormatException("feed.gear thin and thick must be above 0");
        if (!(feed.Gear[2] < feed.Gear[1]))
            throw new FormatException("feed.gear.thick must be below feed.gear.thin: thick trunks feed slower");
        Cells = cells;
        PowerCell = powerCell;
        PowerFace = powerFace;
        WaterCell = waterCell;
        WaterFace = waterFace;
        InfeedSide = infeedSide;
        OutputSide = outputSide;
        Chute = chute;
        Path = path;
        Feed = feed;
        MovingParts = movingParts ?? new RigParts([], path);
    }

    private static void CheckAnchor(string name, Int3 cell, Side face, HashSet<Int3> positions, IReadOnlyList<RigCell> cells)
    {
        if (!positions.Contains(cell))
            throw new FormatException($"{name} {cell} is not one of the cells");
        if (cell == Int3.Zero)
            throw new FormatException($"{name} is the controller's cell; it must be a ghost cell");
        if (cells.Any(c => c.Pos == cell && c.Hollow))
            throw new FormatException($"{name} {cell} is hollow");
        if (positions.Contains(cell + face.Normal()))
            throw new FormatException($"{name} {cell}'s face {face.Code()} is inside the footprint");
    }

    public static Side Opposite(Side side) => side switch
    {
        Side.North => Side.South,
        Side.South => Side.North,
        Side.East => Side.West,
        _ => Side.East,
    };

    /// <summary>The limb breaker's position along the path.</summary>
    public float Breaker => Path.Stations[BreakerStation];

    /// <summary>The ring's position along the path: bark comes off as the trunk passes it.</summary>
    public float Ring => Path.Stations[RingStation];

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    public RigCell? CellAt(Int3 pos) => Cells.FirstOrDefault(c => c.Pos == pos);

    /// <summary>The ground-level cells just outside the infeed end: where a rack has to stand.
    /// Unlike the mill's, only beyond the end row.</summary>
    public IEnumerable<Int3> InfeedNeighbours() => Neighbours(InfeedSide);

    /// <summary>The ground-level cells just outside the outfeed end: a rack to push into, or the in-line mill.</summary>
    public IEnumerable<Int3> OutfeedNeighbours() => Neighbours(OutputSide);

    /// <summary>The ground cells just beyond the end on <paramref name="side"/>: past the end
    /// row only, in line with the trunk's path, never beside the beds where the station is wider.</summary>
    private IEnumerable<Int3> Neighbours(Side side)
    {
        var step = side.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        var ground = Cells.Where(c => c.Pos.Y == 0).ToList();
        int Along(Int3 p) => p.X * step.X + p.Z * step.Z;
        int end = ground.Max(c => Along(c.Pos));
        return ground.Where(c => Along(c.Pos) == end)
            .Select(c => c.Pos + step)
            .Where(p => !occupied.Contains(p))
            .Distinct();
    }

    /// <summary>Whether a ground cell outside the footprint is one of the outfeed neighbours (the
    /// in-line mill asks this of its rack cells).</summary>
    public bool IsOutfeedNeighbour(Int3 pos) => OutfeedNeighbours().Contains(pos);

    /// <summary>Parses rig.json.</summary>
    public static RosserRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var cells = Cells(root);
        var path = TrunkPath.Parse(Required(root, "trunkPath", JsonValueKind.Object));
        var chute = Required(root, "chute", JsonValueKind.Object);
        var feed = Required(root, "feed", JsonValueKind.Object);
        var gear = Required(feed, "gear", JsonValueKind.Object);
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, RosserRequires.KnownRequires, path)
                : throw new FormatException("\"parts\" must be an array");
        return new RosserRig(
            cells,
            Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
            SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
            Int3Of(Required(root, "waterCell", JsonValueKind.Array), "waterCell"),
            SideOf(Required(root, "waterFace", JsonValueKind.String), "waterFace"),
            SideOf(Required(root, "infeedSide", JsonValueKind.String), "infeedSide"),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            new RosserChute(Float3Of(Required(chute, "pos", JsonValueKind.Array), "chute.pos"),
                            SideOf(Required(chute, "side", JsonValueKind.String), "chute.side")),
            path,
            new RosserFeed(Required(feed, "blocksPerRadian", JsonValueKind.Number).GetSingle(),
                           [0, Required(gear, "thin", JsonValueKind.Number).GetSingle(), Required(gear, "thick", JsonValueKind.Number).GetSingle()]),
            parts);
    }
}
