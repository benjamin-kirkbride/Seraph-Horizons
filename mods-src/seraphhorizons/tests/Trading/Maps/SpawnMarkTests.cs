using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>The spawn mark: who gets it on joining, and which prospector's camp it marks (the
/// nearest by real distance from where they joined, not merely the nearest ring).</summary>
public class SpawnMarkTests
{
    private const int S = TraderGrid.CellSize;

    [Fact]
    public void AFreshPlayerGetsItOnceAndAVeteranWithACampMarkedHasItSpent()
    {
        Assert.Equal(SpawnMarkDecision.Give, SpawnMark.Decide(false, []));
        // Other markers (deposits, settlements) don't make a veteran.
        Assert.Equal(SpawnMarkDecision.Give, SpawnMark.Decide(false, ["deposit:copper:1,2", "settlement:4,5"]));
        Assert.Equal(SpawnMarkDecision.Veteran, SpawnMark.Decide(false, ["deposit:copper:1,2", "camp:3,-1"]));
        // Had it: nothing again, whatever is on their map (a deleted waypoint included).
        Assert.Equal(SpawnMarkDecision.Done, SpawnMark.Decide(true, []));
        Assert.Equal(SpawnMarkDecision.Done, SpawnMark.Decide(true, ["camp:3,-1"]));
    }

    /// <summary>Sites at given block positions, measured from (x, z).</summary>
    private static Func<CellKey, CampOption?> Sites(double x, double z, Dictionary<CellKey, (int X, int Z)> sites) =>
        cell => sites.TryGetValue(cell, out var s)
            ? new CampOption(cell, TraderTypes.Prospector, s.X, s.Z, 0, CampLeads.Distance(s.X, s.Z, x, z))
            : null;

    [Fact]
    public void ItIsTheNearestProspectorByDistanceNotByRing()
    {
        // The player near the east edge of cell (0,0): a ring 1 prospector in the far corner to the
        // northwest, a ring 2 one just past the east neighbour. The ring 2 camp is nearer and wins.
        double x = S - 10, z = S / 2.0;
        var sites = new Dictionary<CellKey, (int X, int Z)>
        {
            [new CellKey(-1, -1)] = (-S + 200, -S + 200),
            [new CellKey(2, 0)] = (2 * S + 200, S / 2),
        };
        var target = SpawnMark.Target(x, z, SpawnMark.Reach, sites.ContainsKey, Sites(x, z, sites), new HashSet<CellKey>());
        Assert.Equal(new CellKey(2, 0), target!.Value.Cell);
    }

    [Fact]
    public void ItTakesTheOwnCellAndSkipsOtherTypesWhatTheyHaveAndCellsWithoutASite()
    {
        double x = 100, z = 100;
        var prospectors = new HashSet<CellKey> { new(0, 0), new(1, 0), new(0, 2) };
        var sites = new Dictionary<CellKey, (int X, int Z)>
        {
            [new CellKey(0, 0)] = (1500, 1500),
            [new CellKey(-1, 0)] = (-300, 100), // nearer, but not a prospector
            [new CellKey(0, 2)] = (100, 2 * S + 300),
            // (1,0) is a prospector that can take no camp: no site.
        };
        var site = Sites(x, z, sites);
        Assert.Equal(new CellKey(0, 0), SpawnMark.Target(x, z, SpawnMark.Reach, prospectors.Contains, site, new HashSet<CellKey>())!.Value.Cell);
        // Had already: the next.
        Assert.Equal(new CellKey(0, 2), SpawnMark.Target(x, z, SpawnMark.Reach, prospectors.Contains, site, new HashSet<CellKey> { new(0, 0) })!.Value.Cell);
        // None left within reach.
        Assert.Null(SpawnMark.Target(x, z, SpawnMark.Reach, prospectors.Contains, site, new HashSet<CellKey> { new(0, 0), new(0, 2) }));
        Assert.Null(SpawnMark.Target(x, z, 1, prospectors.Contains, site, new HashSet<CellKey> { new(0, 0) }));
    }

    [Fact]
    public void ItStopsAtTheRingNoNearerCampCanBeIn()
    {
        // A camp 300 blocks off in the player's own cell: ring 1 is at least 600 away (the player is
        // 600 from the cell's west edge), so it is never asked.
        double x = 600, z = 1000;
        var asked = new List<CellKey>();
        var sites = new Dictionary<CellKey, (int X, int Z)> { [new CellKey(0, 0)] = (900, 1000) };
        var inner = Sites(x, z, sites);
        var target = SpawnMark.Target(x, z, SpawnMark.Reach, _ => true, c =>
        {
            asked.Add(c);
            return inner(c);
        }, new HashSet<CellKey>());
        Assert.Equal(new CellKey(0, 0), target!.Value.Cell);
        Assert.Equal([new CellKey(0, 0)], asked);
    }

    [Fact]
    public void ClearanceIsTheDistanceToTheEdgeOfTheInnerRings()
    {
        var own = new CellKey(0, 0);
        Assert.Equal(0, SpawnMark.Clearance(own, 0, 10, 10));
        Assert.Equal(10, SpawnMark.Clearance(own, 1, 10, 1000));
        Assert.Equal(S + 10, SpawnMark.Clearance(own, 2, 10, 1000));
        Assert.Equal(48, SpawnMark.Clearance(own, 1, S - 48, 1000));
        // Negative cells as well.
        Assert.Equal(100, SpawnMark.Clearance(new CellKey(-1, -1), 1, -S + 100, -1000));
    }
}
