using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// seraphhorizons' <c>SlowerRailVehicles</c> (mods-src/seraphhorizons/RailVehicleSpeed.cs) against the
/// pinned Yang's Transport Tycoon: Yang's track caps and engine figures are still the ones the README
/// halves; a standard locomotive on metal standard-gauge track and a primitive engine cart on wooden
/// wagon rails, placed by their items, read half their track's cap where they are; each one's engine,
/// at its top heat, gives its convoy half its top speed; the offscreen cruise is patched; and the
/// handcar, which has its own figures, is not slowed (its top speeds are
/// <see cref="Handcar_is_pumped_braked_reversed_held_and_steered_by_its_riders"/>'s). The off check is
/// in <see cref="SwitchesOffScenarios"/>. A part of the handcar's class: it builds on the same track
/// and players, on the same plain world.
/// </summary>
public partial class HandcarScenarios
{
    private const string WagonRail = "yangtransport:wagonrails-straight-ne-wood";   // runs along z
    private const string PrimitiveEngineCart = "yangtransport:enginecart-primitive";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The material cap of the track piece a vehicle is on, as its tick reads it (its rail
    /// cursor's <c>SegmentMaterialSpeedCapBPS</c>).</summary>
    private static int CursorCap(Entity vehicle)
    {
        var cursor = vehicle.GetType().GetField("Cursor", AnyInstance)?.GetValue(vehicle);
        Assert.NotNull(cursor);
        return (ushort)cursor.GetType().GetField("SegmentMaterialSpeedCapBPS")!.GetValue(cursor)!;
    }

    /// <summary>The top speed the vehicle's own drive gives a convoy of <paramref name="weight"/> with
    /// its steam engine at <paramref name="temperatureC"/>: Yang's <c>TryComputeConvoyDrive</c>, run as
    /// the lead's tick runs it.</summary>
    private static double DriveTopSpeed(Entity lead, double temperatureC, int weight)
    {
        var steam = lead.GetBehavior(RailVehicleSpeed.SteamBehaviour);
        Assert.NotNull(steam);
        var engine = steam.GetType().GetField("EngineController", AnyInstance)!.GetValue(steam)!;
        AccessTools.Property(engine.GetType(), "TemperatureC").SetValue(engine, temperatureC);
        object?[] args = [lead, 1, weight, 0.0, 0.0, 0.0];
        var drive = AccessTools.DeclaredMethod(lead.GetType(), RailVehicleSpeed.DriveMethod);
        Assert.True((bool)drive.Invoke(lead, args)!, $"{lead.Code}'s steam engine at {temperatureC} °C gave no drive");
        return (double)args[5]!;
    }

    /// <summary>Places the vehicle of <paramref name="code"/> on the rail at <paramref name="rail"/> with its item.</summary>
    private async Task<Entity> PlaceVehicle(IServerPlayer by, string code, BlockPos rail)
    {
        var item = W.GetItem(new AssetLocation(code));
        Assert.NotNull(item);
        var sapi = (ICoreServerAPI)World.Api;
        var before = sapi.World.LoadedEntities.Keys.ToHashSet();
        var handling = EnumHandHandling.NotHandled;
        item.OnHeldInteractStart(new DummySlot(new ItemStack(item)), by.Entity,
            new BlockSelection { Position = rail.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.1, 0.5) },
            null, true, ref handling);
        await World.Ticks(10);
        var placed = sapi.World.LoadedEntities.Values.SingleOrDefault(e => !before.Contains(e.EntityId) && e.Code.ToString() == code);
        Assert.True(placed != null, $"{code}'s item placed nothing on the track at {rail}");
        return placed;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Yangs_vehicles_run_at_half_their_top_speed()
    {
        Assert.True(RailVehicleSpeed.Patched, "SlowerRailVehicles patched nothing");
        Assert.Equal(0.5, RailVehicleSpeed.Factor);

        // The figures the README's table halves.
        var caps = W.Api.Assets.Get(new AssetLocation("yangtransport:config/trackmaterialspeeds.json"))
            .ToObject<Dictionary<string, Dictionary<string, int>>>();
        Assert.Equal(8, caps["0"]["wood"]);
        Assert.Equal(14, caps["0"]["metal"]);
        Assert.Equal(16, caps["1"]["wood"]);
        Assert.Equal(32, caps["1"]["metal"]);

        // The offscreen cruise is scaled whole, its cap read unscaled inside it.
        var cruise = AccessTools.DeclaredMethod(AccessTools.TypeByName(RailVehicleSpeed.ConvoySimType), RailVehicleSpeed.CruiseMethod);
        var cruisePatches = Harmony.GetPatchInfo(cruise);
        Assert.NotNull(cruisePatches);
        Assert.Contains(cruisePatches.Postfixes, p => p.PatchMethod.DeclaringType == typeof(RailVehicleSpeed));
        Assert.Contains(cruisePatches.Finalizers, p => p.PatchMethod.DeclaringType == typeof(RailVehicleSpeed));

        var track = await HandcarTrack(World.Spawn.AddCopy(-250, 45, -45), 30);
        var wagon = await HandcarTrack(World.Spawn.AddCopy(-260, 45, -45), 20);
        for (int dz = 0; dz < 20; dz++)
            World.SetBlock(WagonRail, wagon.AddCopy(0, 0, dz));
        await World.Ticks(10);
        var driver = await HandcarPlayer("speeddriver", track.AddCopy(2, 0, 8));
        var placed = new List<Entity>();
        try
        {
            var loco = await PlaceVehicle(driver, StandardLocomotive, track.AddCopy(0, 0, 15));
            placed.Add(loco);
            var cart = await PlaceVehicle(driver, PrimitiveEngineCart, wagon.AddCopy(0, 0, 10));
            placed.Add(cart);
            var handcar = await PlaceHandcar(driver, track.AddCopy(0, 0, 4));
            placed.Add(handcar);
            await World.Ticks(10);

            // The track: metal standard gauge 32 -> 16, wooden wagon rails 8 -> 4. The handcar on the same
            // metal track reads the same 16, over its own top speeds (it is not slowed by it).
            Assert.Equal(16, CursorCap(loco));
            Assert.Equal(4, CursorCap(cart));
            Assert.Equal(16, CursorCap(handcar));
            Assert.True(new Mod.Handcar.Core.HandcarConfig().TopSpeedTwo < 16 / 2.0, "the handcar's top speed reaches the slowest halved standard-gauge cap");

            // The engines, bare, at their top heat: (power x heat - weight) / 2.
            Assert.Equal((4.0 * 11 - 10) / 2, DriveTopSpeed(loco, 1100, 10), 6);   // standard locomotive: 34 -> 17
            Assert.Equal((1.0 * 8 - 1) / 2, DriveTopSpeed(cart, 800, 1), 6);       // primitive engine cart: 7 -> 3.5
            // A loaded convoy halves too: the cart pulling three minecarts, 4 -> 2.
            Assert.Equal((1.0 * 8 - 4) / 2, DriveTopSpeed(cart, 800, 4), 6);
        }
        finally
        {
            foreach (var e in placed)
                e.Die(EnumDespawnReason.Removed);
            await World.Ticks(2);
        }
    }
}
