using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Which ore worldgen changes a world was started with, kept in its savegame. They change how new
/// chunks generate, so a world gets them only if they were on when it was created: switching one
/// on later never applies it to an existing world (its new chunks would not match its old ones),
/// while switching one off in the config does take effect. The cell size is fixed at creation.
/// </summary>
public sealed record OreWorldRecord(
    bool OreCells,
    int CellSize,
    IReadOnlyDictionary<string, int> CellSizeByMetal,
    bool NoSurfaceCopper,
    bool SmallerDeposits,
    bool RarerDistricts)
{
    // Inside the record, OreCells names the switch, not the class.
    private const int DefaultCellSize = global::SeraphHorizons.Mod.Ore.Core.OreCells.DefaultCellSize;

    public static readonly OreWorldRecord AllOff =
        new(false, DefaultCellSize, new Dictionary<string, int>(), false, false, false);

    /// <summary>
    /// The record to keep: the saved one if there is one, else the config's switches for a new
    /// world, else (a world created before these existed) everything off.
    /// </summary>
    public static OreWorldRecord ForWorld(OreWorldRecord? saved, bool isNewWorld, OreWorldRecord config) =>
        saved ?? (isNewWorld ? config : AllOff);

    /// <summary>What is in force this run: the world's record, less what the config switches off.</summary>
    public OreWorldRecord Effective(OreWorldRecord config) => this with
    {
        OreCells = OreCells && config.OreCells,
        NoSurfaceCopper = NoSurfaceCopper && config.NoSurfaceCopper,
        SmallerDeposits = SmallerDeposits && config.SmallerDeposits,
        RarerDistricts = RarerDistricts && config.RarerDistricts,
    };

    // "key=value" lines, so a later version can add keys.
    public string Serialize()
    {
        var sb = new StringBuilder("v1\n");
        sb.Append(CultureInfo.InvariantCulture, $"oreCells={OreCells}\ncellSize={CellSize}\n");
        foreach (var (metal, size) in CellSizeByMetal.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture, $"cellSize.{metal}={size}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"noSurfaceCopper={NoSurfaceCopper}\nsmallerDeposits={SmallerDeposits}\nrarerDistricts={RarerDistricts}\n");
        return sb.ToString();
    }

    public static OreWorldRecord Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0] != "v1")
            throw new FormatException("unknown ore world record format");
        var values = new Dictionary<string, string>();
        foreach (var line in lines.Skip(1))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) values[line[..eq]] = line[(eq + 1)..];
        }
        bool B(string key) => values.TryGetValue(key, out var v) && bool.Parse(v);
        var bySize = values.Where(kv => kv.Key.StartsWith("cellSize.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key["cellSize.".Length..], kv => int.Parse(kv.Value, CultureInfo.InvariantCulture));
        return new OreWorldRecord(
            B("oreCells"),
            values.TryGetValue("cellSize", out var cs) ? int.Parse(cs, CultureInfo.InvariantCulture) : DefaultCellSize,
            bySize,
            B("noSurfaceCopper"), B("smallerDeposits"), B("rarerDistricts"));
    }

    public bool Equals(OreWorldRecord? other) => other is not null && Serialize() == other.Serialize();

    public override int GetHashCode() => Serialize().GetHashCode();
}
