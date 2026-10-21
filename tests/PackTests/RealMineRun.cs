using System.Text.RegularExpressions;
using Atlas.Api;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>Whether a member of a mining job is at work this tick: it broke a block, it is striking, or it walks to
/// its own planned station (its route's head, or the station dispatch gave it; <see cref="MineOrder.DebugState"/>'s
/// "Walk, ... head #n"). Waiting, parting, stopped short of another, or walking with no station of its own is not
/// (EidolonHillsideScenarios' bar, the owner's).</summary>
internal static class MineBusy
{
    public static bool Working(MineOrder order, bool struck)
    {
        string debug = order.DebugState ?? "";
        return struck || debug.StartsWith("Strike,") || debug.StartsWith("Walk,") && !debug.Contains("head none");
    }
}

/// <summary>
/// One realistic mining run (EidolonRealTerrain* scenarios): a crew spawned on the stands of a spot found on generated
/// ground (<see cref="RealTerrain"/>), bound to the command tool and ordered to mine its area as a player does, on the
/// virtual clock (<see cref="VirtualClock"/>), to the job's end or for a span of game time. Watched every tick, it holds
/// the owner's three bars:
/// <list type="bullet">
/// <item>Done means done: the job ends only with every block of the area the order mines gone but what its plan keeps
/// (out of reach, the floor under those, its way out), the report's counts those of the world scanned after it, its
/// members' shares the blocks seen broken by their strikes; and no block kept as out of reach is in reach of a fresh
/// bound over the world as it is left (<see cref="MineReach"/>, as MineHillsideTests make it). A timed run instead:
/// the job still running at its end, its shares those broken so far.</item>
/// <item>Red never grows: the job's count of blocks out of reach (<see cref="MineOrder.Unreachable"/>, what the owner
/// sees outlined in red) never rises from one tick to the next once the first plan is made; and no block a plan holds
/// out of reach is clearable by the bound over the world as it is when that plan comes in.</item>
/// <item>Everyone works: no member spends over <see cref="LongestIdle"/> s of game time at a time not at work
/// (<see cref="MineBusy"/>) while it has work left, and the job is never told stalled.</item>
/// </list>
/// Every failure says what each member was doing (its state changes over the gap) and where, the spot's coordinates
/// and the plan's counts.
/// </summary>
internal sealed class RealMineRun(IWorldSession world, Action<string> log)
{
    /// <summary>The longest a member may go at a time not at work while it has work left (the owner's bar).</summary>
    public const double LongestIdle = 25;

    /// <summary>How long none may break a block while the job runs before the run stops as stuck (game time).</summary>
    public const double StuckSeconds = 120;

    private IWorldAccessor W => world.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(world.Api)!;

    /// <summary>What a run found, for the scenario's own log line and asserts.</summary>
    public sealed record Outcome(int Blocks, int Mined, int[] Shares, double Seconds, bool Ended, int FirstOutOfReach, int LastOutOfReach,
        double[] LongestIdle, List<string> Faults, string? Report);

    private static void RightClick(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
    }

