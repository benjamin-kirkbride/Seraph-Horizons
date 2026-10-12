using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

/// <summary>Watches each tick who broke what: a block gone this tick is the one whose strike's group
/// held it; counts those in its sector and out, overlapping boxes, and a block broken more than a layer
/// under the highest left (the floors more than a climb apart). For several eidolons mining one area
/// (EidolonSectorScenarios, EidolonTeamMineScenarios).</summary>
internal sealed class MineWatcher(IWorldAccessor w, List<BlockPos> toMine, EntityLaborEidolon[] eidolons, MineOrder[] orders)
{
    /// <summary>Whether two eidolons' boxes (1.7 wide) overlap.</summary>
    public static bool Overlap(Entity a, Entity b) => Math.Abs(a.Pos.X - b.Pos.X) < 1.7 && Math.Abs(a.Pos.Z - b.Pos.Z) < 1.7
        && Math.Abs(a.Pos.Y - b.Pos.Y) < 3.75;

    private static string Status(EntityLaborEidolon e) =>
        e.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") ?? "";

    private readonly HashSet<BlockPos> _standing = toMine.ToHashSet();
    /// <summary>Floors more than a climb apart.</summary>
    public readonly List<string> Faults = new();

    /// <summary>Every 30th tick of two overlapping, so it says when.</summary>
    public readonly List<string> Contacts = new();
    public readonly Dictionary<long, (int In, int Out)> BySector = eidolons.ToDictionary(e => e.EntityId, _ => (0, 0));
    public readonly Dictionary<long, List<BlockPos>> Broke = eidolons.ToDictionary(e => e.EntityId, _ => new List<BlockPos>());
    public int Overlaps;
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
            }
        }
        if (broke.Count > 0 && _standing.Count > 0 && broke.Min(c => c.Y) < _standing.Max(c => c.Y) - 1)
            Faults.Add($"{string.Join(" ", broke)} broke with {_standing.Max(c => c.Y)} left");
        for (int i = 0; i < eidolons.Length; i++)
        for (int j = i + 1; j < eidolons.Length; j++)
        {
            if (Overlap(eidolons[i], eidolons[j]) && Overlaps++ % 30 == 0)
                Contacts.Add($"{Describe(i)} and {Describe(j)}");
            // apart while all are at work (none reported, none on its way back)
            if (orders.All(o => o.Reported == null) && eidolons.All(e => e.Orders?.OrderCode == MineOrder.OrderCode))
            {
                DistanceSum += Math.Sqrt(eidolons[i].Pos.SquareHorDistanceTo(eidolons[j].Pos.XYZ));
                DistanceTicks++;
            }
        }
    }

    private string Describe(int i) =>
        $"{eidolons[i].EntityId} at {eidolons[i].Pos.XYZ} ({eidolons[i].Orders?.OrderCode}, {Status(eidolons[i])})";

    public double MeanDistance => DistanceTicks == 0 ? 0 : DistanceSum / DistanceTicks;
}
