using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.Rocker.Core;

/// <summary>What the rocker takes: crushed or ground ore (by ore and grade), or placer gravel (by rock).</summary>
public enum RockerFeedKind { Crushed, Ground, Placer }

/// <summary>One item code the rocker takes, read from its code.</summary>
/// <param name="Ore">The ore (crushed and ground ore), or the gravel's rock (placer).</param>
/// <param name="Grade">The ore's grade; null for placer gravel.</param>
public sealed record RockerFeed(string Code, RockerFeedKind Kind, string Ore, string? Grade)
{
    /// <summary>Wilderlands Panning's rich gravel, the placer fields' gravel: <c>game:richgravel-{rock}</c>.</summary>
    public const string PlacerPrefix = "game:richgravel-";

    /// <summary>The feed an item code is, or null when the rocker does not take it.</summary>
    public static RockerFeed? Parse(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        if (code.StartsWith(PlacerPrefix, StringComparison.Ordinal))
        {
            string rock = code[PlacerPrefix.Length..];
            return rock.Length > 0 && !rock.Contains('-') ? new RockerFeed(code, RockerFeedKind.Placer, rock, null) : null;
        }
        foreach (var (prefix, kind) in new[] { ("crushedore-", RockerFeedKind.Crushed), ("groundore-", RockerFeedKind.Ground) })
        {
            string full = OreProducts.Domain + ":" + prefix;
            if (!code.StartsWith(full, StringComparison.Ordinal))
                continue;
            var rest = code[full.Length..];
            int dash = rest.LastIndexOf('-');
            if (dash <= 0)
                return null;
            string ore = rest[..dash], grade = rest[(dash + 1)..];
            return OreProducts.IsGrade(grade) && OreProducts.Ores.Contains(ore) ? new RockerFeed(code, kind, ore, grade) : null;
        }
        return null;
    }
}

/// <summary>One output of washing an item: <paramref name="Units"/> towards items of
/// <paramref name="UnitsPerItem"/> each (carried over from item to item, <see cref="UnitCarry"/>).</summary>
public readonly record struct RockerYield(string Output, double Units, double UnitsPerItem);

/// <summary>What one step of rocking did.</summary>
/// <param name="Rocks">Rocks made (turns of θ); less than asked when the water, the charge or the riffles ran out.</param>
/// <param name="Litres">Water poured from the bucket.</param>
/// <param name="Washed">Items of the charge washed through (each one rock of tailings).</param>
/// <param name="Caught">Items caught behind the riffles, by code.</param>
public sealed record RockStep(double Rocks, double Litres, int Washed, IReadOnlyDictionary<string, int> Caught)
{
    public static readonly RockStep None = new(0, 0, 0, new Dictionary<string, int>());
}

/// <summary>Why the rocker does not rock now.</summary>
public enum RockerStop { None, NoCharge, RifflesFull, NoWater }

/// <summary>Why a charge is refused.</summary>
public enum RockerLoadVerdict { Loads, NotFeed, OtherFeed, Full }

/// <summary>
/// The rocker's state and rules (#716), game-independent: the charge in the hopper (one kind of feed,
/// up to <see cref="RockerConfig.ChargeItems"/> items: the game side keys a kind by its code and the
/// ore's state on the stack, so riddled and unriddled ore do not share a charge), how far the item
/// being washed has got (0..1), what lies behind the riffles (up to <see cref="RockerConfig.RiffleItems"/>,
/// taken out by hand), and the carry-over of metal units between items. It rocks only while water is
/// poured: each item takes <see cref="RockerConfig.RocksPerItem"/> rocks and
/// <see cref="RockerConfig.LitresPerItem"/> litres, poured evenly over its rocks. What an item gives is
/// the game side's to say (a delegate: the recovery figures are ore processing's).
/// </summary>
public sealed class RockerState
{
    private readonly Dictionary<string, int> _riffles = new(StringComparer.Ordinal);

    /// <summary>The charge's kind (the game side's key), or null.</summary>
    public string? ChargeKey { get; private set; }
    public int ChargeCount { get; private set; }
    /// <summary>How far the item being washed has got, 0..1.</summary>
    public double ItemWork { get; private set; }
    public UnitCarry Carry { get; } = new();

