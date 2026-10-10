namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// What a measured deposit is made of (#692), as <see cref="OreTally.Makeup"/> counts it in the
/// deposit's nine columns: metal units per ore, blocks per grade (<c>-</c> for ungraded ores) and
/// blocks per host rock. Maps name the ore actually there (<see cref="MainOres"/>), the grades
/// (<see cref="Mix"/>) and the rock (<see cref="HostRock"/>), never the metal. Saved with the
/// deposit's record (<see cref="DepositRecord.Makeup"/>).
/// </summary>
public sealed record DepositMakeup
{
    /// <summary>An ore is named when it holds at least this share of the deposit's metal.</summary>
    public const double MinOreShare = 0.1;

    /// <summary>At most this many ores are named, the richest in metal first.</summary>
    public const int MaxOres = 3;

    public Dictionary<string, double> Ores { get; init; } = new();
    public Dictionary<string, long> Grades { get; init; } = new();
    public Dictionary<string, long> Rocks { get; init; } = new();

    /// <summary>The ores holding at least <see cref="MinOreShare"/> of the metal (the richest one
    /// always), richest first, at most <see cref="MaxOres"/>; ties by name.</summary>
    public IReadOnlyList<string> MainOres()
    {
        double total = Ores.Values.Sum();
        var sorted = Ores.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        return sorted.Where((kv, i) => i == 0 || kv.Value >= MinOreShare * total).Take(MaxOres).Select(kv => kv.Key).ToList();
    }

    /// <summary>The rock most of the ore sits in; ties by name; null with no ore.</summary>
    public string? HostRock() => Rocks.Where(kv => kv.Value > 0 && kv.Key != "-")
        .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => kv.Key).FirstOrDefault();

    /// <summary>How the graded ore's blocks split by grade; null with no graded ore.</summary>
    public GradeMix? Mix() => GradeMix.Of(Grades);
}

/// <summary>
/// A deposit's grades in words (#692): <c>only</c> one grade (90 % of its graded blocks or more),
/// <c>mostly</c> one (60 % or more), else <c>mixed</c>, the two commonest, poorest first. Written
/// <c>only:rich</c>, <c>mostly:poor</c>, <c>mixed:poor,medium</c> (<see cref="Code"/>), as maps and
/// offers carry it.
/// </summary>
public sealed record GradeMix(string Kind, IReadOnlyList<string> Grades)
{
    public const string Only = "only";
    public const string Mostly = "mostly";
    public const string Mixed = "mixed";
    public const double OnlyShare = 0.9;
    public const double MostlyShare = 0.6;

    public string Code => $"{Kind}:{string.Join(',', Grades)}";

    public static GradeMix? Of(IReadOnlyDictionary<string, long> blocksByGrade)
    {
        var graded = blocksByGrade.Where(kv => kv.Value > 0 && OreTally.Grades.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => Rank(kv.Key))
            .ToList();
        if (graded.Count == 0) return null;
        double total = graded.Sum(kv => kv.Value);
        double top = graded[0].Value / total;
        if (top >= OnlyShare) return new GradeMix(Only, [graded[0].Key]);
        if (top >= MostlyShare || graded.Count == 1) return new GradeMix(Mostly, [graded[0].Key]);
        return new GradeMix(Mixed, graded.Take(2).Select(kv => kv.Key).OrderBy(Rank).ToList());
    }

    public static GradeMix? Parse(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        int colon = code.IndexOf(':');
        if (colon <= 0) return null;
        string kind = code[..colon];
        var grades = code[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool ok = kind switch
        {
            Only or Mostly => grades.Length == 1,
            Mixed => grades.Length == 2,
            _ => false,
        };
        return ok && grades.All(g => OreTally.Grades.Contains(g)) ? new GradeMix(kind, grades) : null;
    }

    private static int Rank(string grade) => OreTally.Grades.ToList().IndexOf(grade);

    public bool Equals(GradeMix? other) => other is not null && Code == other.Code;

    public override int GetHashCode() => Code.GetHashCode();
}

/// <summary>Ore and metal lists as maps and offers carry them (#692): comma-separated codes, and
/// the lang key that names an ore in running text (lower case: "galena and cerussite").</summary>
public static class OreNames
{
    public static string LangKey(string ore) => "orename-" + ore;

    public static string Csv(IEnumerable<string> codes) => string.Join(',', codes);

    public static IReadOnlyList<string> Split(string? csv) =>
        string.IsNullOrEmpty(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
