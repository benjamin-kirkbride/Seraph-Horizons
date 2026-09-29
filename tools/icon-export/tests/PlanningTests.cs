using SeraphHorizons.IconExport.Core;

namespace SeraphHorizons.IconExport.Tests;

public class PlanningTests
{
    private static RenderTarget Item(string code) => new(code, IconKind.Item);

    [Fact]
    public void A_duplicate_target_renders_once()
    {
        Plan plan = ExportPlanner.Build(new[] { Item("game:a"), Item("game:b"), Item("game:a") }, _ => false, force: false);
        Assert.Equal(new[] { "game/item/a.png", "game/item/b.png" }, plan.ToRender.Select(j => j.RelativePath));
        Assert.Equal(1, plan.Duplicates);
    }

    [Fact]
    public void A_duplicate_in_the_list_file_renders_once()
    {
        // The whole path from a list file with a repeated code to the jobs.
        ListResult list = ListFile.ParseText("game:stick\ngame:flint\ngame:stick\n");
        Plan plan = ExportPlanner.Build(list.Entries.Select(e => Item(e.Code)), _ => false, force: false);
        Assert.Equal(new[] { "game:stick", "game:flint" }, plan.ToRender.Select(j => j.Code));
    }

    [Fact]
    public void Resume_skips_files_that_exist()
    {
        var existing = new HashSet<string> { "game/item/b.png" };
        Plan plan = ExportPlanner.Build(new[] { Item("game:a"), Item("game:b"), Item("game:c") }, existing.Contains, force: false);
        Assert.Equal(new[] { "game:a", "game:c" }, plan.ToRender.Select(j => j.Code));
        Assert.Equal(new[] { "game:b" }, plan.SkippedExisting.Select(j => j.Code));
    }

    [Fact]
    public void Force_renders_files_that_exist()
    {
        var existing = new HashSet<string> { "game/item/b.png" };
        Plan plan = ExportPlanner.Build(new[] { Item("game:a"), Item("game:b") }, existing.Contains, force: true);
        Assert.Equal(new[] { "game:a", "game:b" }, plan.ToRender.Select(j => j.Code));
        Assert.Empty(plan.SkippedExisting);
    }

    [Fact]
    public void Existence_is_asked_with_the_relative_path()
    {
        var asked = new List<string>();
        ExportPlanner.Build(new[] { new RenderTarget("game:clutter-art/bottle", IconKind.Block) }, p => { asked.Add(p); return false; }, false);
        Assert.Equal(new[] { "game/block/clutter-art/bottle.png" }, asked);
    }

    private static List<RenderJob> Jobs(int n) =>
        Enumerable.Range(0, n).Select(i => new RenderJob($"game:j{i}", IconKind.Item, $"game/item/j{i}.png")).ToList();

    [Fact]
    public void Time_budget_ends_the_frame()
    {
        // Each render takes 10 ms of a 25 ms budget: after 3 renders 30 ms have passed.
        var s = new FrameScheduler(Jobs(10), maxPerFrame: 40, maxMsPerFrame: 25);
        double t = 0;
        int n = s.RunFrame(() => t, _ => t += 10);
        Assert.Equal(3, n);
        Assert.Equal(3, s.Next);
    }

    [Fact]
    public void Count_limit_ends_the_frame()
    {
        var s = new FrameScheduler(Jobs(10), maxPerFrame: 4, maxMsPerFrame: 25);
        var done = new List<string>();
        Assert.Equal(4, s.RunFrame(() => 0, j => done.Add(j.Code)));
        Assert.Equal(4, s.RunFrame(() => 0, j => done.Add(j.Code)));
        Assert.Equal(2, s.RunFrame(() => 0, j => done.Add(j.Code)));
        Assert.True(s.Done);
        Assert.Equal(0, s.RunFrame(() => 0, j => done.Add(j.Code)));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"game:j{i}"), done);
    }

    [Fact]
    public void A_slow_item_still_moves_on_one_per_frame()
    {
        var s = new FrameScheduler(Jobs(3), maxPerFrame: 40, maxMsPerFrame: 25);
        double t = 0;
        Assert.Equal(1, s.RunFrame(() => t, _ => t += 100));
        t = 0;
        Assert.Equal(1, s.RunFrame(() => t, _ => t += 100));
    }

    [Fact]
    public void A_frame_already_over_budget_still_renders_one()
    {
        // Without this the export would stall for good on a slow machine.
        var s = new FrameScheduler(Jobs(3), maxPerFrame: 40, maxMsPerFrame: 25);
        Assert.Equal(1, s.RunFrame(() => 30, _ => { }));
        Assert.Equal(1, s.RunFrame(() => 1000, _ => { }));
        Assert.Equal(2, s.Next);
    }

    [Fact]
    public void Interval_timer()
    {
        var timer = new IntervalTimer(5000, startMs: 0);
        Assert.False(timer.Due(4999));
        Assert.True(timer.Due(5000));
        Assert.False(timer.Due(9999));
        Assert.True(timer.Due(10000));
    }

    [Fact]
    public void Untextured_warning_needs_more_than_a_tenth()
    {
        Assert.False(new RunTotals { Written = 10, Untextured = 1 }.MostlyBroken);
        Assert.True(new RunTotals { Written = 10, Untextured = 2 }.MostlyBroken);
        Assert.False(new RunTotals { Written = 0, Untextured = 0 }.MostlyBroken);
    }

    [Fact]
    public void Summary_names_every_total()
    {
        var t = new RunTotals { Written = 5, SkippedExisting = 4, Failed = 3, Transparent = 2, Untextured = 1 };
        Assert.Equal("written 5, skipped as existing 4, failed 3, fully transparent 2, untextured-looking 1", t.Summary());
    }

    [Fact]
    public void Inflight_marker_round_trip()
    {
        var job = new RenderJob("tankardsandgoblets:t&g-winebottle-blue", IconKind.Block, "unused");
        string text = InflightMarker.Format(new[] { job });
        Assert.Equal("block tankardsandgoblets:t&g-winebottle-blue\n", text);
        Assert.Equal(new[] { new RenderTarget(job.Code, IconKind.Block) }, InflightMarker.Parse(text + "garbage\n\nitem nocolon\n"));
    }
}
