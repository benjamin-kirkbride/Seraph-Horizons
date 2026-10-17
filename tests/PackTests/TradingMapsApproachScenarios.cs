using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Glue;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Trader maps (#455, <see cref="TradingMapsScenarioBase"/>): a player nearing a prospector camp far
/// away has its deposits checked in the background, one at a time (#693). A class of its own: the
/// checks (the camps round the spawn the player joins at, then the far camp's) generate a deposit's
/// columns each.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class TradingMapsApproachScenarios(ITestOutputHelper output) : TradingMapsScenarioBase(output)
{
    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Nearing_a_prospector_camp_checks_its_deposits_in_the_background()
    {
        var trading = TradingSystem.Of(Api)!;
        var surveys = Maps.Surveys!;
        Assert.Equal(1500, surveys.ApproachMetres);
        // A prospector camp far from anything generated, so none of its deposits is checked yet.
        var spawnCell = Mod.Trading.Core.TraderGrid.CellOf(World.Spawn.X, World.Spawn.Z);
        var far = new Mod.Trading.Core.CellKey(spawnCell.X + 12, spawnCell.Z + 12);
        var cell = Mod.Trading.Maps.Core.CampLeads.CellsAround(far, 2).First(c => trading.Grid!.IsProspector(c) && Maps.SiteOf(c) != null);
        var site = Maps.SiteOf(cell)!.Value;
        var picks = Maps.PickOre(Deposits, site.X, site.Z).Offers;
        var field = Maps.PickGravel(Deposits, site.X, site.Z).Offer;
        output.WriteLine($"prospector cell {cell} at {site.X},{site.Z}: picks {string.Join(", ", picks.Select(p => $"{p.Key}{(p.Surveyed ? "" : " (unchecked)")}"))}; "
                         + $"gravel {field?.Key}");
        Assert.NotEmpty(picks);
        Assert.Contains(picks, p => !p.Surveyed);
        int before = surveys.Checked;

        // A player comes within 1.2 km of it: the camp's would-be offers are queued at the next scan.
        var p = await World.JoinAtSpawn("surveyor");
        var sp = (IServerPlayer)p.Player;
        sp.WorldData.CurrentGameMode = EnumGameMode.Creative;
        sp.Entity.TeleportTo(site.X + 1200, 200, site.Z);
        await World.Ticks(5);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await World.Until(() => picks.Any(c => surveys.Pending(c.Key) || Deposits.Candidate(c.Key)?.Surveyed != false), 5000);
        output.WriteLine($"queued after {watch.Elapsed.TotalSeconds:0.0} s: {surveys.Queue.Waiting} waiting, {surveys.Queue.Running} running");
        Assert.Contains(picks, c => surveys.Pending(c.Key) || Deposits.Candidate(c.Key)?.Surveyed != false);

        // Throttled: one background check at a time, until everything the camp would offer is checked.
        int most = 0;
        bool Done()
        {
            most = Math.Max(most, surveys.Queue.Running);
            return Maps.PickOre(Deposits, site.X, site.Z).Offers.All(c => c.Surveyed)
                   && Maps.PickGravel(Deposits, site.X, site.Z).Offer is null or { Surveyed: true };
        }
        await World.Until(Done, 2_000_000);
        var after = Maps.PickOre(Deposits, site.X, site.Z).Offers;
        output.WriteLine($"all checked after {watch.Elapsed.TotalSeconds:0.0} s, {surveys.Checked - before} checks, at most {most} running at once: "
                         + string.Join("; ", after.Select(c => $"{c.Key} {c.Record.Tier} {string.Join(" and ", c.Record.Makeup!.MainOres())}"
                                                              + $" {c.Record.Makeup.Mix()?.Code} in {c.Record.Makeup.HostRock()}")));
        Assert.True(Done(), "the camp's deposits were not all checked");
        Assert.True(most <= 1, $"{most} background checks ran at once");
        Assert.True(surveys.Checked > before);
        sp.Entity.TeleportTo(World.Spawn.X, World.Spawn.Y + 1, World.Spawn.Z);
    }
}
