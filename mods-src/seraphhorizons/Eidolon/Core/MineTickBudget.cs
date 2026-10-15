using System.Diagnostics;

namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// The mine planner's main-thread share of a tick (<see cref="MinePlanner.BudgetMs"/> across every job):
/// what it reads of the world there (a job's window a block column at a time, its roofs, the sweep for
/// changes) goes on while <see cref="Take"/> says so. In the game it is wall time; with
/// <paramref name="reads"/> it is that many blocks read a tick instead, the same on every machine (the
/// tests' virtual clock: a plan then starts on the same tick of game time every run).
/// </summary>
public sealed class MineTickBudget(double ms, int? reads = null)
{
    private readonly Stopwatch _clock = new();
    private long _read;

    /// <summary>Blocks read a tick, or null for <see cref="Ms"/> of wall time.</summary>
    public int? Reads { get; } = reads is null or > 0 ? reads : throw new ArgumentOutOfRangeException(nameof(reads), reads, "at least one block a tick");

    public double Ms { get; } = ms;

    /// <summary>A new tick: the whole budget again.</summary>
    public void Restart()
    {
        _read = 0;
        _clock.Restart();
    }

    /// <summary>Whether this tick's budget is used up.</summary>
    public bool Spent => Reads is { } n ? _read >= n : _clock.Elapsed.TotalMilliseconds >= Ms;

    /// <summary>Before a piece of work reading <paramref name="blocks"/> blocks: false if the budget is used
    /// up (the work waits for the next tick), else true, the blocks counted against it. A piece is never
    /// cut, so a tick may go past its budget by at most one piece.</summary>
    public bool Take(int blocks)
    {
        if (Spent)
            return false;
        _read += blocks;
        return true;
    }
}
