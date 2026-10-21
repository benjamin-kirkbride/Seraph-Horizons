using Atlas.Api;
using Atlas.XUnit;
using Newtonsoft.Json;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, two eidolons on a player's mining area cut into a monzonite hillside
/// (fixtures/eidolon-hillside, a WorldEdit export of it mid-job): a third of it covered, driven as a tunnel,
/// the rest open ground. Only one fits at the face at a time; the other quarries the open ground away from
/// the face's approach, works ahead in the band below, and opens a second face only with neither (README
/// "Eidolon", working together, *Spare work*). Over 8 minutes on the virtual clock (<see cref="VirtualClock"/>)
/// both keep mining (neither spends over <see cref="LongestGap"/> at a time neither striking nor walking to its own
/// station), and they mine at least
/// <see cref="Faster"/> times what main did here before (<see cref="Before"/>: a regression guard). In the sky (<see cref="Felling.Sky"/>), the export placed whole.
/// </summary>
[AtlasWorld]
public class EidolonHillsideScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    // The export's own corner (its OriginalPos) and the area and starts in the player's world.
    private static readonly BlockPos Original = new(511862, 113, 512212);
    private static readonly BlockPos AreaFrom = new(511878, 117, 512222), AreaTo = new(511914, 128, 512249);
    private static readonly BlockPos[] Starts = [new(511891, 124, 512236), new(511884, 122, 512233)];

    private const double Minutes = 8;
    /// <summary>What main mined here before the work it guards, and the share more it must mine: the mean of 1005,
    /// 1149, 979, 889, 1046 and 994 blocks, this scenario on main at 6ccd75e (when two took turns at the face) on the
    /// virtual clock with SH_CLOCK_SEED 1 to 6 (each seed plays the same every run; this branch, the same seeds:
    /// 1169, 1153, 1147, 1120, 1090, 1225, a mean of 1151). The scenario runs seed 1.</summary>
    private const int Before = 1010;
    private const double Faster = 1.1;

    /// <summary>The longest either may spend at a time neither striking nor walking to its own station (its route's
    /// head, or the station dispatch gave it): waiting, parting, stopped short of another (the owner's bar). A walk
    /// to a planned station counts as work: one station on the hilltop is a 36 s walk from the heading.</summary>
    private const double LongestGap = 25;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-hillside-test] {0}", what);
        output.WriteLine(what);
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private static void RightClick(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Two_on_a_hillside_both_mine_throughout_the_open_ground_while_one_drives_the_face()
    {
        string file = Path.Combine(Path.GetDirectoryName(typeof(EidolonHillsideScenarios).Assembly.Location)!,
            "fixtures", "eidolon-hillside", "hillside.json");
        var schematic = JsonConvert.DeserializeObject<BlockSchematic>(File.ReadAllText(file))!;
        var centre = World.Spawn.AddCopy(-720, 0, 1600);
        centre.Y = W.BlockAccessor.MapSizeY - 2 - 44;
        var site = await Felling.Sky(World, centre, reach: 40);
        // the export's corner, so that its 63 x 49 lie round the cleared site
        var corner = site.AddCopy(-31, 0, -24);
        BlockPos At(BlockPos p) => new(corner.X + p.X - Original.X, corner.Y + p.Y - Original.Y, corner.Z + p.Z - Original.Z);
        schematic.Init(W.BlockAccessor);
        schematic.Place(W.BlockAccessor, W, corner, EnumReplaceMode.ReplaceAll, true);
        await World.Ticks(20);

        var p = await World.JoinRetrying("eidolonhillside");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(At(new BlockPos(511895, 140, 512235)));
        var tool = player.InventoryManager.ActiveHotbarSlot;
        tool.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        tool.MarkDirty();

        using var clock = VirtualClock.Start(World);
        var eidolons = new List<EntityLaborEidolon>();
        foreach (var at in Starts)
            eidolons.Add(await Spawn(At(at), player));
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(player, tool, block: At(AreaFrom));
        RightClick(player, tool, block: At(AreaTo));
        await World.Ticks(2);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToList();

        // Each tick, from the first plan on: the longest each spends neither striking nor walking to its own station
        // (the bar), and what it did meanwhile (each change of its state, where it was); and, for comparison, the
        // longest it goes with no block broken.
        double start = EidolonJobSystem.Now(W), end = start + Minutes * 60;
        double? planned = null;
        var mined = orders.Select(o => o.Mined).ToArray();
        var since = new double[orders.Count];
        var longest = new (double Seconds, double At, string Doing)[orders.Count];
        var unbroken = new (double Seconds, double At)[orders.Count];
        var idleSince = new double?[orders.Count];
        var gap = orders.Select(_ => new List<string>()).ToArray();
        int ticks = 0;
        string State(int i)
        {
            var parts = (orders[i].DebugState ?? "").Split(", ");
            return string.Join(", ", parts.Where(p => !p.StartsWith("nav") && !p.EndsWith(" -")).Select(p => p.Split(" at PathCell")[0]));
        }
        await World.Until(() =>
        {
            double now = EidolonJobSystem.Now(W);
            if (planned == null && orders[0].Job?.Shared.Plan != null)
            {
                planned = now;
                Array.Fill(since, now);
            }
            for (int i = 0; i < orders.Count; i++)
            {
                bool struck = orders[i].Mined != mined[i];
                if (struck)
                    (mined[i], since[i]) = (orders[i].Mined, now);
                else if (planned != null && now - since[i] > unbroken[i].Seconds)
                    unbroken[i] = (now - since[i], now - start);
                string debug = orders[i].DebugState ?? "";
                bool busy = struck || debug.StartsWith("Strike,")
                    || debug.StartsWith("Walk,") && !debug.Contains("head none");
                if (busy || planned == null)
                {
                    idleSince[i] = null;
                    gap[i].Clear();
                    continue;
                }
                idleSince[i] ??= now;
                string state = State(i);
                if (gap[i].Count == 0 || !gap[i][^1].EndsWith(state))
                {
                    var p = eidolons[i].Pos;
                    gap[i].Add($"{now - start:0} s at {p.X - corner.X:0},{p.Y - corner.Y:0},{p.Z - corner.Z:0}: {state}");
                }
                if (now - idleSince[i]!.Value > longest[i].Seconds)
                    longest[i] = (now - idleSince[i]!.Value, now - start, string.Join("; ", gap[i].TakeLast(12)));
            }
            if (++ticks % 600 == 0)
                Log($"{now - start:0} s: {string.Join(" + ", orders.Select(o => o.Mined))} mined; "
                    + string.Join(" | ", eidolons.Select(e => (e.Orders?.Current as MineOrder)?.DebugState)));
            return now >= end || eidolons.All(e => e.Orders?.OrderCode == null);
        }, 400_000);

        int total = orders.Sum(o => o.Mined);
        Log($"mined {string.Join(" + ", orders.Select(o => o.Mined))} = {total} in {Minutes} minutes (at least {Before * Faster:0}: {Faster} x main's {Before}); longest neither striking nor walking to its station: "
            + string.Join(", ", longest.Select((l, i) => $"{eidolons[i].EntityId} {l.Seconds:0.0} s to {l.At:0} s"))
            + "; longest with no block broken: " + string.Join(", ", unbroken.Select((l, i) => $"{eidolons[i].EntityId} {l.Seconds:0.0} s to {l.At:0} s")));
        for (int i = 0; i < orders.Count; i++)
            Log($"{eidolons[i].EntityId}'s longest: {longest[i].Doing}");
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
        Assert.NotNull(planned);
        Assert.True(total >= Before * Faster, $"{total} mined, under {Faster} x {Before}");
        Assert.All(longest, l => Assert.True(l.Seconds <= LongestGap, $"{l.Seconds:0.0} s neither striking nor walking to its station, to {l.At:0} s: {l.Doing}"));
    }
}
