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

    /// <summary>A liquid touches one of its six faces: left, so the pit never opens onto it. It holds
    /// nothing up: what is under it touches no liquid, and mining it opens nothing onto one.</summary>
    LiquidBeside,

    /// <summary>A liquid touches one of its six faces and it falls (sand, gravel): left, and holds up
    /// what is under it, or it would fall into the pit and open it onto the liquid.</summary>
    LiquidBesideFalls,

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
/// material), and whether it falls with nothing under it (sand, gravel).</summary>
public readonly record struct MineBlock(bool Air, bool Liquid, bool LiquidBeside, bool HasBlockEntity, bool MayBreak,
    int RequiredTier, bool PickBreaks, bool Falls = false);

/// <summary>One strike: up to four cells, flat (2 × 2 on one layer) or upright (2 wide, 2 tall).</summary>
public sealed record MineGroup(IReadOnlyList<MarkPos> Cells, bool Upright)
{
    public double CentreX => Cells.Average(c => c.X + 0.5);
    public double CentreY => Cells.Average(c => c.Y + 0.5);
    public double CentreZ => Cells.Average(c => c.Z + 0.5);
}

/// <summary>The area's cells as the mine planner takes them (<see cref="EidolonMining.Targets"/>): those it
/// digs, those it keeps only as the floor under cells out of its reach (the sill and below), those it
/// leaves for what they are (its rules, skipped, or held up by those), and the block columns not loaded.</summary>
public sealed record MineTargets(IReadOnlySet<MarkPos> Dig, IReadOnlySet<MarkPos> Held,
    IReadOnlySet<MarkPos> Left, IReadOnlyList<(int X, int Z)> Unloaded);

/// <summary>What the miners of one shared job share (README "Eidolon", working together): the cells
/// every member leaves for the order's life for what they are (<see cref="Skip"/>: a land claim its owner
/// may not break in, a block that would not break), and the job's picture of its area (README "Eidolon",
/// mining, *Stations*), made once, by the first member to start: the live state (<see cref="Live"/>, kept
/// as the world changes, asked before every claim and strike), the plan its members take stations from
/// (<see cref="Plan"/>, made again in slices as the world changes and swapped in whole,
/// <see cref="PlanVersion"/> counting each), the stands found bad in the world (<see cref="BadStands"/>:
/// the game's pathfinder found no way to them), and how many blocks out of its reach its owner was last
/// told of (<see cref="ToldOutOfReach"/>, -1 never). A lone eidolon keeps its own.</summary>
public sealed class MineJobCells
{
    public HashSet<MarkPos> Skip { get; } = new();

    public MineState? Live { get; set; }

    public MinePlan? Plan { get; set; }

    /// <summary>Whether a plan is being made (the first, or one made again, on a worker thread).</summary>
    public bool Planning { get; set; }

    public int PlanVersion { get; set; }

    /// <summary>Settable: a plan made on a worker is given a copy, which becomes the job's set when the
    /// plan is swapped in (the live state built with it shares it).</summary>
    public HashSet<PathCell> BadStands { get; set; } = new();

    /// <summary>How many times a member found a stand bad over the job's life (each one the plan made again;
    /// <see cref="BadStands"/> forgets one tried again), for the log and the tests.</summary>
    public int BadStandsFound { get; set; }

    /// <summary>How many stands were found bad since the job's last strike (<see cref="BadSinceStrikeAt"/>, the
    /// <see cref="LastStrikeAt"/> they were counted from): each makes the plan again only up to a few
    /// (MineOrder.MostBadReplans). Main thread only.</summary>
    public int BadSinceStrike { get; set; }

    public double BadSinceStrikeAt { get; set; }

    /// <summary>Ways out by the window's side found to be none (<see cref="MineWalk.SideWays"/>: a stand joined to
    /// where its member stood only by them was found bad), each with the stands they alone led out: those are taken
    /// as no station's until a plan made with those ways out among its bad stands comes in (the live state, made
    /// before, still has them out). Main thread only.</summary>
    public List<(IReadOnlySet<PathCell> Side, IReadOnlySet<PathCell> Joined)> SideShut { get; } = new();