    public IReadOnlyDictionary<string, int> Riffles => _riffles;
    public int RiffleCount => _riffles.Values.Sum();
    public bool HasCharge => ChargeKey != null && ChargeCount > 0;
    public bool HasCatch => RiffleCount > 0;

    /// <summary>Whether the charge may be taken back out: nothing of the item at the top has been washed yet.</summary>
    public bool ChargeUntouched => ItemWork <= 0;

    /// <summary>Whether items of kind <paramref name="key"/> go into the hopper, and how many would.</summary>
    public RockerLoadVerdict CanLoad(string? key, bool isFeed, RockerConfig config, out int room)
    {
        room = 0;
        if (!isFeed || key == null)
            return RockerLoadVerdict.NotFeed;
        if (HasCharge && ChargeKey != key)
            return RockerLoadVerdict.OtherFeed;
        room = Math.Max(0, config.ChargeItems - (HasCharge ? ChargeCount : 0));
        return room > 0 ? RockerLoadVerdict.Loads : RockerLoadVerdict.Full;
    }

    /// <summary>Puts up to <paramref name="count"/> items of kind <paramref name="key"/> in; returns how many went in.</summary>
    public int Load(string key, int count, RockerConfig config)
    {
        if (CanLoad(key, true, config, out int room) != RockerLoadVerdict.Loads)
            return 0;
        int n = Math.Min(room, Math.Max(0, count));
        if (n == 0)
            return 0;
        if (!HasCharge)
        {
            ChargeKey = key;
            ChargeCount = 0;
            ItemWork = 0;
        }
        ChargeCount += n;
        return n;
    }

    /// <summary>Takes the charge back out while untouched: how many items, or 0.</summary>
    public int Unload()
    {
        if (!HasCharge || !ChargeUntouched)
            return 0;
        int n = ChargeCount;
        ClearCharge();
        return n;
    }

    /// <summary>Takes everything from behind the riffles.</summary>
    public IReadOnlyDictionary<string, int> TakeCatch()
    {
        var caught = new Dictionary<string, int>(_riffles, StringComparer.Ordinal);
        _riffles.Clear();
        return caught;
    }

    /// <summary>Why it would not rock now with <paramref name="litres"/> in the bucket.</summary>
    public RockerStop Stop(double litres, RockerConfig config) =>
        !HasCharge ? RockerStop.NoCharge
        : RiffleCount >= config.RiffleItems ? RockerStop.RifflesFull
        : config.LitresPerItem > 0 && !(litres > 1e-9) ? RockerStop.NoWater
        : RockerStop.None;

    /// <summary>
    /// Rocks it by up to <paramref name="rocks"/> with at most <paramref name="litres"/> to pour: the
    /// item at the top washes through as its rocks are made and its water poured; each item done gives
    /// what <paramref name="yieldOf"/> says (whole items behind the riffles, the rest carried over) and
    /// one rock of tailings. Stops at the end of the charge, when the riffles are full, or when the water
    /// runs out.
    /// </summary>
    public RockStep Rock(double rocks, double litres, RockerConfig config, Func<IReadOnlyList<RockerYield>> yieldOf)
    {
        if (!(rocks > 0) || Stop(litres, config) != RockerStop.None)
            return RockStep.None;
        double rocksLeft = rocks, water = Math.Max(0, litres), madeRocks = 0, poured = 0;
        int washed = 0;
        var caught = new Dictionary<string, int>(StringComparer.Ordinal);
        double litresPerRock = config.LitresPerItem / config.RocksPerItem;
        while (rocksLeft > 1e-12 && Stop(water, config) == RockerStop.None)
        {
            double toFinish = (1 - ItemWork) * config.RocksPerItem;
            double step = Math.Min(rocksLeft, toFinish);
            if (litresPerRock > 0)
                step = Math.Min(step, water / litresPerRock);
            if (!(step > 0))
                break;
            double used = step * litresPerRock;
            water -= used;
            poured += used;
            rocksLeft -= step;
            madeRocks += step;
            ItemWork += step / config.RocksPerItem;
            if (ItemWork < 1 - 1e-9)
                continue;
            // one item washed through
            foreach (var y in yieldOf())
            {
                int n = Carry.Add(y.Output, y.Units, y.UnitsPerItem);
                if (n <= 0)
                    continue;
                _riffles[y.Output] = _riffles.GetValueOrDefault(y.Output) + n;
                caught[y.Output] = caught.GetValueOrDefault(y.Output) + n;
            }
            washed++;
            ItemWork = 0;
            if (--ChargeCount <= 0)
                ClearCharge();
        }
        return new RockStep(madeRocks, poured, washed, caught);
    }

