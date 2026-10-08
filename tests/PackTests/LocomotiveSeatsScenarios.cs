using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// seraphhorizons' <c>LocomotiveRidersStayOn</c> and <c>LocomotiveRidersBreathe</c>
/// (mods-src/seraphhorizons/LocomotiveSeats.cs) against the pinned Yang's Transport Tycoon: a
/// standard locomotive (whose type sets <c>CollisionCheckSeats</c>) no longer has its seats checked
/// for blocks, while the handcar, which sets it too, keeps the check; and the breathing postfix, on
/// the game's check, gives a survival rider seated in the locomotive with a block at the eye their
/// air back, but not under water, and not once they are off. Their off checks are in <see cref="SwitchesOffScenarios"/>. A part of
/// the handcar's class: it builds on the same track and players, on the same plain world.
/// </summary>
public partial class HandcarScenarios
{
    private const string StandardLocomotive = "yangtransport:sglocomotive-standard";

    /// <summary>Yang's <c>RailVehicleSeat.CollisionChecksEnabled(Entity)</c>, as Yang's code calls it.</summary>
    private static bool CollisionChecksEnabled(Entity vehicle)
    {
        var method = AccessTools.DeclaredMethod(AccessTools.TypeByName(LocomotiveSeats.SeatType), LocomotiveSeats.CollisionCheckMethod, [typeof(Entity)]);
        Assert.NotNull(method);
        return (bool)method.Invoke(null, [vehicle])!;
    }

    /// <summary>The block holding the entity's eye, as <c>EntityBehaviorBreathe.Check</c> finds it.</summary>
    private static BlockPos EyeBlock(Entity e)
    {
        var pos = e.Pos;
        return new BlockPos((int)(pos.X + e.LocalEyePos.X), (int)(pos.Y + e.Properties.EyeHeight), (int)(pos.Z + e.LocalEyePos.Z), pos.Dimension);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Locomotive_riders_are_not_checked_for_blocks_and_breathe_inside_one()
    {
        Assert.True(Harmony.HasAnyPatches(LocomotiveSeats.StayOnHarmonyId), "LocomotiveRidersStayOn patched nothing");
        Assert.True(Harmony.HasAnyPatches(LocomotiveSeats.BreatheHarmonyId), "LocomotiveRidersBreathe patched nothing");

        var track = await HandcarTrack(World.Spawn.AddCopy(-210, 45, -45), 40);
        var driver = await HandcarPlayer("locodriver", track.AddCopy(2, 0, 8));
        Entity? loco = null;
        Entity? handcar = null;
        var changed = new List<BlockPos>();
        try
        {
            var item = W.GetItem(new AssetLocation(StandardLocomotive));
            Assert.NotNull(item);
            var before = ((Vintagestory.API.Server.ICoreServerAPI)World.Api).World.LoadedEntities.Keys.ToHashSet();
            var handling = EnumHandHandling.NotHandled;
            item.OnHeldInteractStart(new DummySlot(new ItemStack(item)), driver.Entity,
                new BlockSelection { Position = track.AddCopy(0, 0, 25), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.1, 0.5) },
                null, true, ref handling);
            await World.Ticks(5);
            loco = ((Vintagestory.API.Server.ICoreServerAPI)World.Api).World.LoadedEntities.Values
                .SingleOrDefault(e => !before.Contains(e.EntityId) && e.Code.ToString() == StandardLocomotive);
            Assert.True(loco != null, "the standard locomotive's item placed no locomotive on the track");
            handcar = await PlaceHandcar(driver, track.AddCopy(0, 0, 8));

            // Both types ask for the check; only the locomotive's is answered no.
            Assert.True(loco.Properties.Attributes["CollisionCheckSeats"].AsBool(), "the standard locomotive no longer sets CollisionCheckSeats");
            Assert.True(handcar.Properties.Attributes["CollisionCheckSeats"].AsBool(), "the handcar no longer sets CollisionCheckSeats");
            Assert.False(CollisionChecksEnabled(loco), "the locomotive's seats are still checked for blocks");
            Assert.True(CollisionChecksEnabled(handcar), "the handcar's seats are no longer checked for blocks");
            Assert.True(LocomotiveSeats.IsLocomotive(loco));
            Assert.False(LocomotiveSeats.IsLocomotive(handcar));

            // The breathing postfix is on the game's check. The game's own block test never takes the
            // air away at a world position (it intersects the block's box in world coordinates with the
            // entity's selection box in its own), so the postfix is run here on a lost breath.
            var check = AccessTools.DeclaredMethod(typeof(EntityBehaviorBreathe), nameof(EntityBehaviorBreathe.Check));
            Assert.Contains(Harmony.GetPatchInfo(check)?.Postfixes ?? [], p => p.owner == LocomotiveSeats.BreatheHarmonyId);
            var seat = loco.GetBehavior<EntityBehaviorSeatable>()!.Seats.First();
            Assert.True(driver.Entity.TryMount(seat), "the driver could not take the locomotive's seat");
            await World.Ticks(5);
            Assert.True(LocomotiveSeats.RidesLocomotive(driver.Entity));
            var breathe = driver.Entity.GetBehavior<EntityBehaviorBreathe>()!;
            var eye = EyeBlock(driver.Entity);

            // Seated, a block at the eye: the air is given back.
            World.SetBlock("game:rock-granite", eye);
            changed.Add(eye);
            breathe.HasAir = false;
            LocomotiveSeats.BreatheCheckPostfix(breathe);
            Assert.True(breathe.HasAir, $"a rider seated in the locomotive with granite at the eye ({eye}) has no air");

            // Seated, under water: still drowns.
            World.SetBlock("game:water-still-7", eye);
            breathe.HasAir = false;
            LocomotiveSeats.BreatheCheckPostfix(breathe);
            Assert.False(breathe.HasAir, $"a rider seated in the locomotive under water at the eye ({eye}) breathes");
            World.SetBlock("game:air", eye);

            // Off the seat: nothing given.
            driver.Entity.TryUnmount();
            await World.Ticks(2);
            Assert.Null(driver.Entity.MountedOn);
            Assert.False(LocomotiveSeats.RidesLocomotive(driver.Entity));
            breathe.HasAir = false;
            LocomotiveSeats.BreatheCheckPostfix(breathe);
            Assert.False(breathe.HasAir, "a player off the locomotive was given air");
            breathe.HasAir = true;
        }
        finally
        {
            driver.Entity.TryUnmount();
            foreach (var pos in changed)
                World.SetBlock("game:air", pos);
            loco?.Die(EnumDespawnReason.Removed);
            handcar?.Die(EnumDespawnReason.Removed);
            await World.Ticks(2);
        }
    }
}