    /// <summary>Whether <paramref name="stand"/> is one of <see cref="SideShut"/>'s.</summary>
    public bool ShutOff(PathCell stand) => SideShut.Count > 0 && SideShut.Any(s => s.Joined.Contains(stand));

    /// <summary>The stands a member found no way to with another eidolon by them since the job's last strike,
    /// and how many times each, by any member (README "Eidolon", working together, *When none may work*):
    /// main thread only; one counted often enough is bad all the same.</summary>
    public Dictionary<PathCell, int> BlockedStands { get; } = new();

    public int ToldOutOfReach { get; set; } = -1;

    /// <summary>Every cell a plan of the job left as its stair: one made again late (the world changed, a
    /// stand found bad) takes what is left of the stair for blocks it cannot reach, but they are still the
    /// way out, and are told and reported as such.</summary>
    public HashSet<MarkPos> Stair { get; } = new();

    /// <summary>The plan's blocks out of reach that are no stair of the job's (<see cref="Stair"/>).</summary>
    public IReadOnlySet<MarkPos> OutOfReach
    {
        get
        {
            // made again only when the plan or the stair changes (every member asks on every look)
            if (_outOfReach is { } o && ReferenceEquals(o.Plan, Plan) && o.Stair == Stair.Count)
                return o.Set;
            var set = Plan is { } p ? p.OutOfReach.Where(c => !Stair.Contains(c)).ToHashSet() : new HashSet<MarkPos>();
            _outOfReach = (Plan, Stair.Count, set);
            return set;
        }
    }

    private (MinePlan? Plan, int Stair, IReadOnlySet<MarkPos> Set)? _outOfReach;

    /// <summary>The cells under eidolons parked in the area (none of the job's) when the plan was last made
    /// for being stuck: planned round, as cells left for now, until they move (README "Eidolon", working
    /// together, *Footing*).</summary>
    public HashSet<MarkPos> Parked { get; set; } = new();

    /// <summary>The cells under parked eidolons as last seen, and since when: the plan is made round them
    /// only once they have stayed so a while (one walking across the area stirs no plan).</summary>
    public HashSet<MarkPos> ParkedSeen { get; set; } = new();

    public double ParkedSince { get; set; }

    /// <summary>The member walking on the stair (README "Eidolon", working together, *The stair*), 0 for none,
    /// and until when (a holder unloaded or stuck lets it go then): one at a time, the others keeping off it.</summary>
    public long StairHolder { get; set; }

    public double StairUntil { get; set; }

    /// <summary>Whether the plan was made again because no member had a station it could work while
    /// cells were left (README "Eidolon", working together, *Dispatch*): once, until a block is broken.</summary>
    public bool StuckReplanned { get; set; }

    /// <summary>How many of its targets went with no strike of its own (the game's rock caving in where a strike
    /// left it hanging, granite breaking into its drops, or a player): what it mined and these are all that went.</summary>
    public int Gone { get; set; }

    /// <summary>How many blocks came to be ones it mines in the area with no doing of its own (grass grown on dug
    /// ground, sand or gravel fallen into a dug cell, a block placed): what it mined is the area's blocks gone,
    /// less <see cref="Gone"/>, and at most these more.</summary>
    public int Appeared { get; set; }

    /// <summary>The job's last strike (<see cref="LastStrikeAt"/>) when its owner was last told it looks stalled
    /// (README "Eidolon", working together, *When none may work*), null never: told again only after another.</summary>
    public double? StallToldStrike { get; set; }

    /// <summary>The job's route plan (README "Eidolon", working together, *Routes*), main thread only; null
    /// before the first, and while today's dispatch is used instead.</summary>
    public MineRoutes? Routes { get; set; }

    /// <summary>Counts every swap of <see cref="Routes"/> and every repair that changes a route.</summary>
    public int RoutesVersion { get; set; }

    /// <summary>Why a route solve is asked for (null: none).</summary>
    public string? RoutesWanted { get; set; }

    /// <summary>Whether the solve asked for may start before the least time between solves is up.</summary>
    public bool RoutesUrgent { get; set; }

    /// <summary>The job's choice between today's dispatch and the routes' variants, kept apart from
    /// <see cref="Routes"/> so that it outlasts routes let go (a job down to one member) and new station plans,
    /// for as long as the area and the members present are those it was made for.</summary>
    public MineRouteChoice RouteChoice { get; } = new();

