using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>A material a build stage takes: an item code (<c>*</c> for any run of characters) and how many.</summary>
public sealed record StageMaterial(string Code, int Quantity)
{
    public bool Matches(string? code) => code != null && PotRecipes.CodeMatches(Code, code);
}

/// <summary>A build stage of the furnace (the rig's <c>build.stages</c>, in order): the
/// <see cref="Requires"/> it fits once all its <see cref="Materials"/> are in; <see cref="OneType"/>
/// when every brick must be the first's type; <see cref="AlsoParts"/> rig parts that come with it at
/// no cost of their own; <see cref="Also"/> requires it makes possible (the damper's places).</summary>
public sealed record BuildStage(string Requires, IReadOnlyList<StageMaterial> Materials, bool OneType,
                                IReadOnlyList<string> AlsoParts, IReadOnlyList<string> Also);

/// <summary>A fired brick type: the block its bricks make, and that block's texture, a base and the
/// overlays drawn over it.</summary>
public sealed record BrickType(string Id, string Block, string Base, IReadOnlyList<string> Overlays);

/// <summary>The rig's <c>bricks</c>: the texture code the brickwork wears, the brick item's pattern
/// (<c>{type}</c> for the type), the default type, and the types.</summary>
public sealed record BrickTypes(string TextureCode, string Item, string Default, IReadOnlyList<BrickType> Types)
{
    public BrickType? Get(string? id) => Types.FirstOrDefault(t => t.Id == id);

    /// <summary>The brick type <paramref name="code"/> is (<see cref="Item"/> with its <c>{type}</c>),
    /// or null when it is no brick of the pattern. A type not in the list is returned too: the caller
    /// refuses it (<see cref="Get"/> is null).</summary>
    public string? TypeOf(string? code)
    {
        int at = Item.IndexOf("{type}", StringComparison.Ordinal);
        if (code == null || at < 0)
            return null;
        string prefix = Item[..at], suffix = Item[(at + "{type}".Length)..];
        if (!code.Contains(':'))
            code = "game:" + code;
        return code.Length > prefix.Length + suffix.Length && code.StartsWith(prefix, StringComparison.Ordinal)
               && code.EndsWith(suffix, StringComparison.Ordinal)
            ? code[prefix.Length..^suffix.Length]
            : null;
    }

    /// <summary>The brick item of type <paramref name="id"/>.</summary>
    public string ItemOf(string id) => Item.Replace("{type}", id);

    /// <summary>Parses a rig's <c>bricks</c> (the crucible furnace's, the stall roaster's); throws
    /// <see cref="FormatException"/> naming what is wrong.</summary>
    public static BrickTypes Parse(JsonElement bricksJson)
    {
        List<string> Strings(JsonElement obj, string key) =>
            obj.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(e => e.GetString() ?? throw new FormatException($"{key} must be strings")).ToList()
                : [];
        var types = new List<BrickType>();
        foreach (var t in Required(bricksJson, "types", JsonValueKind.Object).EnumerateObject())
            types.Add(new BrickType(t.Name,
                Str(t.Value, "block") ?? throw new FormatException($"bricks.types.{t.Name} has no block"),
                Str(t.Value, "base") ?? throw new FormatException($"bricks.types.{t.Name} has no base"),
                Strings(t.Value, "overlays")));
        var bricks = new BrickTypes(Str(bricksJson, "code") ?? throw new FormatException("bricks.code is missing"),
            Str(bricksJson, "item") ?? throw new FormatException("bricks.item is missing"),
            Str(bricksJson, "default") ?? throw new FormatException("bricks.default is missing"), types);
        if (bricks.Get(bricks.Default) == null)
            throw new FormatException($"bricks.default \"{bricks.Default}\" is not one of the types");
        if (!bricks.Item.Contains("{type}", StringComparison.Ordinal))
            throw new FormatException("bricks.item must hold {type}");
        return bricks;
    }
}

/// <summary>The rig's <c>furnace</c> figures.</summary>
public sealed record FurnaceFigures(int Pots, int PotUnits, int CokeLayers, int CokePerLayer, int MinChimney, int RowMax, float LidOpenDegrees);

/// <summary>The crucible furnace's <c>requires</c> vocabulary (the rig's parts).</summary>
public static class FurnaceRequires
{
    public const string Brickwork = "brickwork";
    public const string Lining = "lining";
    public const string Lid = "lid";
    public const string OutletCap = "outletcap";

    public static string Pot(int i) => "pot" + (i + 1);
    public static string Charge(int i) => "charge" + (i + 1);
    public static string Melt(int i) => "melt" + (i + 1);
    public static string Coke(int layer) => "coke" + layer;
    public static string Fire(int layer) => "fire" + layer;

    public static string DamperPart(Damper d) => d switch
    {
        Damper.Half => "damperhalf",
        Damper.Shut => "dampershut",
        _ => "damperopen",
    };

    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string>
    {
        Brickwork, Lining, Lid, OutletCap,
        "pot1", "pot2", "charge1", "charge2", "melt1", "melt2",
        "coke1", "coke2", "coke3", "fire1", "fire2", "fire3",
        "damperopen", "damperhalf", "dampershut",
    };
}

