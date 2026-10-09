using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The power section (tools/recipe-export/Power/, docs/recipe-browser/power.md): the producers and
/// consumers with figures worked out by hand from the game's code and the mods' defaults (the pack
/// ships no config that changes them), every figure read live (no <c>fallback</c>: a mod update
/// that renames a setting fails here), and the wind's simulation close to the reference run.
/// </summary>
public partial class RecipeExportScenarios
{
    private JObject Power => (JObject)(Doc["power"] ?? throw new Xunit.Sdk.XunitException("the export has no power section"));

    private JObject Producer(string id) =>
        Power["producers"]!.Cast<JObject>().SingleOrDefault(p => (string)p["id"]! == id)
        ?? throw new Xunit.Sdk.XunitException($"no producer {id}; there are {string.Join(", ", Power["producers"]!.Select(p => (string)p["id"]!))}");

    private JObject Consumer(string id) =>
        Power["consumers"]!.Cast<JObject>().SingleOrDefault(c => (string)c["id"]! == id)
        ?? throw new Xunit.Sdk.XunitException($"no consumer {id}; there are {string.Join(", ", Power["consumers"]!.Select(c => (string)c["id"]!))}");

    private static void Near(double expected, JToken? actual, double tolerance = 1e-4) =>
        Assert.True(actual != null && Math.Abs(expected - (double)actual) <= tolerance, $"expected {expected}, got {actual}");

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Power_section_reads_every_figure_live()
    {
        var fallbacks = Power.SelectTokens("$..sources[?(@.fallback == true)]")
            .Select(s => $"{s.Parent!.Parent!.Parent!["id"]}: {s["what"]} ({s["from"]})").ToList();
        Assert.True(fallbacks.Count == 0, "figures from the exporter's defaults:\n  " + string.Join("\n  ", fallbacks));
        foreach (var e in Power["producers"]!.Concat(Power["consumers"]!))
        {
            Assert.NotEmpty((JArray)e["sources"]!);
            // Every entry of the pack links to an item the export holds.
            var item = (string?)e["item"];
            Assert.True(item != null && Doc["items"]![item] != null, $"{e["id"]} links to {item ?? "nothing"}");
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Power_producers_carry_their_models()
    {
        Assert.Equal(18, Power["producers"]!.Count());
        // The game's windmills: 5 wooden sails at powerMul 1, 10 metal ones at 1.25.
        Json("""
            { "kind": "wind", "speedPerWind": 1, "speedCap": 0.6, "torqueFactor": 3.125, "turbulencePenalty": true,
              "sails": { "count": 10, "max": 10, "torqueFactorPerSail": 0.3125 } }
            """, Producer("game:windmillrotor-metal@10")["model"]!);
        Assert.Equal(250, (int)Producer("game:windmillrotor-metal@10")["shownKN"]!);
        Assert.Equal(100, (int)Producer("game:windmillrotor-wood@5")["shownKN"]!);
        Assert.Equal("game:windmillrotor-wood-north", (string)Producer("game:windmillrotor-wood@5")["item"]!);

        // Millwright: modifier 2 × the blade type, no turbulence; the kN its block info shows.
        foreach (var (id, bm, sails, kn) in new[]
                 {
                     ("millwright:windmillrotor-single@7", 2.0, 7, 280), ("millwright:windmillrotor-double@5", 4.0, 5, 400),
                     ("millwright:windmillrotor-three@8", 1.5, 8, 240), ("millwright:windmillrotor-six@6", 3.0, 6, 360),
                     ("millwright:windmillrotorud-three@8", 2.0, 8, 320),
                 })
        {
            var p = Producer(id);
            var m = p["model"]!;
            Assert.Equal("wind", (string)m["kind"]!);
            Near(bm, m["speedPerWind"]);
            Near(0.6 * bm, m["speedCap"]);
            Near(sails * bm / 4, m["torqueFactor"]);
            Assert.False((bool)m["turbulencePenalty"]!);
            Assert.Equal(kn, (int)p["shownKN"]!);
        }

        // The water wheel: 0.004 push × 750, the three bottom cells 0.004 × (1 + √2).
        Json("""{ "kind": "rotor", "targetSpeed": 0.3, "torqueFactor": 3 }""", Producer("game:waterwheel@1")["model"]!);
        Near(7.242641, Producer("game:waterwheel@3")["model"]!["torqueFactor"]);

        var crank = Producer("handcrank:handcrank@1");
        Json("""{ "kind": "rotor", "targetSpeed": 0.5, "torqueFactor": 0.5 }""", crank["model"]!);
        Assert.Equal("2.5 satiety per second", (string)crank["cost"]!);
        Assert.Equal("muscle", (string)crank["family"]!);

        // ppex: P × 2 × 1 spread over the shaft speed (0.5 + P) × π / 5.
        foreach (var (id, power, steam) in new[]
                 {
                     ("ppex:enginewatt", 0.3, 30), ("ppex:enginecornish-low", 0.2, 8),
                     ("ppex:enginecornish-normal", 0.4, 16), ("ppex:enginecornish-high", 0.8, 32),
                 })
        {
            var p = Producer(id);
            var m = p["model"]!;
            Assert.Equal("constantPower", (string)m["kind"]!);
            Near(power * 2, m["budget"]);
            Near((0.5 + power) * Math.PI / 5, m["shaftSpeed"]);
            Near(0.25, m["minSpeed"]);
            Near((0.5 + power) * Math.PI / 5 * 2 / 3, m["taperFrom"]);
            Assert.Equal($"{steam} L/s of steam", (string)p["cost"]!);
        }
        Assert.Contains("between 2 and 4 atm", (string)Producer("ppex:enginewatt")["conditions"]!);

        // Yang Transport at the tier's maximum temperature: speed T/100 × acceleration, stall T/100 × power.
        foreach (var (tier, speed, stall) in new[]
                 {
                     ("primitive", 0.18, 0.18), ("simple", 0.33, 0.33), ("standard", 0.6, 0.66), ("advanced", 0.715, 0.78),
                 })
        {
            var m = Producer($"yangtransport:steamengine-{tier}")["model"]!;
            Near(speed, m["targetSpeed"]);
            Near(stall, (double)m["targetSpeed"]! * (double)m["torqueFactor"]!);
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Power_consumers_carry_their_loads()
    {
        foreach (var (id, load) in new[]
                 {
                     ("game:quern", 0.1), ("game:grindingwheel", 0.05), ("game:axlearm", 0.01), ("hydrateordiedrate:winch", 0.1),
                     ("aculinaryartillery:poweredmixingbowl", 0.05), ("game:archimedesscrew", 0.015), ("game:woodenaxle", 0.0005),
                     ("game:largegear3", 0.004), ("game:brake", 3), ("millwright:brake", 6), ("mpegearbox:gearbox12", 0.001),
                     ("mpecenteredspurgear:centeredspurgear", 0.001), ("madmechanics:transmission", 0.0005),
                     ("immersivewoodworking:sawmill", 0.085), ("immersivewoodworking:sawmill@flywheel", 0.051),
                     ("immersivewoodworking:chopper", 0.085), ("panningmachine:panningmachine", 0.085),
                     ("smex:convertertransmission", 0.25), ("ppex:mpfluidpump", 0.125), ("ppex:enginempgenerator", 0.0005),
                     ("gondolacablecar:station", 2.25), ("gondolacablecar:station@viking", 3.6), ("gondolacablecar:smallstation@1", 0.45),
                     ("seraphhorizons:buckingmill", 0.17), ("seraphhorizons:rosser", 0.2), ("seraphhorizons:gearcutter", 0.2),
                     ("seraphhorizons:drawbench", 0.2),
                 })
            Near(load, Consumer(id)["load"]);

        Json("""{ "dryMultiplier": 3, "tank": 10 }""", Consumer("game:pulverizerframe")["oil"]!);
        Near(0.005, Consumer("game:pulverizerframe")["idleLoad"]);
        Near(0.085, Consumer("game:pulverizerframe")["load"]);
        Near(0.125, Consumer("game:woodentoggle")["load"]);
        Assert.Equal("Helve hammer", (string)Consumer("game:woodentoggle")["name"]!);
        Assert.Equal("Quern", (string)Consumer("game:quern")["name"]!);
        Json("""{ "dryMultiplier": 3, "tank": 10 }""", Consumer("immersivewoodworking:chopper")["oil"]!);
        // The gear cutter's oil wears its kit, not its load.
        Json("""{ "dryMultiplier": 1, "tank": 10 }""", Consumer("seraphhorizons:gearcutter")["oil"]!);
        Near(0.35, Consumer("seraphhorizons:drawbench")["loadMax"]);
        Near(0.15, Consumer("smex:mpblower")["loadMax"]);
        Near(0.05, Consumer("smex:mpblower")["load"]);
        Near(0.02, Consumer("gondolacablecar:station")["idleLoad"]);
        Assert.Equal("transmission", (string)Consumer("game:woodenaxle")["category"]!);
        Assert.Equal("brake", (string)Consumer("millwright:brake")["category"]!);
    }

    /// <summary>A load read from block JSON agrees with what the placed block's behavior returns.</summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public async Task Power_consumer_loads_match_placed_blocks()
    {
        // This world loads no chunks of its own (no player joins): load the column, and stand the
        // blocks on rock, since the quern falls when unsupported.
        var pos = World.Spawn.AddCopy(-37, 12, 29);
        var w = World.Api.World;
        int size = GlobalConstants.ChunkSize;
        ((ICoreServerAPI)World.Api).WorldManager.LoadChunkColumnPriority(pos.X / size, pos.Z / size);
        await World.Until(() => w.BlockAccessor.GetChunkAtBlockPos(pos) != null, 30000);
        World.SetBlock("game:rock-granite", pos.DownCopy());
        foreach (var id in new[] { "game:quern", "game:grindingwheel" })
        {
            var c = Consumer(id);
            World.SetBlock((string)c["item"]!, pos);
            await World.Ticks(2);
            var be = World.Api.World.BlockAccessor.GetBlockEntity(pos);
            var mp = be?.GetBehavior<BEBehaviorMPConsumer>() ?? throw new Xunit.Sdk.XunitException(
                $"{c["item"]} has no MPConsumer at {pos} (block {World.Api.World.BlockAccessor.GetBlock(pos)?.Code}, entity {be?.GetType().Name ?? "none"})");
            Near((double)c["load"]!, mp.GetResistance(), 1e-6);
            World.SetBlock("game:air", pos);
            await World.Ticks(1);
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Power_wind_matches_the_reference_simulation()
    {
        var wind = Power["wind"]!;
        Json("""{ "years": 100, "samplesPerHour": 60, "seed": 1 }""", wind["simulation"]!);
        Json("""{ "base": 0.9, "divisor": 100, "cap": 1.5 }""", wind["altitude"]!);
        var share = wind["patterns"]!.ToDictionary(p => (string)p["code"]!, p => (double)p["share"]!);
        // An independent run of the same rules (200 years, another seed): still 15.0%, light 30.3%,
        // medium 19.9%, strong 25.5%, storm 9.2%; sea-level mean 0.42; 28.5% of the time at 0.6 or more.
        foreach (var (code, expected) in new[] { ("still", 0.15), ("lightbreeze", 0.303), ("mediumbreeze", 0.199), ("strongbreeze", 0.255), ("storm", 0.092) })
            Near(expected, share[code], 0.015);
        Assert.False((bool)wind["patterns"]!.Single(p => (string)p["code"]! == "still")["gusts"]!);
        // invexp durations: avg + var / 4 on average (lightbreeze: 6 + 48 / 4).
        var light = wind["patterns"]!.Single(p => (string)p["code"]! == "lightbreeze");
        Assert.Equal("invexp", (string)light["durationDist"]!);
        Near(18, light["durationMeanHours"]);
        Near(0.42, wind["mean"], 0.02);
        var bins = wind["histogram"]!["shares"]!.Select(s => (double)s).ToList();
        Near(1, bins.Sum(), 1e-3);
        Near(0.285, bins.Skip(60).Sum(), 0.015);
        var froms = wind["sources"]!.Select(s => (string)s["from"]!).ToList();
        Assert.Contains("game:config/windpatterns/storm.json", froms);
        Assert.Contains("code constant (WeatherSimulationRegion)", froms);
    }
}
