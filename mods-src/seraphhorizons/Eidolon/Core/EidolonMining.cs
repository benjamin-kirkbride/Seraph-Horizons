namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What one block in a mine order's area is to it (README "Eidolon", mining).</summary>
public enum MineRule
{
    /// <summary>Nothing to do.</summary>
    Air,

    /// <summary>It breaks it.</summary>
    Mine,

    /// <summary>A liquid (water, lava): left.</summary>
    Liquid,

    /// <summary>A liquid touches one of its six faces: left, so the pit never opens onto it.</summary>
    LiquidBeside,

    /// <summary>It has a block entity (a chest, a machine, chiseled stone, farmland): left, unless its
    /// code is one of the ground blocks it mines through (<see cref="EidolonMining.LeavesBlockEntity"/>:
    /// Desire Paths' worn soil).</summary>
    BlockEntity,

    /// <summary>In a land claim its owner may not break in: left.</summary>
    Claimed,

    /// <summary>Above the steel pickaxe's tier (<see cref="EidolonMining.PickTier"/>), unbreakable, or
    /// nothing a pickaxe breaks: left.</summary>
    TooHard,
}

/// <summary>What the mine order reads of one block: whether it is air or a liquid, whether a liquid
/// touches it, has a block entity it leaves (<see cref="EidolonMining.LeavesBlockEntity"/>: any but
/// the ground blocks it mines through), its owner may break there, the tier it needs and whether a pickaxe
/// breaks it at all (the game refuses a block that needs a tier when the pick has no speed for its
/// material).</summary>
public readonly record struct MineBlock(bool Air, bool Liquid, bool LiquidBeside, bool HasBlockEntity, bool MayBreak,
    int RequiredTier, bool PickBreaks);

/// <summary>One strike: up to four cells, flat (2 × 2 on one layer) or upright (2 wide, 2 tall).</summary>
public sealed record MineGroup(IReadOnlyList<MarkPos> Cells, bool Upright)
{
    public double CentreX => Cells.Average(c => c.X + 0.5);
    public double CentreY => Cells.Average(c => c.Y + 0.5);
    public double CentreZ => Cells.Average(c => c.Z + 0.5);
}

/// <summary>What a search of the area found (<see cref="EidolonMining.Scan"/>): the layer it works (the
/// highest with a block left it may break; null for none), that layer's cells left and the next one's
/// down, and the block columns of the area not loaded, which read as air and so are never done.</summary>
public sealed record MineScan(int? Layer, IReadOnlySet<MarkPos> Left, IReadOnlyList<(int X, int Z)> Unloaded)
{
    /// <summary>Whether the area is done: no block left it may break, and none of it unloaded.</summary>
    public bool Done => Layer == null && Unloaded.Count == 0;
}

/// <summary>What the miners of one shared job share (README "Eidolon", working together): the cells
/// every member leaves for the order's life for what they are (a land claim its owner may not break in,
/// a block that would not break), the cells some member gave up for want of a way to them, for the
/// report, and the area's sectors and who works each. A cell one member cannot reach another may, so
/// each keeps its own unreachable cells for its search; only the report reads them all here.</summary>
public sealed class MineJobCells
{
    public HashSet<MarkPos> Skip { get; } = new();
    public HashSet<MarkPos> Unreachable { get; } = new();
    public MineSectors Sectors { get; } = new();
}

/// <summary>A strike's timing (seconds from its start): the slam is started at <see cref="SlamAt"/>
/// at <see cref="SlamSpeed"/> and lands at <see cref="ImpactAt"/>, when the group breaks; the
/// strike ends at <see cref="Seconds"/>.</summary>
public readonly record struct MineStrike(double Seconds, double SlamSpeed, double SlamAt, double ImpactAt);

/// <summary>
/// The mine order's rules (README "Eidolon", mining), game-independent: the area's limits, what it
/// breaks, how long a strike takes, which cells it reaches from where it stands (never its own
/// footprint or under it), which group it strikes next, top down, and where it stands to reach one.
/// Cells are blocks; it stands at a path node (<see cref="PathCell"/>, a block corner), its feet in
/// block <c>Y</c>.
/// </summary>
public static class EidolonMining
{
    /// <summary>The marked area's limits: blocks in all, and its longest horizontal side.</summary>
    public const int MaxAreaBlocks = 16384;
    public const int MaxAreaSide = 1024;