    /// <summary>When a member last began a strike (game seconds; 0: none yet).</summary>
    public double LastStrikeAt { get; set; }

    /// <summary>The station plan whose routes stalled (no strike for a while by a crew all waiting on them): it
    /// is dispatched as today's for the rest of that plan; -1 for none.</summary>
    public int RoutesStalledPlan { get; set; } = -1;

    /// <summary>When routes for the station plan in use first came in (game seconds): a stall is timed from
    /// it, not from each solve after.</summary>
    public double RoutesPlanSince { get; set; }
}

/// <summary>A strike's timing (seconds from its start): the slam is started at <see cref="SlamAt"/>
/// at <see cref="SlamSpeed"/> and lands at <see cref="ImpactAt"/>, when the group breaks; the
/// strike ends at <see cref="Seconds"/>.</summary>
public readonly record struct MineStrike(double Seconds, double SlamSpeed, double SlamAt, double ImpactAt);

/// <summary>
/// The mine order's rules (README "Eidolon", mining), game-independent: the area's limits, what it
/// breaks, how long a strike takes, which cells it reaches from where it stands (never its own
/// footprint or under it), which group it strikes next, top down, and which cells it digs (<see cref="Targets"/>;
/// where it stands for them is the planner's, <see cref="MinePlanner"/>). Cells are blocks; it stands at a path node (<see cref="PathCell"/>, a block corner), its feet in
/// block <c>Y</c>.
/// </summary>
public static class EidolonMining
{
    /// <summary>The marked area's limits: blocks in all, and its longest horizontal side.</summary>
    public const int MaxAreaBlocks = 16384;
    public const int MaxAreaSide = 1024;

    /// <summary>The highest mining tier it breaks when the steel pickaxe's own
    /// (<c>ToolTier</c>, read at runtime: <see cref="PickTier"/>) cannot be read: the game's steel
    /// pickaxe's in 1.22.</summary>
    public const int SteelPickTier = 5;

    /// <summary>Its pace against a seraph with a steel pickaxe or shovel, whichever is quicker for the block.</summary>
    public const double PlayerShare = 0.75;

    /// <summary>The pickaxe whose mining speeds it strikes at, and whose tier it breaks up to.</summary>
    public const string PickCode = "game:pickaxe-steel";

    /// <summary>The shovel whose mining speeds it strikes at too, where they are the faster
    /// (<see cref="DigSpeed"/>: soil, sand, gravel, snow).</summary>
    public const string ShovelCode = "game:shovel-steel";

    /// <summary>How far past its box it reaches sideways, in blocks; and from how far below its feet'
    /// block to how far above: one layer under its feet to its head (3.75 tall).</summary>
    public const int Reach = 2;
    public const int ReachBelow = 1;
    public const int ReachAbove = 3;

