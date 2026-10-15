using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

/// <summary>Watches each tick who broke what: a block gone this tick is the one whose strike's group
/// held it; counts those in the columns of the station it struck (<see cref="MineOrder.Sector"/>) and out,
/// overlapping boxes, a block broken in another eidolon's columns at any height (under it is its floor, over
/// it is over it), and, with <c>floors</c>, a block broken more than <c>floorSlack</c> layers under the
/// highest left (the floors more than that many climbs apart: 1, or 2 where a crew by look-ahead routes
/// works spare work a band deeper, README "Eidolon", working together, *Routes*), those <c>exempt</c> (what the plan keeps for good: out of reach, the
/// floor under those, the way out) not counted as left. For several eidolons mining one area
/// (EidolonSectorScenarios, EidolonTeamMineScenarios, EidolonTallTunnelScenarios).
///
/// <para>Contact is allowed only shallow (<see cref="Pushing"/>): two boxes brushing as one settles or
/// squeezes past another, however long, is no fault (timing on a slower server stretches it), but one deep
/// in another is. Every contact is logged with how long it lasted.</para></summary>
internal sealed class MineWatcher(IWorldAccessor w, List<BlockPos> toMine, EntityLaborEidolon[] eidolons, MineOrder[] orders,
    System.Func<MarkPos, bool>? exempt = null, bool floors = true, int floorSlack = 1)
{
    /// <summary>Whether two eidolons' boxes (1.7 wide) overlap.</summary>
    public static bool Overlap(Entity a, Entity b) => Math.Abs(a.Pos.X - b.Pos.X) < 1.7 && Math.Abs(a.Pos.Z - b.Pos.Z) < 1.7
        && Math.Abs(a.Pos.Y - b.Pos.Y) < 3.75 - TouchSlack;

    /// <summary>One box's top at the other's feet (one climbing a step past one at its foot) is touching,
    /// not overlapping.</summary>
    private const double TouchSlack = 0.05;

    /// <summary>
    /// Claim separation as the members strike (README "Eidolon", working together, *Claims*): an eidolon
    /// striking stands in its own claim, so no column of its box nor of the group it strikes may be held by
    /// another member; null when none is. (Two members holding one column is impossible on the board, so
    /// this is what tells a claim taken from under one.)
    /// </summary>
    public static string? ClaimFault(JobBoard<(int X, int Z), MineJobCells> board, EntityLaborEidolon[] eidolons, MineOrder[] orders, double now)
    {
        for (int i = 0; i < eidolons.Length; i++)
        {
            if (orders[i].Group is not { } group)
                continue;
            // the columns of the place it stands at (the node nearest it: it strikes within 0.2 of it, which
            // may brush a third column by a hair)
            var node = WidePath.NodeAt(eidolons[i].Pos.X, eidolons[i].Pos.Y, eidolons[i].Pos.Z);
            var columns = new List<(int X, int Z)>();
            for (int x = node.X - 1; x <= node.X; x++)
            for (int z = node.Z - 1; z <= node.Z; z++)
                columns.Add((x, z));
            columns.AddRange(group.Cells.Select(c => (c.X, c.Z)));
            foreach (var column in columns)
                if (board.Holder(column, now) is { } holder && holder != eidolons[i].EntityId)
                    return $"{eidolons[i].EntityId} strikes at {eidolons[i].Pos.XYZ} with column {column} held by {holder}";
        }
        return null;
    }

    /// <summary>The nearest two centres may come, across: nearer, one box is deep in the other (their
    /// boxes are 1.7 wide), never a brush.</summary>
    public const double MinCentres = 1.0;

    private static string Status(EntityLaborEidolon e) =>
        e.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") ?? "";

    private readonly HashSet<BlockPos> _standing = toMine.ToHashSet();
    /// <summary>Floors more than a climb apart, and blocks broken in another's columns.</summary>
    public readonly List<string> Faults = new();

    /// <summary>Every contact: who, where and how each was at its start, and how long it lasted and how
    /// near they came.</summary>
    public readonly List<string> Contacts = new();
    private readonly Dictionary<(int, int), (long Since, double Nearest, string Who)> _touching = new();
    public readonly Dictionary<long, (int In, int Out)> BySector = eidolons.ToDictionary(e => e.EntityId, _ => (0, 0));
    public readonly Dictionary<long, List<BlockPos>> Broke = eidolons.ToDictionary(e => e.EntityId, _ => new List<BlockPos>());
    /// <summary>Ticks two overlapped, each pair counted.</summary>
    public int Overlaps;
    /// <summary>All the time two overlapped, the longest contact (both for the log) and the nearest two
    /// overlapping centres came (<see cref="MinCentres"/>).</summary>
    public long ContactMs, LongestMs;
    public double Nearest = double.MaxValue;
    private long _lastTick = -1;
    public double DistanceSum;
    public int DistanceTicks;

    public void Tick()
    {
        var broke = _standing.Where(c => w.BlockAccessor.GetBlock(c).Id == 0).ToList();
        foreach (var c in broke)
        {
            _standing.Remove(c);
            var cell = new MarkPos(c.X, c.Y, c.Z);
            for (int i = 0; i < eidolons.Length; i++)
            {
                if (orders[i].Group?.Cells.Contains(cell) != true)
                    continue;
                var (inside, outside) = BySector[eidolons[i].EntityId];
                BySector[eidolons[i].EntityId] = orders[i].Sector is { } s && s.Contains(c.X, c.Z) ? (inside + 1, outside) : (inside, outside + 1);
                Broke[eidolons[i].EntityId].Add(c);
                for (int j = 0; j < eidolons.Length; j++)
                    if (j != i && EidolonMining.UnderFoot(eidolons[j].Pos.X, eidolons[j].Pos.Z, cell))
                        Faults.Add($"{c} broke by {eidolons[i].EntityId} in the columns of {eidolons[j].EntityId} at {eidolons[j].Pos.XYZ}");
            }
        }
        var left = exempt == null ? _standing.ToList() : _standing.Where(c => !exempt(new MarkPos(c.X, c.Y, c.Z))).ToList();
        if (floors && broke.Count > 0 && left.Count > 0 && broke.Min(c => c.Y) < left.Max(c => c.Y) - floorSlack)
            Faults.Add($"{string.Join(" ", broke)} broke with {left.Max(c => c.Y)} left");
        long now = w.ElapsedMilliseconds, step = _lastTick < 0 ? 0 : now - _lastTick;
        _lastTick = now;
        for (int i = 0; i < eidolons.Length; i++)
        for (int j = i + 1; j < eidolons.Length; j++)
        {
            if (Overlap(eidolons[i], eidolons[j]))
            {
                Overlaps++;
                ContactMs += step;
                double d = Math.Sqrt(eidolons[i].Pos.SquareHorDistanceTo(eidolons[j].Pos.XYZ));
                Nearest = Math.Min(Nearest, d);
                _touching[(i, j)] = _touching.TryGetValue((i, j), out var t)
                    ? t with { Nearest = Math.Min(t.Nearest, d) }
                    : (now, d, $"{Describe(i)} and {Describe(j)}");
                LongestMs = Math.Max(LongestMs, now - _touching[(i, j)].Since);
            }
            else if (_touching.Remove((i, j), out var t))
                Contacts.Add($"{now - t.Since} ms, nearest {t.Nearest:0.00}: {t.Who}");
            // apart while all are at work (none reported, none on its way back)
            if (orders.All(o => o.Reported == null) && eidolons.All(e => e.Orders?.OrderCode == MineOrder.OrderCode))
            {
                DistanceSum += Math.Sqrt(eidolons[i].Pos.SquareHorDistanceTo(eidolons[j].Pos.XYZ));
                DistanceTicks++;
            }
        }
    }

    private string Describe(int i) =>
        $"{eidolons[i].EntityId} at {eidolons[i].Pos.XYZ} ({eidolons[i].Orders?.OrderCode}, {Status(eidolons[i])}, "
        + $"motion {eidolons[i].Pos.Motion.X:0.000} {eidolons[i].Pos.Motion.Z:0.000}{(eidolons[i].ClimbTop is { } t ? $", climbing to {t}" : "")})";

    /// <summary>Contact deeper than <see cref="MinCentres"/>, or null: what an assertion says, with every
    /// contact (each still going counted as it stands).</summary>
    public string? Pushing()
    {
        var open = _touching.Select(t => $"{_lastTick - t.Value.Since} ms so far, nearest {t.Value.Nearest:0.00}: {t.Value.Who}");
        string all = string.Join(" | ", Contacts.Concat(open));
        if (Nearest < MinCentres)
            return $"overlapping {ContactMs} ms in all ({Overlaps} ticks), longest {LongestMs} ms, nearest {Nearest:0.00}: {all}";
        return null;
    }

    /// <summary>The contacts for the log: ticks, time, longest and nearest, then each.</summary>
    public string Summary() =>
        $"{Overlaps} ticks, {ContactMs} ms, longest {LongestMs} ms, nearest {(Overlaps == 0 ? "-" : Nearest.ToString("0.00"))}: "
        + string.Join(" | ", Contacts.Concat(_touching.Select(t => $"{_lastTick - t.Value.Since} ms so far: {t.Value.Who}")));

    public double MeanDistance => DistanceTicks == 0 ? 0 : DistanceSum / DistanceTicks;
}