    /// <summary>Runs <paramref name="crew"/> on <paramref name="spot"/>: to the job's end, or for
    /// <paramref name="minutes"/> of game time when given. <paramref name="player"/>: the commander's name.</summary>
    public async Task<Outcome> Run(TerrainSpot spot, int crew, string player, double? minutes = null, int maxTicks = 60_000)
    {
        var area = spot.Area;
        var blocks = W.BlockAccessor;
        var faults = new List<string>();
        string where = $"{spot.Kind} {RealTerrain.Describe(area)} (seed {RealTerrain.Seed}), crew {crew}";

        // What the order mines of it, read as the planner reads it
        var pick = W.GetItem(new AssetLocation(EidolonMining.PickCode));
        var shovel = W.GetItem(new AssetLocation(EidolonMining.ShovelCode));
        var config = Mod.Config;
        int step = Math.Clamp(Math.Min(config.MaxClimbBlocks, config.MaxFallBlocks), 1, 2);
        var reader = new GameMineWorld(W, 0, area, pick, EidolonMining.PickTier(pick?.ToolTier), config.MineThroughBlockEntities, shovel);
        var cells = Cells(area);
        var toMine = cells.Where(c => !blocks.GetBlock(c).IsLiquid() && blocks.GetBlock(c).Id != 0).ToList();
        var codes = toMine.ToDictionary(c => c, c => blocks.GetBlock(c).Code.ToShortString());
        var solid = toMine.ToDictionary(c => c, c => GameMineWorld.Solid(blocks.GetBlock(c).GetCollisionBoxes(blocks, c)));
        var rules = cells.GroupBy(c => reader.Read(c.X, c.Y, c.Z).Rule).ToDictionary(g => g.Key, g => g.Count());
        log($"{where}: {toMine.Count} blocks; rules {string.Join(", ", rules.Select(r => $"{r.Key} {r.Value}"))}");

        var p = await world.JoinRetrying(player);
        var sp = (IServerPlayer)p.Player;
        sp.WorldData.CurrentGameMode = EnumGameMode.Creative;
        var first = spot.Stands[0];
        await p.TeleportTo(new BlockPos((int)first.X, (int)first.Y + 2, (int)first.Z));
        var tool = sp.InventoryManager.ActiveHotbarSlot;
        tool.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        tool.MarkDirty();

        using var clock = VirtualClock.Start(world);
        var eidolons = new List<EntityLaborEidolon>();
        foreach (var at in spot.Stands.Take(crew))
        {
            var e = Mod.Spawn(W, at, 0, sp, activate: true);
            Assert.NotNull(e);
            await world.Until(() => e!.CanWork, 400);
            eidolons.Add(e!);
        }
        foreach (var e in eidolons)
            RightClick(sp, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, sp, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(sp, tool, block: new BlockPos(area.Min.X, area.Min.Y, area.Min.Z));
        RightClick(sp, tool, block: new BlockPos(area.Max.X, area.Max.Y, area.Max.Z));
        await world.Ticks(2);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
        Assert.All(orders, o => Assert.Equal(area, o.Area));
        var job = orders[0].Job;
        Assert.NotNull(job);
        Assert.All(orders, o => Assert.Same(job, o.Job));

        var watch = new MineWatcher(W, toMine, eidolons.ToArray(), orders, floors: false);
        double start = EidolonJobSystem.Now(W), end = minutes is { } m ? start + m * 60 : double.MaxValue;
        double? planned = null;
        MinePlan? seen = null;
        int plans = 0, firstOut = -1, lastOut = -1, ticks = 0, lastMined = 0;
        var mined = orders.Select(o => o.Mined).ToArray();
        var idleSince = new double?[crew];
        var longest = new (double Seconds, double At, string Doing)[crew];
        // while idle, whether there was work it could take, asked once a second (the guard is not cheap)
        var could = new bool[crew];
        var askedAt = new double[crew];
        var unbusy = new double[crew];
        var unbusySince = new double?[crew];
        var couldTake = new string[crew];
        var gap = orders.Select(_ => new List<string>()).ToArray();
        string State(int i)
        {
            var parts = (orders[i].DebugState ?? "").Split(", ");
            return string.Join(", ", parts.Where(s => !s.StartsWith("nav") && !s.EndsWith(" -")).Select(s => s.Split(" at PathCell")[0]));
        }
        double? reportedAt = null;
        Dictionary<MarkPos, string>? atReport = null;
        // a job that stops: none breaks a block for this long (game time) while the job runs
        double lastBreak = start;
        string? stuck = null;
        await world.Until(() =>
        {
            double now = EidolonJobSystem.Now(W);
            watch.Tick();
            var shared = job!.Shared;
            if (shared.Plan is { } plan && plan != seen)
            {
                seen = plan;
                plans++;
                planned ??= now;
                log($"{now - start:0} s: plan {plans}: {plan.Stations.Count} stations, {plan.Targets.Count} to dig, {plan.OutOfReach.Count} out of reach, "
                    + $"{plan.Held.Count} kept under those, {plan.WayOut.Count} way out, {plan.Stair.Count} stair, tunnel {(plan.Tunnel is { } t ? $"{t.Covered.Count} columns from ({t.DriveX},{t.DriveZ})" : "none")}, "
                    + $"{plan.Reruns} reruns{(plan.Partial ? ", partial" : "")}");
                if (shared.Live is { } first && plans == 1)
                {
                    // the bound reaches every block the plan digs (else what it says of the rest means nothing)
                    var unreached = plan.Targets.Except(InReach(reader, area, step, first, plan.Targets.Concat(plan.OutOfReach).Concat(plan.Held), plan.Targets)).ToList();
                    if (unreached.Count > 0)
                        faults.Add($"plan {plans} digs {unreached.Count} the bound does not reach: " + string.Join(" ", unreached.Take(20)));
                }
                if (plan.OutOfReach.Count > 0 && shared.Live is { } live)
                {
                    if (Environment.GetEnvironmentVariable("SH_MINE_DUMP") is { } dumps)
                        Dump(Path.Combine(dumps, $"{spot.Kind}-{RealTerrain.Seed}-plan{plans}.mine"), area, step, live, shared, blocks);
                    var reachable = InReach(reader, area, step, live, plan.Targets.Concat(plan.OutOfReach).Concat(plan.Held), plan.OutOfReach);
                    if (reachable.Count > 0)
                        faults.Add($"plan {plans} at {now - start:0} s holds {reachable.Count} of its {plan.OutOfReach.Count} out of reach that the bound reaches: "
                            + string.Join(" ", reachable.Take(20)));
                }
            }
            if (planned != null)
            {
                int red = orders[0].Unreachable;
                if (firstOut < 0)
                    firstOut = red;
                else if (red > lastOut)
                    faults.Add($"out of reach rose from {lastOut} to {red} at {now - start:0} s (plan {plans}, {orders.Sum(o => o.Mined)} mined)");
                lastOut = red;
            }
            for (int i = 0; i < crew; i++)
            {
                bool struck = orders[i].Mined != mined[i];
                mined[i] = orders[i].Mined;
                bool counts = planned != null && orders[i].HasWorkLeft && !orders[i].Returning && orders[i].Reported == null
                    && eidolons[i].Orders?.OrderCode == MineOrder.OrderCode;
                bool working = MineBusy.Working(orders[i], struck);
                if (working || !counts)
                    unbusySince[i] = null;
                else if (now - (unbusySince[i] ??= now) > unbusy[i])
                    unbusy[i] = now - unbusySince[i]!.Value;
                if (!working && counts && (idleSince[i] == null || now - askedAt[i] >= 1))
                {
                    var free = FreeStations(i, eidolons, orders, job!.Shared, now);
                    could[i] = free.Count > 0;
                    var crosses = free.Select(f => CrossesClaims(orders[i], eidolons[i], f.Stand, now)).ToList();
                    couldTake[i] = free.Count == 0 ? "" : "could take " + string.Join(", ", free.Select((f, k) =>
                        $"#{f.Id} at ({f.Stand.X},{f.Stand.Y},{f.Stand.Z}){(crosses[k] ? " (its way crosses a claim)" : "")}"))
                        + (crosses.All(c => c) ? "; every free station's way crosses a claim" : "");
                    askedAt[i] = now;
                }
                if (working || !counts || !could[i])
                {
                    idleSince[i] = null;
                    gap[i].Clear();
                    continue;
                }
                idleSince[i] ??= now;
                string state = State(i) + "; " + couldTake[i];
                if (gap[i].Count == 0 || !gap[i][^1].EndsWith(state))
                {
                    var at = eidolons[i].Pos;
                    gap[i].Add($"{now - start:0} s at {at.X:0.0},{at.Y:0.0},{at.Z:0.0}: {state}");
                }
                if (now - idleSince[i]!.Value > longest[i].Seconds)
                    longest[i] = (now - idleSince[i]!.Value, now - start, string.Join("; ", gap[i].TakeLast(12)));
            }
            if (reportedAt == null && orders.Any(o => o.Reported != null))
            {
                reportedAt = now;
                // the area as the report counted it: what comes into it after (a vine growing down onto the dug floor)
                // is no work left undone
                atReport = Cells(area).Where(c => blocks.GetBlock(c) is { Id: not 0 } b && !b.IsLiquid())
                    .ToDictionary(c => new MarkPos(c.X, c.Y, c.Z), c => blocks.GetBlock(c).Code.ToShortString());
            }
            if (orders.Sum(o => o.Mined) != lastMined)
                (lastMined, lastBreak) = (orders.Sum(o => o.Mined), now);
            else if (reportedAt == null && now - lastBreak > StuckSeconds)
            {
                stuck = Stuck(eidolons, orders, job!.Shared, now - start);
                return true;
            }
            // reported: each walks back and its order ends
            if (reportedAt is { } told && now - told > StuckSeconds && eidolons.Any(e => e.Orders?.OrderCode != null))
            {
                stuck = $"{StuckSeconds} s after the report, still on their orders: " + string.Join(" | ", eidolons.Where(e => e.Orders?.OrderCode != null)
                    .Select(e => $"{e.EntityId} at {e.Pos.X:0.00},{e.Pos.Y:0.00},{e.Pos.Z:0.00}: {(e.Orders?.Current as MineOrder)?.DebugState}"))
                    + "; the others at " + string.Join(", ", eidolons.Where(e => e.Orders?.OrderCode == null).Select(e => $"{e.EntityId} {e.Pos.X:0.00},{e.Pos.Y:0.00},{e.Pos.Z:0.00}"));
                return true;
            }
            if (++ticks % 900 == 0)
                log($"{now - start:0} s: {string.Join(" + ", orders.Select(o => o.Mined))} mined, {orders[0].Unreachable} out of reach; "
                    + string.Join(" | ", orders.Select((o, i) => eidolons[i].Orders?.OrderCode == null ? "order ended" : o.DebugState)));
            return now >= end || eidolons.All(e => e.Orders?.OrderCode == null);
        }, maxTicks);
        double seconds = (reportedAt ?? EidolonJobSystem.Now(W)) - start;
        bool ended = eidolons.All(e => e.Orders?.OrderCode == null);
        if (stuck != null)
            faults.Add(stuck);
        var shares = orders.Select(o => o.Mined).ToArray();
        int total = shares.Sum();
        string? report = orders.Select(o => o.Reported).OfType<string>().SingleOrDefault();
        log($"{where}: {string.Join(" + ", shares)} = {total} of {toMine.Count} mined in {seconds:0} s of game time, {plans} plans, "
            + $"out of reach {firstOut} first, {lastOut} last; {(ended ? "ended" : "still running")}; report: {report ?? "none"}"
            + (orders.Select(o => o.ReportedWhy).OfType<string>().FirstOrDefault() is { } why ? $"; why: {why}" : ""));
        log("longest not at work with work it could take: " + string.Join(", ", longest.Select((l, i) => $"{eidolons[i].EntityId} {l.Seconds:0.0} s to {l.At:0} s"))
            + "; longest not at work at all: " + string.Join(", ", unbusy.Select((u, i) => $"{eidolons[i].EntityId} {u:0.0} s")));
        for (int i = 0; i < crew; i++)
            if (longest[i].Seconds > 0)
                log($"{eidolons[i].EntityId}'s longest: {longest[i].Doing}");
        log($"contacts: {watch.Summary()}; worked by {string.Join(" then ", watch.Choices)}; watcher's faults: {string.Join(" | ", watch.Faults.Take(10))}");

        // Everyone works
        for (int i = 0; i < crew; i++)
            if (longest[i].Seconds > LongestIdle)
                faults.Add($"{eidolons[i].EntityId} {longest[i].Seconds:0.0} s not at work with work it could take, to {longest[i].At:0} s: {longest[i].Doing}");
        foreach (var o in orders)
            if (o.ToldStalled is { } stalled)
                faults.Add($"told stalled: {stalled}");
        // The shares are the blocks gone but what the game took by itself, which the job counts (MineJobCells.Gone: a
        // plant or vine whose footing a strike broke, rock left hanging that caved in, sand and gravel that fell), and
        // what came to be mined in the area meanwhile, which it counts too (MineJobCells.Appeared: grass grown on dug
        // ground, sand or gravel fallen into a dug cell, mined there again).
        var gone = toMine.Where(c => blocks.GetBlock(c).Id == 0).ToList();
        var others = gone.Except(watch.Broke.Values.SelectMany(b => b)).ToList();
        int byGame = job!.Shared.Gone, appeared = job.Shared.Appeared;
        log($"{gone.Count} gone, {total} mined, {byGame} gone by the game's own rules and {appeared} come to be mined (the job's counts); {others.Count} with no strike seen: "
            + string.Join(" ", others.Take(20).Select(c => $"{c} ({codes[c]}{(solid[c] ? "" : ", not solid")})")));
        if (gone.Count - total > byGame || total - gone.Count > byGame + appeared)
            faults.Add($"{total} mined by the shares, {gone.Count} of the area's blocks gone: more apart than the {byGame} the job counts gone by the game"
                + $" (and {appeared} come to be mined)");

        if (minutes == null)
        {
            if (!ended && stuck == null)
                faults.Add($"the job had not ended after {seconds:0} s ({maxTicks} ticks)");
            else
                Finished(area, reader, step, job!, orders, atReport, total, report, faults);
        }
        else if (ended)
            faults.Add($"the job ended at {seconds:0} s, before its {minutes} minutes: {report}");

        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
        return new Outcome(toMine.Count, total, shares, seconds, ended, firstOut, lastOut, longest.Select(l => l.Seconds).ToArray(), faults, report);
    }

    // Done means done: the world scanned after the job, against what it keeps and what it reported.
    private void Finished(MarkArea area, GameMineWorld reader, int step, SharedJob<MineJobCells> job, MineOrder[] orders,
        Dictionary<MarkPos, string>? atReport, int total, string? report, List<string> faults)
    {
        var blocks = W.BlockAccessor;
        var shared = job.Shared;
        var plan = shared.Plan!;
        var kept = orders[0].Kept;
        int left = 0, unreachable = 0, held = 0, wayOut = 0;
        var stillOut = new List<MarkPos>();
        var since = new List<string>();
        foreach (var c in Cells(area))
        {
            var block = blocks.GetBlock(c);
            if (block.Id == 0 || block.IsLiquid())
                continue;
            var cell = new MarkPos(c.X, c.Y, c.Z);
            if (atReport != null && !atReport.ContainsKey(cell))
            {
                since.Add($"({c.X},{c.Y},{c.Z}) {block.Code.ToShortString()}");
                continue;
            }
            if (shared.Stair.Contains(cell))
                wayOut++;
            else if (plan.OutOfReach.Contains(cell))
            {
                unreachable++;
                stillOut.Add(cell);
            }
            else if (plan.Held.Contains(cell))
                held++;
            else
                left++;
        }
        // what the planner would dig of it now (EidolonMining.Targets over a fresh read: the order's blocks but the
        // columns held up under one it leaves, those under the out of reach kept): none but its way out
        var site = new MineSite(area, step, shared.Live?.Site.Window);
        foreach (var _ in site.Load(reader)) { }
        var now = EidolonMining.Targets(area, site, shared.Skip.Contains, plan.OutOfReach);
        var notDone = now.Dig.Where(c => !kept.Contains(c) && atReport?.ContainsKey(c) != false).ToList();
        if (since.Count > 0)
            log($"{since.Count} in the area since the report, none counted: {string.Join(" ", since)}");
        log($"after: {left} left as they were ({now.Left.Count} of those it leaves or held up under one), {unreachable} out of reach, {held} kept under those, "
            + $"{wayOut} way out; {notDone.Count} it would dig still standing");
        if (notDone.Count > 0)
            faults.Add($"done with {notDone.Count} it would dig still standing, none kept: " + string.Join(" ", notDone.Take(30)));
        if (report == null)
            faults.Add("ended with no report");
        else if (Regex.Match(report, @"\((\d+) blocks mined.*?(\d+) left as they were, (\d+) out of (?:its|their) reach, (\d+) kept under those as their floor, (\d+) left as (?:its|their) way out\)") is not { Success: true } r)
            faults.Add($"report not read: {report}");
        else
        {
            var told = r.Groups.Values.Skip(1).Select(g => int.Parse(g.Value)).ToArray();
            int[] world = [total, left, unreachable, held, wayOut];
            if (!told.SequenceEqual(world))
                faults.Add($"report says {string.Join(", ", told)} (mined, left, out of reach, kept, way out), the world {string.Join(", ", world)}");
        }
        if (orders.Select(o => o.ReportedWhy).OfType<string>().FirstOrDefault() is { } why)
            faults.Add($"reported blocks still to dig: {why}");
        // what is left out of reach is out of reach of any order, the world as it is now
        if (stillOut.Count > 0 && shared.Live is { } live)
        {
            var reachable = InReach(reader, area, step, live, stillOut.Concat(plan.Held), stillOut);
            if (reachable.Count > 0)
                faults.Add($"{reachable.Count} of the {stillOut.Count} left out of reach are in the bound's reach: " + string.Join(" ", reachable.Take(20)));
        }
    }

    // What a stuck job was doing: each member, the job's stall, what is left to dig and what dispatch makes of it.
    private string Stuck(List<EntityLaborEidolon> eidolons, MineOrder[] orders, MineJobCells shared, double at)
    {
        var parts = new List<string> { $"no block broken for {StuckSeconds} s, to {at:0} s (deadlock?)" };
        for (int i = 0; i < eidolons.Count; i++)
            parts.Add($"{eidolons[i].EntityId} at {eidolons[i].Pos.X:0.00},{eidolons[i].Pos.Y:0.00},{eidolons[i].Pos.Z:0.00} "
                + $"({eidolons[i].Orders?.OrderCode}, work left {orders[i].HasWorkLeft}, returning {orders[i].Returning}, reported {orders[i].Reported != null}): "
                + $"{orders[i].DebugState}");
        parts.Add($"told stalled: {orders.Select(o => o.ToldStalled).OfType<string>().FirstOrDefault() ?? "no"}");
        if (shared.Live is { } live && shared.Plan is { } plan)
        {
            parts.Add($"{live.Remaining.Count} still to dig: {string.Join(" ", live.Remaining.Take(20).Select(c => $"({c.X},{c.Y},{c.Z})"))}");
            var at2 = eidolons.Select(e => new MineWorkerAt(e.EntityId, e.Pos.X, (int)Math.Floor(e.Pos.Y + 0.01), e.Pos.Z, [])).ToList();
            var workable = MineDispatch.Workable(plan, live);
            parts.Add($"workable with the others out of the way: {(workable is { } w ? $"#{w.Id} at ({w.Stand.X},{w.Stand.Y},{w.Stand.Z}), cells {string.Join(" ", MineDispatch.Cells(live, w).Select(c => $"({c.X},{c.Y},{c.Z})"))}" : "none")}; "
                + $"with them where they stand: {(MineDispatch.Workable(plan, live, at2) is { } v ? $"#{v.Id}" : "none")}; "
                + $"why left: {string.Join(", ", MineDispatch.WhyLeft(plan, live, live.Remaining, at2).Select(k => $"{k.Value} {k.Key}"))}");
            for (int i = 0; i < eidolons.Count; i++)
                parts.Add($"{eidolons[i].EntityId} could take: {(FreeStations(i, eidolons, orders, shared, W.ElapsedMilliseconds / 1000.0).FirstOrDefault() is { } c ? $"#{c.Id} at ({c.Stand.X},{c.Stand.Y},{c.Stand.Z})" : "none")}");
        }
        return string.Join(" | ", parts);
    }

    /// <summary>Stations member <paramref name="i"/> could take now (the first <paramref name="most"/>): the plan's with cells left that
    /// passes the guard with the others where they stand (<see cref="MineDispatch.Executable"/>), none of the others'
    /// own (the station each works or walks to), its stand under none of them, its claim's columns held by none of them
    /// on the job board (README "Eidolon", working together, *Claims*). What the dispatch's order or the routes would
    /// give it first is not asked, nor whether its way crosses another's claim (a station reachable at all is work:
    /// crossing a claim is how members taking turns showed; <see cref="CrossesClaims"/> says which for the log): only whether there is work for it.</summary>
    private List<MineStation> FreeStations(int i, List<EntityLaborEidolon> eidolons, MineOrder[] orders, MineJobCells shared, double now,
        int most = 6)
    {
        var free = new List<MineStation>();
        if (shared.Plan is not { } plan || shared.Live is not { } live)
            return free;
        var board = EidolonJobSystem.Of(world.Api)!.Mining;
        var others = new List<MineWorkerAt>();
        for (int j = 0; j < eidolons.Count; j++)
            if (j != i)
                others.Add(new MineWorkerAt(eidolons[j].EntityId, eidolons[j].Pos.X, (int)Math.Floor(eidolons[j].Pos.Y + 0.01), eidolons[j].Pos.Z,
                    orders[j].Station is { } s ? [s.Stand] : []));
        var theirs = orders.Where((_, j) => j != i).Select(o => o.Station?.Id).OfType<int>().ToHashSet();
        foreach (var station in plan.Stations)
        {
            if (theirs.Contains(station.Id) || MineDispatch.Cells(live, station).Count == 0)
                continue;
            if (others.Any(o => Math.Abs(o.X - station.Stand.X) < 2 * EidolonMining.HalfWidth && Math.Abs(o.Z - station.Stand.Z) < 2 * EidolonMining.HalfWidth
                                && Math.Abs(o.Feet - station.Stand.Y) < 4))
                continue;
            if (MineDispatch.Claim(station, plan.Tunnel).Any(c => board.Holder(c, now) is { } h && h != eidolons[i].EntityId))
                continue;
            if (MineDispatch.Executable(plan, live, station, others, out _) && free.Count < most)
                free.Add(station);
        }
        return free;
    }

    private static readonly System.Reflection.MethodInfo CrossesClaimsMethod =
        typeof(MineOrder).GetMethod("CrossesClaims", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MineOrder.CrossesClaims (the dispatch's claim crossing) is gone: find it again for RealMineRun");

    /// <summary>Whether the member's way to <paramref name="stand"/> crosses a column another holds, as its own dispatch
    /// asks it (MineOrder.CrossesClaims, private: the game's path found, its first steps off): a station set aside for
    /// it (README "Eidolon", working together) is told apart from one free and clear. For the log only.</summary>
    private static bool CrossesClaims(MineOrder order, EntityLaborEidolon eidolon, PathCell stand, double now) =>
        (bool)CrossesClaimsMethod.Invoke(order, [eidolon, stand, now])!;

    /// <summary>A plan's input as the job's live state holds it, in the unit tests' fixture format
    /// (mods-src/seraphhorizons/tests/Eidolon/fixtures/*.mine, <c>MineTestWorld.FromDump</c>), for one that holds cells out of
    /// reach to be planned again there: its area, window, climb, home and bad stands, the window's blocks a column a line
    /// from its bottom up as runs of <c>count:code</c> (code: rule × 2 + solid), and the cells over the area that roof it.</summary>
    private static void Dump(string path, MarkArea area, int step, MineState live, MineJobCells shared, IBlockAccessor blocks)
    {
        var w = live.Site.Window;
        var lines = new List<string>
        {
            $"area {area.Min.X} {area.Min.Y} {area.Min.Z} {area.Max.X} {area.Max.Y} {area.Max.Z}",
            $"window {w.Min.X} {w.Min.Y} {w.Min.Z} {w.Max.X} {w.Max.Y} {w.Max.Z}",
            $"step {step}",
        };
        if (live.Home is { } h)
            lines.Add($"home {h.X} {h.Y} {h.Z}");
        lines.AddRange(shared.BadStands.Select(b => $"bad {b.X} {b.Y} {b.Z}"));
        for (int x = w.Min.X; x <= w.Max.X; x++)
        for (int z = w.Min.Z; z <= w.Max.Z; z++)
        {
            var runs = new List<string>();
            int last = -1, n = 0;
            for (int y = w.Min.Y; y <= w.Max.Y; y++)
            {
                int code = (int)live.Site.Rule(new MarkPos(x, y, z)) * 2 + (live.Site.Solid(x, y, z) ? 1 : 0);
                if (code == last)
                    n++;
                else
                {
                    if (last >= 0)
                        runs.Add($"{n}:{last}");
                    (last, n) = (code, 1);
                }
            }
            runs.Add($"{n}:{last}");
            if (runs.Count > 1 || last != 0)
                lines.Add($"c {x} {z} {string.Join(" ", runs)}");
        }
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            var roofs = Enumerable.Range(area.Max.Y + 1, EidolonTunnels.RoofRise).Where(y => RealTerrain.Roof(blocks.GetBlock(new BlockPos(x, y, z)))).ToList();
            if (roofs.Count > 0)
                lines.Add($"r {x} {z} {string.Join(" ", roofs)}");
        }
        File.WriteAllLines(path, lines);
    }

    /// <summary>Those of <paramref name="check"/> a fresh reach bound (<see cref="MineReach"/>) over the world as it is
    /// now clears: the window the job's live state reads, its home and bad stands, over <paramref name="targets"/>
    /// that the order mines.</summary>
    private static List<MarkPos> InReach(GameMineWorld reader, MarkArea area, int step, MineState live, IEnumerable<MarkPos> targets,
        IEnumerable<MarkPos> check)
    {
        var site = new MineSite(area, step, live.Site.Window);
        foreach (var _ in site.Load(reader)) { }
        var dig = targets.Where(c => site.Rule(c) == MineRule.Mine).ToHashSet();
        var state = new MineState(site, live.Home, live.BadStands);
        state.BuildNow(dig);
        return check.Where(state.Reach.Clearable.Contains).ToList();
    }

    public static List<BlockPos> Cells(MarkArea area)
    {
        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));
        return cells;
    }
}
