# PackTests

Atlas scenarios for the whole pack: a headless Vintage Story server booted in-process with every locked
mod (`CONTRIBUTING.md` has how to run them; `.github/workflows/ci.yml` how CI shards them).

## The virtual clock

Atlas runs the embedded server in real time: a 140 s eidolon dig takes 140 s of wall time, and when a slow
runner's passes fall behind, anything timed on the server's clock stretches against anything stepped per
tick. `VirtualClock` (#960) runs the server faster than real time, the same way on every run: each engine
pass moves the server's clock on by exactly one nominal tick (33.3 ms) and does not sleep, about 20–25×
faster than real time.

### Opting in

Start it in the scenario body, after the world is set up, with `using`:

```csharp
[AtlasScenario]
public async Task It_digs_the_pit()
{
    var site = await Felling.Sky(World, origin, reach: 10);   // set-up, chunk loading: real time
    // ... place blocks, join the player, spawn the eidolon ...
    using var clock = VirtualClock.Start(World);
    // ... give the order, await World.Until(...) ...
}
```

- **Dispose always goes back to real time**, the clock going on from the virtual one's time, also when an
  assertion throws. A scenario that ends with a clock still on (no `using`) fails, and the clock is stopped
  (`VirtualClockGuardAttribute`, after every scenario), so it never leaks into the next one. Clocks don't
  nest: a second `Start` throws.
- **Boot, chunk generation and rollbacks stay real-time**: they happen outside the `using`. A pass while the
  server is suspended (autosave, rollback) does not advance the clock.
- **Start fails loudly** (`NotSupportedException`) if an engine member it reaches by reflection, or the
  pattern its transpiler looks for in `ServerMain.Process`, is missing, as a game or .NET update can do. Find
  them again by decompiling `VintagestoryLib.dll` (`ilspycmd -t Vintagestory.Server.ServerMain ...`) and
  update `VirtualClock.Engine`.
- **Its Harmony patch is checked at every `Start`, and put back if gone.** MadMechanics, MadFoundation and
  MadHoarding call `Harmony.UnpatchAll(null)` when a server stops, which takes every patch in the process
  off, the clock's too. Patched only once, a later class's clock would stop the server's clock for good and
  hang until the watchdog. If the patch goes while a clock runs, a watch on a timer sees passes running
  without it, records a fault and goes back to real time, so `Dispose` fails with the reason instead.
- **No game time passes while an eidolon mine plan or a crew's look-ahead routes are made off the main
  thread**: `Start` holds the clock while any is on its worker (`MinePlanHold`, read from
  `EidolonMinePlans`' private state: each job's `Job` and `Routing` tasks). Pass `holdMinePlans: false` to
  measure the planner's and the route solver's latency in game time instead.
- **Your own holds**: `clock.HoldWhile(() => cond, "what")` keeps game time still while `cond` is true, for
  other work off the main thread that runs on wall time. Each pass waits on the game thread (yielding) until
  no hold holds, then advances: the main thread steps the same way whatever the runner's speed. The predicate
  runs on the game thread between passes, so it may read main-thread state, but it must clear without the
  game thread's help. One that holds past its wall-time timeout (60 s by default) or throws is dropped, and
  the clock's `Dispose` throws, naming it. Dispose the hold to remove it.

### What changes in a scenario on it

- `W.ElapsedMilliseconds` (and `ServerUptimeMilliseconds`, every tick listener's and system's dt, every
  eidolon timer) is game time, in whole 33/34 ms steps. Makespans, contact milliseconds and the eidolon's
  live rules already read it, so they become game time with no change and stop stretching on slow runners.
- **Anything a scenario measures with its own `Stopwatch` or `Environment.TickCount64` is wall time and
  must move to `W.ElapsedMilliseconds`.**
- `World.Until(..., ticks)` and `World.Ticks(n)` bounds are exactly ticks × 33.3 ms of game time.
- Wall-time limits (`[AtlasScenario(TimeoutMs = ...)]`) can be cut about ten-fold.
- The calendar moves on by the same step (× its speed of time), so in-game hours pass with the clock.
- Each pass first waits for physics' off-thread sender to be idle. That closes an engine race
  (`Collection was modified` in `PhysicsManager.SendAttributesViaTCP`) which passes 1 ms apart expose.

### Still on wall time

Off-thread work: chunk loading, generation and compression threads, `Task.Run` work, and physics' sender.
Seen from the virtual clock it is about 20× slower. The mine planner's and route solver's workers are held
(above); a route solve starts on the tick it is due, with no wall-time budget. The planner's
main-thread slices, which in the game stop each tick after `MinePlanner.BudgetMs` (2 ms) of wall time, are
counted in blocks read while a clock is on (`EidolonMinePlans.BudgetReads`, set by `Start` to
`TypicalReads`, about what 2 ms reads, and put back by `Dispose`), so a job's window is read in the same
ticks on any runner and a plan starts on the same tick every run.

### When not to use it

- **Scenarios checking code that keeps wall time**: NanMotion's suspend detection, `TrunkToolBehavior`'s
  repeat pause and MapReveal read `Environment.TickCount64`, which the virtual clock does not move.
- **Waits on chunk loading or generation**: the chunk threads stay on wall time, so a tick-bounded wait for
  chunks gets about 20× fewer real seconds. Load what the scenario needs before `Start`.
