using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Routes against today's dispatch (README "Eidolon", working together, *Routes*): each case of
/// <see cref="MineCases"/> worked by <see cref="TeamRunner"/> as the game does today (it waits while its way
/// crosses another's claim or another eidolon, up to 20 s, and goes round one where there is room) and by
/// <see cref="RouteRunner"/>, timed alike, with two and three eidolons, as estimated and averaged over strikes
/// jittered by seeds 7, 11 and 13. The planner leaves a job to today's dispatch where that finishes no later in
/// its simulations, so the owner's targets are: a makespan no later than today's everywhere; in the open pit and
/// the trench with three, 10 % under it with less idling; contacts rare and brief (at most about one
/// a member each 2 minutes of work, none over 2 s, none with one on a narrow way) and no fault. Today's
/// contacts are held to the same 2 s, and both runners then walk every member home at once
/// (<see cref="HomeWalk"/>, the game's returners stopping short of one in their way whatever the dispatch,
/// #1329), every one getting there with no touch over 2 s; the walk home is not in the makespans.</summary>
public class MineRouteBenchmarkTests(ITestOutputHelper output)
{
    private sealed record Result(double Makespan, double Idle, double Walked, int Contacts, string Extra)
    {
        public override string ToString() => $"makespan {Makespan,6:0.0} s, idle {Idle,6:0.0} s, walked {Walked,5:0}, contacts {Contacts}{Extra}";
    }

    private static readonly int?[] Seeds = [null, 7, 11, 13];

    private static Result Team(MineCase c, int? jitter, bool crossing = true)
    {
        var world = c.World();
        var plan = c.Plan(world);
        var team = new TeamRunner(world, c.Area, plan, c.Home, c.Starts, jitter, crossing);
        team.Run(200_000);
        Assert.True(team.AllLeave(c.Home), "today's: all leave");
        var home = team.WalkHome();
        if (crossing)
        {
            // contacts brief, as by routes, and so the walk home
            Assert.True(team.ContactSeconds <= 2, $"today's: a contact of {team.ContactSeconds:0.0} s");
            AssertHome("today's", home);
        }
        return new Result(team.Makespan, team.Idle.Sum(), team.Walked.Sum(), team.Contacts, $"; {home}");
    }

    private Result Routes(MineCase c, int? jitter)
    {
        var world = c.World();
        var plan = c.Plan(world);
        var runner = new RouteRunner(world, c.Area, plan, c.Home, c.Starts, jitter);
        try
        {
            runner.Run(20_000);
        }
        catch
        {
            foreach (var line in runner.Log.TakeLast(80))
                output.WriteLine(line);
            throw;
        }
        finally
        {
            // MINE_ROUTE_LOG=<dir>: each run's log written there, to see what the members did
            if (Environment.GetEnvironmentVariable("MINE_ROUTE_LOG") is { Length: > 0 } dir)
                File.WriteAllLines(Path.Combine(dir, $"{c.Name.Replace(' ', '-')}-{c.Starts.Length}{(jitter != null ? $"-jitter{jitter}" : "")}.log"), runner.Log);
        }
        // no fault (the runner asserts each strike), all leave, everything dug but the stair's way out
        Assert.True(runner.AllLeave(c.Home), "routes: all leave");
        // one choice the whole job, across its replans too (the crew and area never change here)
        Assert.All(runner.Choices, x => Assert.True(x.Choice == runner.Choices[0].Choice, $"chose {x.Choice} ({x.Why}) after {runner.Choices[0].Choice}"));
        Assert.DoesNotContain(runner.Live.Remaining, cell => !plan.WayOut.Contains(cell));
        // contacts rare and brief
        Assert.True(runner.ContactSeconds <= 2, $"a contact of {runner.ContactSeconds:0.0} s");
        foreach (var (contacts, work) in runner.ContactRates)
            Assert.True(contacts <= 1 + work / 120, $"{contacts} contacts in {work:0} s of work");
        var home = runner.WalkHome();
        AssertHome("routes", home);
        return new Result(runner.Makespan, runner.Idle.Sum(), runner.Walked.Sum(), runner.Contacts,
            $"; {home}, chosen {runner.Chosen}, across claims {runner.Crossings}, solves {runner.Solves} (kept {runner.Kept}), repairs {runner.Repairs}, ahead {runner.AheadStops}, replans {runner.Replans}; idle by: {runner.IdleWhy}");
    }