    private void ClearCharge()
    {
        ChargeKey = null;
        ChargeCount = 0;
        ItemWork = 0;
    }

    /// <summary>Restores a saved state.</summary>
    public void Restore(string? chargeKey, int chargeCount, double itemWork, IEnumerable<KeyValuePair<string, int>> riffles,
                        IEnumerable<KeyValuePair<string, double>> carry)
    {
        ClearCharge();
        if (!string.IsNullOrEmpty(chargeKey) && chargeCount > 0)
        {
            ChargeKey = chargeKey;
            ChargeCount = chargeCount;
            ItemWork = Math.Clamp(double.IsFinite(itemWork) ? itemWork : 0, 0, 1);
        }
        _riffles.Clear();
        foreach (var (k, v) in riffles)
            if (v > 0)
                _riffles[k] = v;
        Carry.Restore(carry);
    }
}

/// <summary>
/// What the rocker gives that is not ore processing's own recovery: crushed and ground ore are
/// <c>OreRecovery.HandConcentrate(HandStation.Rocker, stream)</c> (#765); placer gravel follows the gold
/// pan's table (#701's rule).
/// </summary>
public static class RockerRecovery
{
    /// <summary>
    /// Placer gravel (#701's rule, the owner's rulings): a block of rich gravel is <paramref name="pansPerBlock"/>
    /// pans' worth, and the rocker gives each metal of the gold pan's table at the rocker's figure over the
    /// pan's (<paramref name="rocker"/> / <paramref name="pan"/>, 55 / 45), the by-products (stone, flint,
    /// gems) at the pan's own rate, with no one-item cap: the expected count, carried from block to block.
    /// Placer counts as classified and takes no free-metal loss. <paramref name="drops"/> is the pan's table
    /// for this gravel: each drop's code, its chance a pan (<see cref="ChancePerPan"/>) and whether it is metal.
    /// </summary>
    public static IReadOnlyList<RockerYield> PlacerYield(double rocker, double pan,
                                                          IEnumerable<(string Code, double ChancePerPan, bool Metal)> drops, double pansPerBlock)
    {
        double scale = pan > 0 ? rocker / pan : 1;
        return drops
            .Where(d => d.ChancePerPan > 0)
            .Select(d => new RockerYield(d.Code, pansPerBlock * d.ChancePerPan * (d.Metal ? scale : 1), 1))
            .ToList();
    }

    /// <summary>
    /// The chance that one pan gives drop <paramref name="i"/> of a table with roll chances
    /// <paramref name="chances"/>: the game's pan shuffles the table, rolls each drop in turn and stops at
    /// the first hit, so a pan gives at most one item (the recipe exporter's <c>Panning.ChancePerPan</c>).
    /// </summary>
    public static double ChancePerPan(IReadOnlyList<double> chances, int i)
    {
        var n = chances.Count;
        var e = new double[n];
        e[0] = 1;
        var m = 0;
        for (var j = 0; j < n; j++)
        {
            if (j == i) continue;
            var q = 1 - chances[j];
            m++;
            for (var k = m; k > 0; k--) e[k] += e[k - 1] * q;
        }
        double reached = 0, binom = 1;
        for (var k = 0; k < n; k++)
        {
            reached += e[k] / binom;
            binom = binom * (n - 1 - k) / (k + 1);
        }
        return chances[i] * reached / n;
    }
}
