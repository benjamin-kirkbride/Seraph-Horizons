using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The spawn mark (Trading/Maps, switch <c>SpawnProspectorMark</c>): a fresh player joining a world
/// with the camp grid gets the nearest prospector's camp marked on their map once, approximate as a
/// bought lead's marker (settled as a lead's camp is, so it may land a few seconds after the join),
/// with a chat line saying how far and which way; joining again adds nothing, and nor does a join
/// after the marker is deleted. Its own seeded standard world (the trader maps' seed, where
/// <see cref="TradingMapsScenarios"/> has the mark off), in the <c>trading-misc</c> shard.
/// </summary>
[AtlasWorld(Seed = TradingMapsScenarioBase.Seed, WorldType = "standard")]
public class TradingSpawnMarkScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private ICoreServerAPI Api => World.Api;
    private MapsSystem Maps => Api.ModLoader.GetModSystem<MapsSystem>();

    private List<Waypoint> TraderMarks(IServerPlayer player) =>
        Api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single().Waypoints
            .Where(w => w.OwningPlayerUid == player.PlayerUID && w.Icon == "trader").ToList();

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task A_fresh_player_gets_the_nearest_prospectors_camp_marked_once_approximate()
    {
        Assert.True(SeraphHorizonsSystem.ConfigFor(Api).SpawnProspectorMark);
        Assert.True(Maps.Active);
        var trading = TradingSystem.Of(Api)!;
        Assert.True(trading.GridReady);

        var p = await World.JoinAtSpawn("newcomer");
        var sp = (IServerPlayer)p.Player;
        string uid = sp.PlayerUID;
        double jx = sp.Entity.Pos.X, jz = sp.Entity.Pos.Z;
        await World.Until(() => Maps.Leads.SpawnMarkGiven(uid), 600_000);
        Assert.False(Maps.SpawnMarkPending(uid));

        // One trader marker, a lead's: approximate, remembered by the camp's id at a lead's precision.
        var mark = Assert.Single(TraderMarks(sp));
        output.WriteLine($"joined at {jx:0},{jz:0}; marked '{mark.Title}' at {mark.Position.X:0},{mark.Position.Z:0}");
        Assert.EndsWith($"(approximate, ±{MapMarks.LeadReach} m)", mark.Title);
        var record = Assert.Single(MapMarksSystem.Of(Api)!.Book.Of(uid), r => r.Guid == mark.Guid);
        Assert.Equal(MapMarks.LeadPrecision, record.Precision);
        var cell = MapsSystem.CampCell(record.Key);
        Assert.NotNull(cell);

        // A placed prospector camp, where the marker is, near where they joined.
        var camp = trading.Camps!.Registry.Get(cell!.Value)!;
        Assert.Equal(CampStatus.Placed, camp.Status);
        Assert.Equal(TraderTypes.Prospector, camp.Type);
        Assert.True(trading.Grid!.IsProspector(cell.Value));
        Assert.Equal(camp.X + 0.5, mark.Position.X);
        Assert.Equal(camp.Z + 0.5, mark.Position.Z);
        Assert.Equal(TraderIds.Camp(cell.Value.X, cell.Value.Z), record.Key);
        Assert.InRange(CampLeads.Ring(TraderGrid.CellOf((int)jx, (int)jz), cell.Value), 0, SpawnMark.Reach);
        // No other prospector camp placed within reach is nearer.
        double d = CampLeads.Distance(camp.X, camp.Z, jx, jz);
        foreach (var other in CampLeads.CellsAround(TraderGrid.CellOf((int)jx, (int)jz), SpawnMark.Reach))
            if (trading.Camps.Registry.Get(other) is { Status: CampStatus.Placed, Type: TraderTypes.Prospector } c && other != cell.Value)
                Assert.True(CampLeads.Distance(c.X, c.Z, jx, jz) >= d, $"the prospector at {other} is nearer than {cell.Value}");
        // Told in chat how far and which way.
        string told = Assert.Single(p.Client.ChatLines(), l => l.StartsWith("The nearest prospector's camp is marked on your map", StringComparison.Ordinal));
        output.WriteLine(told);
        Assert.Contains("km", told);

        // Joining again adds nothing.
        await Rejoin("newcomer");
        sp = (IServerPlayer)World.Api.World.PlayerByUid(uid);
        await World.Ticks(60);
        Assert.False(Maps.SpawnMarkPending(uid));
        Assert.Single(TraderMarks(sp));

        // Nor after the marker is deleted: once ever.
        var layer = Api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single();
        layer.Waypoints.RemoveAll(w => w.OwningPlayerUid == uid && w.Guid == mark.Guid);
        await Rejoin("newcomer");
        sp = (IServerPlayer)World.Api.World.PlayerByUid(uid);
        await World.Ticks(60);
        Assert.False(Maps.SpawnMarkPending(uid));
        Assert.Empty(TraderMarks(sp));
        Assert.True(Maps.Leads.SpawnMarkGiven(uid));
    }

    /// <summary>The player leaves (the server drops them, as a real client's quit does) and joins again.</summary>
    private async Task Rejoin(string name)
    {
        var sp = (IServerPlayer)Api.World.AllOnlinePlayers.Single(pl => pl.PlayerName == name);
        sp.Disconnect();
        await World.Until(() => Api.World.AllOnlinePlayers.All(pl => pl.PlayerName != name), 30_000);
        await World.Ticks(20);
        var again = await World.JoinAtSpawn(name);
        Assert.Equal(sp.PlayerUID, again.Player.PlayerUID);
    }
}