    /// <summary>The highest mining tier it breaks, bare-handed, when the steel pickaxe's own
    /// (<c>ToolTier</c>, read at runtime: <see cref="PickTier"/>) cannot be read: the game's steel
    /// pickaxe's in 1.22.</summary>
    public const int SteelPickTier = 5;

    /// <summary>Its pace against a seraph with a steel pickaxe.</summary>
    public const double PlayerShare = 0.75;

    /// <summary>The pickaxe whose mining speeds it strikes at.</summary>
    public const string PickCode = "game:pickaxe-steel";

    /// <summary>How far past its box it reaches sideways, in blocks; and from how far below its feet'
    /// block to how far above: one layer under its feet to its head (3.75 tall).</summary>
    public const int Reach = 2;
    public const int ReachBelow = 1;
    public const int ReachAbove = 3;

    /// <summary>Its box's half width (1.7 wide).</summary>
    public const double HalfWidth = 0.85;

    /// <summary>Vanilla's <c>stand-slam</c> (<see cref="EidolonStrikes.Slam"/>): 45 frames, both fists
    /// on the ground at frame 35.</summary>
    public static double SlamSeconds => EidolonStrikes.Slam.Seconds;
    public static double SlamHitSeconds => EidolonStrikes.Slam.HitAt;

    /// <summary>How far the slam is slowed or quickened to fill a strike; past these it plays at the
    /// limit, standing still first for a slow strike, and a strike quicker than the fastest slam takes
    /// the slam's time.</summary>
    public const double MinSlamSpeed = 0.5;
    public const double MaxSlamSpeed = 3;

    // ---- the area ----

    /// <summary>Whether a marked area is within the limits: at most <paramref name="maxSide"/> a side
    /// across and <paramref name="maxBlocks"/> blocks in all.</summary>
    public static bool AreaFits(MarkArea area, int maxSide = MaxAreaSide, int maxBlocks = MaxAreaBlocks) =>
        area.LongestSide <= maxSide && area.Volume <= maxBlocks;

    // ---- what it breaks ----

    /// <summary>The highest mining tier it breaks: the steel pickaxe's tier (<paramref name="toolTier"/>,
    /// as read from <see cref="PickCode"/>), or <see cref="SteelPickTier"/> without one.</summary>
    public static int PickTier(int? toolTier) => toolTier is > 0 and { } tier ? tier : SteelPickTier;

    /// <summary>What a block is to it, breaking up to <paramref name="maxTier"/> (<see cref="PickTier"/>).</summary>
    public static MineRule Rule(in MineBlock block, int maxTier = SteelPickTier)
    {
        if (block.Air)
            return MineRule.Air;
        if (block.Liquid)
            return MineRule.Liquid;
        if (block.HasBlockEntity)
            return MineRule.BlockEntity;
        if (block.LiquidBeside)
            return MineRule.LiquidBeside;
        if (block.RequiredTier > maxTier || !block.PickBreaks)
            return MineRule.TooHard;
        return block.MayBreak ? MineRule.Mine : MineRule.Claimed;
    }

    /// <summary>Whether the game lets a pickaxe break a block at all: one that needs no tier always
    /// (at the bare hand's pace if the pick has none for it); one that needs a tier only if the pick
    /// has a speed for its material.</summary>
    public static bool PickBreaks(int requiredTier, bool pickHasSpeed) => requiredTier <= 0 || pickHasSpeed;

    /// <summary>The ground blocks with a block entity it mines through by default
    /// (<see cref="EidolonConfig.MineThroughBlockEntities"/>): Desire Paths' worn soil, whole and as a
    /// slab (with Terrain Slabs). The pinned mods' other blocks of soil, stone or gravel with a block
    /// entity are all a player's (doors, ovens, aqueducts, canals, a nitre bed, a manure stack).</summary>
    public static readonly string[] GroundBlockEntities = ["desirepaths:soilpath-*", "desirepaths:soilpathslab-*"];

