# Entity Interpolation Fix

A code mod that stops mobs and other remote entities from turning NaN on the client after the
game runs at a low frame rate (a window in the background), and the client crash that follows:
`System.ArgumentException: Given pos contained NaN ... for entity game:player`
([VintageStory-Issues#6758](https://github.com/anegostudios/VintageStory-Issues/issues/6758),
the pack's #405).

## The bug

`EntityBehaviorInterpolatePosition` (VSEssentials) smooths every remote entity's position on the
client between the server's position packets. At the end of each frame it eases its playback
speed toward a target that depends on how many packets are queued:

```csharp
float v = (float)queueCount * 0.2f + 0.8f;
targetSpeed = GameMath.Lerp(targetSpeed, v, dt * 4f);
```

The factor `dt * 4` is never clamped, so each frame multiplies targetSpeed's distance from its
target by `1 - 4·dt`. Below 0.5 s a frame, that distance shrinks. Above 0.5 s it grows, and at 1 FPS
it triples and flips sign every frame. A client window in the background can run at 1 FPS: a
pack client was measured at dt = 1.000 ± 0.001 s for 30 frames in a row. After about 80 such
frames targetSpeed overflows float:

1. `dtAccum += dt * targetSpeed` reaches about ±1e38.
2. `dtAccum / pN.interval` overflows to ±Infinity.
3. `GameMath.Lerp(pL.x, pN.x, ±inf)` returns NaN: `x + 0·inf` when the entity stands still.

The entity never recovers. The next lerp is `inf - inf`, and a NaN `dtAccum` passes neither of
`PopQueue`'s resets (`< 0`, `> 1`). It stays at a NaN position, invisible, until it unloads.
Many entities break in one background stretch, each after a different number of frames. Back at
full frame rate, when the player comes near one, `EntityBehaviorRepulseAgents` copies the NaN
into the player's motion, and the next physics step throws in
`EntityBehaviorControlledPhysics.ApplyTests`. That can be seconds or hours later.

## The fix

A Harmony patch on `EntityBehaviorInterpolatePosition.OnRenderFrame`:

- A transpiler wraps the factor of that one lerp in `Math.Min(…, 1f)`. targetSpeed then stays
  between its old value and its target, so it can't overshoot at any frame rate. Below 0.25 s a
  frame the factor is under 1 and nothing changes. If the method no longer has exactly one
  `GameMath.Lerp(float, float, float)` stored into `targetSpeed`, the transpiler leaves it as it
  is and the mod logs a warning.
- A prefix resets an interpolation that is already non-finite (`targetSpeed` or `dtAccum`) to the
  behavior's start values (0.6, 0). Such an entity goes back to following its packets and does
  not stay NaN. With the clamp in place this only happens if something else broke it.

The behavior runs only on the client. The mod is optional on either side (`"side": "Universal"`,
not required on the client or the server). It patches on the server too, where the method never
runs, so the pack's Atlas scenarios can test it.

## Tests

`tests/PackTests/EntityInterpolationFixScenarios.cs` (Atlas, a partial of `PackFixScenarios`)
builds a behavior by hand on a client API stub and feeds it packets as the server sends them. It
runs the game's original method (a Harmony reverse patch) and the patched one:

- 180 frames at 1 FPS, a mob standing and a mob walking: the original must go NaN, which shows
  the scenario reaches the bug, and the patched one must stay finite.
- 30 s at 60, 15 and 5 FPS: the patched method must move the mob exactly as the original does,
  frame for frame.
- An interpolation already NaN or infinite recovers on the next frames.

The test project loads this directory's build as a mod, and leaves out a pinned copy from the
ModDB (`entityinterpolationfix_*.zip` in `build/mods`).

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`EntityInterpolationFix.csproj`, merge, tag `entityinterpolationfix-v<version>` on main, upload the
zip from the GitHub Release to the ModDB, then pin it in `pack/pack.toml` and run `packtool lock`.

## Retiring it

When the game clamps that lerp (a fix may land in 1.23: VintageStory-Issues#6758), remove the mod
and its scenarios, and mark the mod obsolete on the ModDB.