    // the walk home (HomeWalk): every member home, two boxes never in each other's over 2 s on the way
    private static void AssertHome(string runner, HomeWalk home)
    {
        Assert.True(home.AllHome, $"{runner}: all walk home");
        Assert.True(home.ContactSeconds <= 2, $"{runner}: a contact of {home.ContactSeconds:0.0} s on the walk home");
    }

    // the open pit and the trench with three: 10 % under today's, with less idling
    private static bool MustGain(string geometry, int crew) => geometry is "G1" or "G4" && crew == 3;

    private void Check(string label, string geometry, int crew, Result today, Result routes)
    {
        output.WriteLine($"{label}: today  {today}");
        output.WriteLine($"{label}: routes {routes}");
        Assert.True(routes.Makespan <= today.Makespan + 1e-6, $"{label}: routes' makespan {routes.Makespan:0.0} s over today's {today.Makespan:0.0} s");
        if (MustGain(geometry, crew))
        {
            Assert.True(routes.Makespan <= 0.90 * today.Makespan, $"{label}: routes' makespan {routes.Makespan:0.0} s not 10 % under today's {today.Makespan:0.0} s");
            Assert.True(routes.Idle < today.Idle, $"{label}: routes idle {routes.Idle:0.0} s against today's {today.Idle:0.0} s");
        }
    }

    [Theory]
    [InlineData("G1", 2)]
    [InlineData("G1", 3)]
    [InlineData("G2", 2)]
    [InlineData("G2", 3)]
    [InlineData("G3", 2)]
    [InlineData("G3", 3)]
    [InlineData("G4", 2)]
    [InlineData("G4", 3)]
    [InlineData("G5", 2)]
    [InlineData("G5", 3)]
    public void Routes_meet_the_owner_s_targets(string geometry, int crew)
    {
        var c = MineCases.ByName(geometry, crew);
        Check($"{geometry} x{crew}", geometry, crew, Team(c, null), Routes(c, null));
        // and averaged over jittered strikes
        var today = Seeds.Select(s => Team(c, s)).ToList();
        var routes = Seeds.Select(s => Routes(c, s)).ToList();
        Check($"{geometry} x{crew} averaged", geometry, crew, Mean(today), Mean(routes));
    }

    private static Result Mean(List<Result> runs) =>
        new(runs.Average(r => r.Makespan), runs.Average(r => r.Idle), runs.Average(r => r.Walked), runs.Sum(r => r.Contacts), "");

    /// <summary>Every case, the skipped ones too, as a table: today's runner walking through others' claims and
    /// eidolons ("through"), today's as the game does it, and routes; as estimated, and averaged over the
    /// jittered seeds. Run when MINE_ROUTE_TABLE=1 (it takes minutes); the theory above covers the cases it
    /// asserts on.</summary>
    [Fact]
    public void The_table_of_routes_against_today_s_dispatch()
    {
        // two minutes in Debug: run on asking only (MINE_ROUTE_TABLE=1)
        if (Environment.GetEnvironmentVariable("MINE_ROUTE_TABLE") != "1")
        {
            output.WriteLine("set MINE_ROUTE_TABLE=1 to make the table");
            return;
        }
        output.WriteLine("case   runner     makespan     idle  walked  contacts | averaged: makespan     idle  walked  contacts");
        foreach (string g in new[] { "G1", "G2", "G3", "G4", "G5" })
        foreach (int crew in new[] { 2, 3 })
        {
            var c = MineCases.ByName(g, crew);
            foreach (var (runner, run) in new (string, Func<int?, Result>)[]
                     {
                         ("through", s => Team(c, s, crossing: false)), ("today", s => Team(c, s)), ("routes", s => Routes(c, s)),
                     })
            {
                var once = run(null);
                var mean = Mean(Seeds.Select(run).ToList());
                output.WriteLine($"{g} x{crew}  {runner,-8} {once.Makespan,8:0.0} {once.Idle,8:0.0} {once.Walked,7:0} {once.Contacts,9} | "
                    + $"{mean.Makespan,18:0.0} {mean.Idle,8:0.0} {mean.Walked,7:0} {mean.Contacts,9}");
            }
        }
    }
}