    /// <summary>Its box's half width (1.7 wide), and its height.</summary>
    public const double HalfWidth = 0.85;
    public const double Height = 3.75;

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
            return block.Falls ? MineRule.LiquidBesideFalls : MineRule.LiquidBeside;
        if (block.RequiredTier > maxTier || !block.PickBreaks)
            return MineRule.TooHard;
        return block.MayBreak ? MineRule.Mine : MineRule.Claimed;
    }

    /// <summary>Whether the game lets a pickaxe break a block at all: one that needs no tier always
    /// (at the bare hand's pace if neither the pick nor the shovel has one for it); one that needs a tier
    /// only if the pick has a speed for its material. The shovel never adds one: it has no tier
    /// (<c>ToolTier</c> 0), so the game refuses it every block that needs one.</summary>
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

    /// <summary>How fast it digs a block, resistance a second, as a seraph would with the better tool
    /// for it: the faster of the steel pickaxe's (<paramref name="pickSpeed"/>) and the steel shovel's
    /// (<paramref name="shovelSpeed"/>) <see cref="PickSpeed"/> for its material, each null where that
    /// tool has none. So soil goes at the shovel's 7, not the bare hand's 1, and stone at the pick's 9.</summary>
    public static double DigSpeed(double? pickSpeed, double? shovelSpeed, double modifier = 1) =>
        Math.Max(PickSpeed(pickSpeed, modifier), PickSpeed(shovelSpeed, modifier));

    /// <summary>Seconds a seraph takes to break a block of <paramref name="resistance"/> at
    /// <paramref name="pickSpeed"/> (<see cref="DigSpeed"/>): the game takes speed × the tick's seconds off the block's
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

    // ---- the search ----

    /// <summary>The lowest layer it mines: the world's bottom layer is the mantle.</summary>
    public const int BottomY = 1;

    /// <summary>
    /// The cells of the area it digs (README "Eidolon", mining, *Stations*): every one whose rule is
    /// <see cref="MineRule.Mine"/>, not <paramref name="skip"/>ped, and not held up; held up: the column under a block it leaves (or one just over the area,
    /// <see cref="Holds"/>) down to the first air, but not under one left only for a liquid beside it; and
    /// under each of <paramref name="hold"/> (out of its reach for good, <see cref="MineReach"/>) likewise,
    /// so it never mines the floor under what it cannot reach. All layers at once; unloaded columns
    /// listed, not searched. The cells of <paramref name="hold"/> are in none of the sets.
    /// </summary>
    public static MineTargets Targets(MarkArea area, MineSite site, Func<MarkPos, bool> skip, IReadOnlySet<MarkPos>? hold = null)
    {
        MineTargets? result = null;
        foreach (var _ in Targets(area, site, skip, hold, t => result = t)) { }
        return result!;
    }

    /// <summary><see cref="Targets(MarkArea, MineSite, Func{MarkPos, bool}, IReadOnlySet{MarkPos}?)"/> a few
    /// block columns a step (the planner's slices), handing the result to <paramref name="done"/>.</summary>
    public static IEnumerable<bool> Targets(MarkArea area, MineSite site, Func<MarkPos, bool> skip, IReadOnlySet<MarkPos>? hold,
        Action<MineTargets> done)
    {
        int columns = 0;
        var dig = new HashSet<MarkPos>();
        var held = new HashSet<MarkPos>();
        var left = new HashSet<MarkPos>();
        var unloaded = new List<(int X, int Z)>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            if (site.Unloaded(x, z))
            {
                unloaded.Add((x, z));
                continue;
            }
            bool byLeft = Holds(site.Rule(new MarkPos(x, area.Max.Y + 1, z))), byHold = false;
            for (int y = area.Max.Y; y >= Math.Max(BottomY, area.Min.Y); y--)
            {
                var cell = new MarkPos(x, y, z);
                var what = site.Rule(cell);
                if (what == MineRule.Air)
                {
                    byLeft = byHold = false;
                    continue;
                }
                if (hold?.Contains(cell) == true)
                {
                    byHold = true;
                    continue;
                }
                if (byLeft || skip(cell) || what != MineRule.Mine)
                {
                    if (what != MineRule.LiquidBeside || byLeft || skip(cell))
                        byLeft = true;
                    left.Add(cell);
                    continue;
                }
                if (byHold)
                {
                    held.Add(cell);
                    continue;
                }
                dig.Add(cell);
            }
            MineWork.Add(area.SizeY + 1);
            if (++columns % 32 == 0)
                yield return false;
        }
        done(new MineTargets(dig, held, left, unloaded));
        yield return true;
    }

    /// <summary>Whether a block just over the area, which it never breaks, holds up the column under it:
    /// one it would leave were it in the area for what it is (a block entity, too hard, claimed, one that
    /// falls with a liquid beside it). Not a liquid, whose block under it is left already (a liquid beside
    /// it), nor a block beside one that stays put.</summary>
    public static bool Holds(MineRule over) => over is MineRule.BlockEntity or MineRule.TooHard or MineRule.Claimed or MineRule.LiquidBesideFalls;

    /// <summary>The block column of <paramref name="columns"/> nearest (<paramref name="x"/>,
    /// <paramref name="z"/>), or null for none.</summary>
    public static (int X, int Z)? NearestColumn(IEnumerable<(int X, int Z)> columns, double x, double z) =>
        columns.Select(c => ((int X, int Z)?)c).MinBy(c => Distance2(c!.Value.X + 0.5, c.Value.Z + 0.5, x, z));

    private static double Distance2(double ax, double az, double bx, double bz) => (ax - bx) * (ax - bx) + (az - bz) * (az - bz);
}