    /// <summary>Whether a block with a block entity, <paramref name="code"/> (<c>domain:path</c>), is
    /// one it leaves: every one but those <paramref name="mineThrough"/> matches (codes, <c>*</c> any
    /// run of characters). A block's material says nothing here: one whose type gives none is stone to
    /// the game, chiseled blocks, farmland, a berry bush and a tapestry among them.</summary>
    public static bool LeavesBlockEntity(string code, IEnumerable<string> mineThrough) =>
        !mineThrough.Any(pattern => CodeMatches(pattern, code));

    /// <summary>Whether <paramref name="code"/> matches <paramref name="pattern"/>, <c>*</c> any run of
    /// characters, ignoring case; a pattern with no domain is the game's.</summary>
    public static bool CodeMatches(string pattern, string code)
    {
        if (!pattern.Contains(':'))
            pattern = "game:" + pattern;
        if (!code.Contains(':'))
            code = "game:" + code;
        var parts = pattern.Split('*');
        int at = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (i == 0)
            {
                if (!code.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                    return false;
                at = part.Length;
                continue;
            }
            if (i == parts.Length - 1)
                return code.Length - at >= part.Length && code.EndsWith(part, StringComparison.OrdinalIgnoreCase);
            int found = code.IndexOf(part, at, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return false;
            at = found + part.Length;
        }
        return at == code.Length;
    }

    // ---- how long ----

    /// <summary>
    /// How fast a seraph's pick breaks a block, resistance a second, as the game reckons it
    /// (<c>CollectibleObject.GetMiningSpeed</c>): the pick's mining speed for the block's material
    /// times the game's tool mining speed modifier (<c>GlobalConstants.ToolMiningSpeedModifier</c>, 1),
    /// or 1 (the bare hand's) for a material it has no speed for. The seraph's <c>miningSpeedMul</c>
    /// stat is 1 without traits.
    /// </summary>
    public static double PickSpeed(double? materialSpeed, double modifier = 1) =>
        materialSpeed is { } speed && speed > 0 ? speed * modifier : 1;

    /// <summary>Seconds a seraph takes to break a block of <paramref name="resistance"/> at
    /// <paramref name="pickSpeed"/>: the game takes speed × the tick's seconds off the block's
    /// resistance each tick until none is left.</summary>
    public static double PlayerSeconds(double resistance, double pickSpeed) => Math.Max(0, resistance) / pickSpeed;

    /// <summary>A strike's length: the slowest of its group's blocks as a seraph breaks it, at
    /// <see cref="PlayerShare"/> of their pace.</summary>
    public static double StrikeSeconds(IEnumerable<double> playerSeconds) =>
        playerSeconds.DefaultIfEmpty(0).Max() / PlayerShare;

    /// <summary>A strike's timing: the slam stretched or squeezed to fill it, so its blows land at
    /// the same steady pace; limited to <see cref="MinSlamSpeed"/> (it stands first) and
    /// <see cref="MaxSlamSpeed"/> (the strike takes the slam's time).</summary>
    public static MineStrike Timing(double strikeSeconds)
    {
        double speed = strikeSeconds <= 0 ? MaxSlamSpeed : Math.Clamp(SlamSeconds / strikeSeconds, MinSlamSpeed, MaxSlamSpeed);
        double slam = SlamSeconds / speed;
        double seconds = Math.Max(strikeSeconds, slam);
        double slamAt = seconds - slam;
        return new MineStrike(seconds, speed, slamAt, slamAt + SlamHitSeconds / speed);
    }

    // ---- reach ----

    /// <summary>The block columns its box stands over, centred at (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public static (int MinX, int MaxX, int MinZ, int MaxZ) Footprint(double x, double z)
    {
        const double eps = 1e-6;
        return ((int)Math.Floor(x - HalfWidth), (int)Math.Floor(x + HalfWidth - eps),
            (int)Math.Floor(z - HalfWidth), (int)Math.Floor(z + HalfWidth - eps));
    }

    /// <summary>Whether a cell is in a column its box stands over (its footprint, or under it).</summary>
    public static bool UnderFoot(double x, double z, MarkPos cell)
    {
        var f = Footprint(x, z);
        return cell.X >= f.MinX && cell.X <= f.MaxX && cell.Z >= f.MinZ && cell.Z <= f.MaxZ;
    }

    /// <summary>Whether breaking a cell would undermine an eidolon standing centred at (<paramref name="x"/>,
    /// <paramref name="z"/>), its feet in block <paramref name="feet"/>: the cell is in a column its box
    /// stands over, at or below its feet. A miner never breaks such a cell under any eidolon, its own job's
    /// or not, working or not (README "Eidolon", working together); under itself, it never breaks one at
    /// any height (<see cref="InReach"/>).</summary>
    public static bool Undermines(double x, int feet, double z, MarkPos cell) => cell.Y <= feet && UnderFoot(x, z, cell);

    /// <summary>Whether breaking a cell would undermine any of <paramref name="eidolons"/> (each where it
    /// stands and its feet' block: <see cref="Undermines(double, int, double, MarkPos)"/>).</summary>
    public static bool Undermines(IEnumerable<(double X, int Feet, double Z)> eidolons, MarkPos cell) =>
        eidolons.Any(e => Undermines(e.X, e.Feet, e.Z, cell));

    /// <summary>Whether it reaches a cell standing centred at (<paramref name="x"/>, <paramref name="z"/>),
    /// its feet in block <paramref name="feet"/>: within <see cref="Reach"/> of its box sideways, from
    /// <see cref="ReachBelow"/> under its feet to <see cref="ReachAbove"/> over, never under its box.</summary>
    public static bool InReach(double x, int feet, double z, MarkPos cell)
    {
        if (cell.Y < feet - ReachBelow || cell.Y > feet + ReachAbove)
            return false;
        var f = Footprint(x, z);
        if (cell.X < f.MinX - Reach || cell.X > f.MaxX + Reach || cell.Z < f.MinZ - Reach || cell.Z > f.MaxZ + Reach)
            return false;
        return !UnderFoot(x, z, cell);
    }

    // ---- what next ----

    /// <summary>The lower corner of the 2 × 2 square a column is in: squares are laid from the area's
    /// lower corner, so the strikes tile it.</summary>
    public static (int X, int Z) Square(MarkArea area, int x, int z) =>
        (area.Min.X + ((x - area.Min.X) & ~1), area.Min.Z + ((z - area.Min.Z) & ~1));

    /// <summary>A square's columns that are in the area.</summary>
    public static IEnumerable<(int X, int Z)> SquareColumns(MarkArea area, (int X, int Z) square)
    {
        for (int x = square.X; x <= Math.Min(square.X + 1, area.Max.X); x++)
        for (int z = square.Z; z <= Math.Min(square.Z + 1, area.Max.Z); z++)
            yield return (x, z);
    }

    /// <summary>
    /// The group it strikes next from where it stands, working <paramref name="layer"/> (the highest
    /// with a block left to mine), or null when none of that layer is in reach. Of the squares with a
    /// cell of the layer left in reach, the nearest; of its shapes, the one breaking most, flat on
    /// ties: flat, the square's cells on the layer; upright, one of its four rows (two cells side by
    /// side) on the layer and the one under it, which takes a wall where both are left. Only cells
    /// left (<paramref name="left"/>: in the area, to mine, not given up) and in reach.
    /// </summary>
    public static MineGroup? Next(MarkArea area, int layer, Func<MarkPos, bool> left, double x, int feet, double z)
    {
        var f = Footprint(x, z);
        var squares = new HashSet<(int X, int Z)>();
        for (int cx = Math.Max(area.Min.X, f.MinX - Reach); cx <= Math.Min(area.Max.X, f.MaxX + Reach); cx++)
        for (int cz = Math.Max(area.Min.Z, f.MinZ - Reach); cz <= Math.Min(area.Max.Z, f.MaxZ + Reach); cz++)
        {
            var cell = new MarkPos(cx, layer, cz);
            if (left(cell) && InReach(x, feet, z, cell))
                squares.Add(Square(area, cx, cz));
        }
        foreach (var square in squares.OrderBy(s => Distance2(s.X + 1, s.Z + 1, x, z)).ThenBy(s => s.X).ThenBy(s => s.Z))
        {
            bool Takes(MarkPos c) => c.Y >= area.Min.Y && left(c) && InReach(x, feet, z, c);
            var flat = SquareColumns(area, square).Select(c => new MarkPos(c.X, layer, c.Z)).Where(Takes).ToList();
            List<MarkPos>? upright = null;
            foreach (var row in Rows(area, square))
            {
                var cells = row.SelectMany(c => new[] { new MarkPos(c.X, layer, c.Z), new MarkPos(c.X, layer - 1, c.Z) })
                    .Where(Takes).ToList();
                if (cells.Any(c => c.Y == layer) && cells.Count > (upright?.Count ?? 0))
                    upright = cells;
            }
            if (upright != null && upright.Count > flat.Count)
                return new MineGroup(upright, true);
            if (flat.Count > 0)
                return new MineGroup(flat, false);
        }
        return null;
    }

    // A square's four rows: its two along x and its two along z, each its columns in the area.
    private static IEnumerable<IReadOnlyList<(int X, int Z)>> Rows(MarkArea area, (int X, int Z) square)
    {
        var columns = SquareColumns(area, square).ToList();
        for (int i = 0; i < 2; i++)
        {
            yield return columns.Where(c => c.Z == square.Z + i).ToList();
            yield return columns.Where(c => c.X == square.X + i).ToList();
        }
    }

    /// <summary>The squares of the area with a cell of <paramref name="layer"/> among
    /// <paramref name="cells"/>, nearest (<paramref name="x"/>, <paramref name="z"/>) first, each with
    /// those cells.</summary>
    public static IEnumerable<((int X, int Z) Square, IReadOnlyList<MarkPos> Cells)> Squares(MarkArea area, IEnumerable<MarkPos> cells, int layer, double x, double z) =>
        cells.Where(c => c.Y == layer).GroupBy(c => Square(area, c.X, c.Z)).ToList()
            .OrderBy(g => Distance2(g.Key.X + 1, g.Key.Z + 1, x, z)).ThenBy(g => g.Key.X).ThenBy(g => g.Key.Z)
            .Select(g => (g.Key, (IReadOnlyList<MarkPos>)g.ToList()));

    /// <summary>The square of the area nearest (<paramref name="x"/>, <paramref name="z"/>) with a cell of
    /// <paramref name="layer"/> among <paramref name="cells"/>, and those cells; null for none.</summary>
    public static ((int X, int Z) Square, IReadOnlyList<MarkPos> Cells)? Nearest(MarkArea area, IEnumerable<MarkPos> cells, int layer, double x, double z) =>
        Squares(area, cells, layer, x, z).Select(s => (((int X, int Z) Square, IReadOnlyList<MarkPos> Cells)?)s).FirstOrDefault();

    // ---- the search ----

    /// <summary>The lowest layer it mines: the world's bottom layer is the mantle.</summary>
    public const int BottomY = 1;

    /// <summary>
    /// Searches the area (<see cref="MineScan"/>): from the top down, the highest layer with a cell
    /// <paramref name="rule"/> says it mines (<see cref="MineRule.Mine"/>) and not <paramref name="skip"/>ped,
    /// and that layer's such cells and the next one's down (for upright strikes). Only in the block
    /// columns <paramref name="loaded"/>: a column not loaded reads as air, so it is not searched but
    /// listed, unfinished until it loads. A block it leaves in place (any rule but mine and air, or
    /// <paramref name="skip"/>ped: one its owner may not break, one that would not break) holds up what
    /// is under it: the blocks below it in its column, down to the first air, are left too, so nothing
    /// it leaves is left floating, nor walls the pit off with blocks hanging in it; so does one it
    /// leaves just over the area (<see cref="Holds"/>: a chest, a sign, farmland on the surface). A block
    /// <paramref name="pass"/>ed (given up for want of a way to it) is only passed over: what is under it
    /// is mined.
    /// </summary>
    public static MineScan Scan(MarkArea area, Func<int, int, bool> loaded, Func<MarkPos, MineRule> rule, Func<MarkPos, bool> skip,
        Func<MarkPos, bool>? pass = null)
    {
        var columns = new List<(int X, int Z)>();
        var unloaded = new List<(int X, int Z)>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            (loaded(x, z) ? columns : unloaded).Add((x, z));
        var left = new HashSet<MarkPos>();
        var held = new HashSet<(int X, int Z)>();
        foreach (var (x, z) in columns)
            if (Holds(rule(new MarkPos(x, area.Max.Y + 1, z))))
                held.Add((x, z));
        int? layer = null;
        for (int y = area.Max.Y; y >= Math.Max(BottomY, area.Min.Y) && (layer == null || y >= layer - 1); y--)
        foreach (var (x, z) in columns)
        {
            var cell = new MarkPos(x, y, z);
            var what = rule(cell);
            if (what == MineRule.Air)
            {
                held.Remove((x, z));
                continue;
            }
            if (held.Contains((x, z)) || skip(cell) || what != MineRule.Mine)
            {
                held.Add((x, z));
                continue;
            }
            if (pass?.Invoke(cell) == true)
                continue;
            layer ??= y;
            left.Add(cell);
        }
        return new MineScan(layer, left, unloaded);
    }

    /// <summary>Whether a block just over the area, which it never breaks, holds up the column under it:
    /// one it would leave were it in the area for what it is (a block entity, too hard, claimed). Not a
    /// liquid, whose block under it is left already (a liquid beside it), nor a block beside one.</summary>
    public static bool Holds(MineRule over) => over is MineRule.BlockEntity or MineRule.TooHard or MineRule.Claimed;

    /// <summary>The block column of <paramref name="columns"/> nearest (<paramref name="x"/>,
    /// <paramref name="z"/>), or null for none.</summary>
    public static (int X, int Z)? NearestColumn(IEnumerable<(int X, int Z)> columns, double x, double z) =>
        columns.Select(c => ((int X, int Z)?)c).MinBy(c => Distance2(c!.Value.X + 0.5, c.Value.Z + 0.5, x, z));

    /// <summary>
    /// Where it may stand to reach every one of <paramref name="cells"/> (one square's cells on the
    /// layer it works): path nodes whose box is free and on the ground, none of the cells under it, its
    /// feet from a block over the highest cell to <see cref="ReachAbove"/> under the lowest. Those not
    /// standing on a cell left to mine (<paramref name="left"/>) first, so it does not stand in its own
    /// way; then nearest (<paramref name="fromX"/>, <paramref name="fromZ"/>), then nearest its feet' height.
    /// </summary>
    public static IReadOnlyList<PathCell> Stands(IWideSpace space, IReadOnlyList<MarkPos> cells, Func<MarkPos, bool> left,
        double fromX, int fromFeet, double fromZ)
    {
        if (cells.Count == 0)
            return [];
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minZ = cells.Min(c => c.Z), maxZ = cells.Max(c => c.Z);
        int minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        var stands = new List<(PathCell Node, bool OnLeft, double From, int Rise)>();
        for (int x = maxX - Reach; x <= minX + Reach + 1; x++)
        for (int z = maxZ - Reach; z <= minZ + Reach + 1; z++)
        for (int feet = maxY - ReachAbove; feet <= minY + ReachBelow; feet++)
        {
            if (!cells.All(c => InReach(x, feet, z, c)))
                continue;
            if (!space.Free(x, feet, z) || space.Free(x, feet - 1, z))
                continue;
            bool onLeft = false;
            for (int bx = x - 1; bx <= x && !onLeft; bx++)
            for (int bz = z - 1; bz <= z && !onLeft; bz++)
                onLeft = left(new MarkPos(bx, feet - 1, bz));
            stands.Add((new PathCell(x, feet, z), onLeft, Distance2(x, z, fromX, fromZ), Math.Abs(feet - fromFeet)));
        }
        return stands.OrderBy(s => s.OnLeft).ThenBy(s => s.From).ThenBy(s => s.Rise).ThenBy(s => s.Node.X).ThenBy(s => s.Node.Z)
            .Select(s => s.Node).ToList();
    }

    private static double Distance2(double ax, double az, double bx, double bz) => (ax - bx) * (ax - bx) + (az - bz) * (az - bz);
}