/// <summary>
/// The crucible furnace's footprint, anchors, build and moving parts, from
/// assets/seraphhorizons/config/cruciblefurnace-rig.json (written by CrucibleFurnace/tools/make_shape.py).
/// Native frame (the front, with the ash mouth, faces north), controller cell at (0,0,0) (the front
/// middle cell, the ash mouth's), blocks. The keys are the model's contract with gameplay
/// (CrucibleFurnace/README.md, "Rig schema"); unknown keys are ignored. Throws
/// <see cref="FormatException"/> naming what is wrong. The shared machine base reads it as an
/// <see cref="IMachineRig"/> (no anchors: it takes no power, water or chute); its build there is
/// <c>config/builds/cruciblefurnace.json</c>, these <see cref="Stages"/> as the model's README has them.
/// </summary>
public sealed class CrucibleFurnaceRig : IMachineRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    /// <summary>The cell over the hole's middle, worked from its top: both pots stand under it.</summary>
    public Int3 HoleCell { get; }
    /// <summary>The middle of each pot's mouth, pot 1 west.</summary>
    public IReadOnlyList<Float3> PotPoints { get; }
    /// <summary>The stack's top cell; the chimney (or the cap) on its top.</summary>
    public Int3 OutletCell { get; }
    /// <summary>The middle of the ash mouth, in the controller's front face.</summary>
    public Float3 Ash { get; }
    public Side AshSide { get; }
    /// <summary>The middle of the hole's mouth at the floor: flames while the lid is open.</summary>
    public Float3 Mouth { get; }
    public IReadOnlyList<BuildStage> Stages { get; }
    public StageMaterial OutletCap { get; }
    /// <summary>The frame's grid recipe, as the rig settles it.</summary>
    public IReadOnlyList<StageMaterial> FrameIngredients { get; }
    public BrickTypes Bricks { get; }
    public FurnaceFigures Figures { get; }
    public RigParts MovingParts { get; }
    /// <summary>The outlet, the cap, the brick types and the rig's row figures, as every fired machine's
    /// (the furnace's row takes its figures from the settings).</summary>
    public FurnaceFlue Flue { get; }

    RigParts IMachineRig.Parts => MovingParts;
    public IReadOnlyDictionary<string, MachineAnchor> Anchors { get; } = new Dictionary<string, MachineAnchor>();
    public MachineAnchor? Anchor(string name) => null;

    public CrucibleFurnaceRig(IReadOnlyList<RigCell> cells, Int3 holeCell, IReadOnlyList<Float3> potPoints, Int3 outletCell,
                              Float3 ash, Side ashSide, Float3 mouth, IReadOnlyList<BuildStage> stages, StageMaterial outletCap,
                              IReadOnlyList<StageMaterial> frameIngredients, BrickTypes bricks, FurnaceFigures figures, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.First(c => c.Pos == Int3.Zero).Hollow)
            throw new FormatException("the controller's cell is hollow");
        foreach (var (name, cell) in new[] { ("holeCell", holeCell), ("outletCell", outletCell) })
            if (cells.FirstOrDefault(c => c.Pos == cell) is not { Hollow: false })
                throw new FormatException($"{name} {cell} is not a solid cell of the footprint");
        if (positions.Contains(outletCell + new Int3(0, 1, 0)))
            throw new FormatException("the cell over the outlet is the furnace's: the chimney stands there");
        if (potPoints.Count != figures.Pots || figures.Pots < 1)
            throw new FormatException($"furnace.pots is {figures.Pots} but there are {potPoints.Count} pot anchors");
        if (stages.Count == 0 || stages.Any(s => s.Materials.Count == 0 || s.Materials.Any(m => m.Quantity <= 0)))
            throw new FormatException("build.stages must each have materials with quantities above 0");
        if (stages.Select(s => s.Requires).Distinct().Count() != stages.Count)
            throw new FormatException("two build stages fit the same requires");
        if (stages.Any(s => s.OneType && s.Materials.Count != 1))
            throw new FormatException("a oneType stage takes one material, the brick");
        if (bricks.Get(bricks.Default) == null)
            throw new FormatException($"bricks.default \"{bricks.Default}\" is not one of the types");
        if (!bricks.Item.Contains("{type}", StringComparison.Ordinal))
            throw new FormatException("bricks.item must hold {type}");
        if (figures.CokeLayers < 1 || figures.CokePerLayer < 1)
            throw new FormatException("furnace.cokeLayers and cokePerLayer must be at least 1");
        if (figures.RowMax < 1)
            throw new FormatException("furnace.rowMax must be at least 1");
        Cells = cells;
        HoleCell = holeCell;
        PotPoints = potPoints;
        OutletCell = outletCell;
        Ash = ash;
        AshSide = ashSide;
        Mouth = mouth;
        Stages = stages;
        OutletCap = outletCap;
        FrameIngredients = frameIngredients;
        Bricks = bricks;
        Figures = figures;
        MovingParts = movingParts ?? new RigParts([]);
        Flue = new FurnaceFlue(outletCell, outletCap.Code, outletCap.Quantity, bricks, figures.MinChimney, figures.RowMax, Width);
    }

    /// <summary>The furnace's width along x, the row's axis: a neighbour's controller stands this far along.</summary>
    public int Width => Cells.Max(c => c.Pos.X) - Cells.Min(c => c.Pos.X) + 1;

    /// <summary>The hollow cells: over the hole's rows, where the lid swings.</summary>
    public IEnumerable<RigCell> HollowCells => Cells.Where(c => c.Hollow);

    /// <summary>The first cell of the chimney, over the outlet.</summary>
    public Int3 ChimneyBase => Flue.ChimneyBase;

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>: the
    /// furnace extends away from them, its front (the ash mouth, native north) towards them, as the
    /// cylinder borer's does.</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The pot nearer <paramref name="native"/> (a point in the native frame, blocks), by its
    /// distance across the plan to each pot's mouth.</summary>
    public int NearerPot(Float3 native)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < PotPoints.Count; i++)
        {
            double dx = native.X - PotPoints[i].X, dz = native.Z - PotPoints[i].Z;
            double d = dx * dx + dz * dz;
            if (d < bestD - 1e-9)
            {
                best = i;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary><paramref name="box"/> (native blocks) cut into the cells of <paramref name="cells"/>
    /// it crosses, each part cell-local.</summary>
    public static IReadOnlyDictionary<Int3, Box> ByCell(Box box, IEnumerable<Int3> cells)
    {
        var result = new Dictionary<Int3, Box>();
        foreach (var cell in cells)
        {
            var part = new Box(Math.Max(box.X1, cell.X) - cell.X, Math.Max(box.Y1, cell.Y) - cell.Y, Math.Max(box.Z1, cell.Z) - cell.Z,
                               Math.Min(box.X2, cell.X + 1) - cell.X, Math.Min(box.Y2, cell.Y + 1) - cell.Y, Math.Min(box.Z2, cell.Z + 1) - cell.Z);
            if (part.X2 - part.X1 > 1e-4 && part.Y2 - part.Y1 > 1e-4 && part.Z2 - part.Z1 > 1e-4)
                result[cell] = part;
        }
        return result;
    }

    /// <summary>Parses cruciblefurnace-rig.json.</summary>
    public static CrucibleFurnaceRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        if (Str(root, "holeFace") != "up" || Str(root, "outletFace") != "up")
            throw new FormatException("holeFace and outletFace must be \"up\"");

        var build = Required(root, "build", JsonValueKind.Object);
        StageMaterial Material(JsonElement m, string where) =>
            new(Str(m, "code") ?? throw new FormatException($"{where}: a material has no code"),
                m.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetInt32() : throw new FormatException($"{where}: a material has no quantity"));
        List<string> Strings(JsonElement obj, string key) =>
            obj.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(e => e.GetString() ?? throw new FormatException($"{key} must be strings")).ToList()
                : [];
        var stages = Required(build, "stages", JsonValueKind.Array).EnumerateArray().Select(s =>
        {
            string requires = Str(s, "requires") ?? throw new FormatException("a build stage has no requires");
            if (!FurnaceRequires.KnownRequires.Contains(requires))
                throw new FormatException($"build stage {requires} is not a requires the gameplay knows");
            return new BuildStage(requires,
                Required(s, "materials", JsonValueKind.Array).EnumerateArray().Select(m => Material(m, "build stage " + requires)).ToList(),
                Bool(s, "oneType"), Strings(s, "alsoParts"), Strings(s, "also"));
        }).ToList();
        var frame = Required(build, "frame", JsonValueKind.Object);
        var frameIngredients = Required(frame, "ingredients", JsonValueKind.Array).EnumerateArray().Select(m => Material(m, "build.frame")).ToList();
        var cap = Material(Required(build, "outletCap", JsonValueKind.Object), "build.outletCap");

        var bricks = BrickTypes.Parse(Required(root, "bricks", JsonValueKind.Object));

        var f = Required(root, "furnace", JsonValueKind.Object);
        int Int(string key) => Required(f, key, JsonValueKind.Number).GetInt32();
        if (Str(f, "rowAxis") != "x")
            throw new FormatException("furnace.rowAxis must be x");
        var figures = new FurnaceFigures(Int("pots"), Int("potUnits"), Int("cokeLayers"), Int("cokePerLayer"), Int("minChimney"), Int("rowMax"),
            Required(f, "lidOpenDegrees", JsonValueKind.Number).GetSingle());

        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, FurnaceRequires.KnownRequires, null)
                : throw new FormatException("\"parts\" must be an array");
        var pots = Enumerable.Range(1, figures.Pots).Select(i => Anchor("pot" + i)).ToList();
        return new CrucibleFurnaceRig(
            Cells(root),
            Int3Of(Required(root, "holeCell", JsonValueKind.Array), "holeCell"),
            pots,
            Int3Of(Required(root, "outletCell", JsonValueKind.Array), "outletCell"),
            Anchor("ash"),
            SideOf(Required(root, "ashSide", JsonValueKind.String), "ashSide"),
            Anchor("mouth"),
            stages, cap, frameIngredients, bricks, figures, parts);
    }
}
