using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/entityinterpolationfix: EntityBehaviorInterpolatePosition.OnRenderFrame, patched,
/// keeps a remote entity's interpolated position finite through a stretch of 1 FPS frames, where
/// the game's own method (a Harmony reverse patch: its unpatched body) leaves it NaN
/// (VintageStory-Issues#6758, the pack's #405), and moves it exactly as the game does at normal
/// frame rates. The behavior is client-only and Atlas boots a server only, so each scenario
/// builds one by hand, without its constructor (which refuses the server side), on a client API
/// stub that answers only what OnRenderFrame asks, and feeds it position packets as the server
/// would: 15 a second, collapsed past 20 queued, as OnReceivedServerPos does.
/// </summary>
public partial class PackFixScenarios
{
    private static class Interpolation
    {
        public const string HarmonyId = "entityinterpolationfix";

        public static readonly MethodInfo Target =
            AccessTools.DeclaredMethod(typeof(EntityBehaviorInterpolatePosition),
                nameof(EntityBehaviorInterpolatePosition.OnRenderFrame), [typeof(float), typeof(EnumRenderStage)])
            ?? throw new InvalidOperationException("EntityBehaviorInterpolatePosition.OnRenderFrame not found");

        private static readonly object Gate = new();
        private static bool _ready;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void VanillaFrame(EntityBehaviorInterpolatePosition instance, float dt, EnumRenderStage stage) =>
            throw new InvalidOperationException("reverse patch not applied");

        /// <summary>One frame of the game's OnRenderFrame, unpatched.</summary>
        public static void Vanilla(EntityBehaviorInterpolatePosition b, float dt)
        {
            lock (Gate)
            {
                if (!_ready)
                {
                    Harmony.ReversePatch(Target, new HarmonyMethod(typeof(Interpolation), nameof(VanillaFrame)));
                    _ready = true;
                }
            }
            VanillaFrame(b, dt, EnumRenderStage.Before);
        }

        /// <summary>One frame of OnRenderFrame as the pack runs it.</summary>
        public static void Patched(EntityBehaviorInterpolatePosition b, float dt) => b.OnRenderFrame(dt, EnumRenderStage.Before);

        /// <summary>A behavior as Initialize leaves it, on an entity standing at (x, y, z).</summary>
        public static EntityBehaviorInterpolatePosition Make(double x, double y, double z)
        {
            var entity = new EntityAgent();
            entity.Pos.SetPos(x, y, z);
            var b = (EntityBehaviorInterpolatePosition)RuntimeHelpers.GetUninitializedObject(typeof(EntityBehaviorInterpolatePosition));
            b.entity = entity;
            b.agent = entity;
            b.capi = ClientStub.Create();
            b.positionQueue = new Queue<PositionSnapshot>();
            b.targetSpeed = 0.6f;
            b.PushQueue(new PositionSnapshot(entity.Pos, 1f / 15f, isTeleport: false));
            return b;
        }

        /// <summary>One server position packet, as OnReceivedServerPos queues it.</summary>
        public static void Receive(EntityBehaviorInterpolatePosition b, double x, double y, double z)
        {
            var pos = b.entity.Pos.Copy();
            pos.SetPos(x, y, z);
            b.PushQueue(new PositionSnapshot(pos, 1f / 15f, isTeleport: false));
            if (b.queueCount > 20)
                b.PopQueue(clear: true);
        }

        /// <summary>
        /// Runs <paramref name="frames"/> frames of <paramref name="dt"/> seconds on a mob walking
        /// along x at <paramref name="speed"/> blocks a second, with the packets the server sends
        /// in each frame's time (15 a second) before it, and returns the position after each frame.
        /// </summary>
        public static List<(double X, double Y, double Z, float TargetSpeed)> Run(
            EntityBehaviorInterpolatePosition b, Action<EntityBehaviorInterpolatePosition, float> frame,
            float dt, int frames, double speed)
        {
            var trace = new List<(double, double, double, float)>();
            double serverX = b.entity.Pos.X, owed = 0;
            for (int i = 0; i < frames; i++)
            {
                for (owed += dt * 15; owed >= 1; owed--)
                    Receive(b, serverX += speed / 15, b.entity.Pos.Y, b.entity.Pos.Z);
                frame(b, dt);
                trace.Add((b.entity.Pos.X, b.entity.Pos.Y, b.entity.Pos.Z, b.targetSpeed));
            }
            return trace;
        }

        public static bool Finite((double X, double Y, double Z, float TargetSpeed) p) =>
            double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z) && float.IsFinite(p.TargetSpeed);
    }

    /// <summary>
    /// The client API OnRenderFrame reads: not paused, and a player entity that is never the
    /// interpolated one (null). Anything else it is asked for returns the type's default.
    /// </summary>
    public class ClientStub : DispatchProxy
    {
        public static ICoreClientAPI Create() => Create<ICoreClientAPI, ClientStub>();

        // IPlayer has internal members a proxy can't implement, so the player is the game's own
        // ClientPlayer, built without its constructor, on player data with no entity.
        private static readonly IClientPlayer Player = MakePlayer();

        private static IClientPlayer MakePlayer()
        {
            var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
            AccessTools.Field(typeof(ClientPlayer), "worlddata")
                .SetValue(player, RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData)));
            return player;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_IsGamePaused" => false,
            "get_World" => Create<IClientWorldAccessor, ClientStub>(),
            "get_Player" => Player,
            _ => method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null,
        };
    }

    [AtlasScenario]
    public void The_mod_patches_OnRenderFrame()
    {
        var info = Harmony.GetPatchInfo(Interpolation.Target);
        var transpilers = info?.Transpilers.Select(p => p.owner).ToList() ?? [];
        var prefixes = info?.Prefixes.Select(p => p.owner).ToList() ?? [];
        Assert.True(transpilers.Contains(Interpolation.HarmonyId) && prefixes.Contains(Interpolation.HarmonyId),
            $"OnRenderFrame is not patched by {Interpolation.HarmonyId} (transpilers: [{string.Join(", ", transpilers)}], "
            + $"prefixes: [{string.Join(", ", prefixes)}]); did the mod load?");
    }

    // The bug itself: at 1 FPS the game's targetSpeed overshoots ×3 a frame and overflows in about
    // 80 frames, leaving the mob's position NaN. Checked on the game's own method first, so the
    // scenario can't pass by not reaching the bug at all.
    [AtlasTheory]
    [InlineData(0.0)] // standing still, as the pudu fawn of #405 was
    [InlineData(1.5)] // walking
    public void A_mob_stays_finite_through_three_minutes_at_one_fps(double speed)
    {
        var vanilla = Interpolation.Run(Interpolation.Make(511668.077, 108, 512010.287), Interpolation.Vanilla, 1f, 180, speed);
        int broke = vanilla.FindIndex(p => !Interpolation.Finite(p));
        Assert.True(broke >= 0, $"the game's own method stayed finite at 1 FPS; the scenario no longer reproduces the bug "
                                + $"(last targetSpeed {vanilla[^1].TargetSpeed})");
        Console.WriteLine($"[entityinterpolationfix] vanilla went non-finite after {broke + 1} frames at 1 FPS, speed {speed}");

        var patched = Interpolation.Run(Interpolation.Make(511668.077, 108, 512010.287), Interpolation.Patched, 1f, 180, speed);
        int bad = patched.FindIndex(p => !Interpolation.Finite(p));
        Assert.True(bad < 0, $"patched: non-finite after {bad + 1} frames: {(bad < 0 ? default : patched[bad])}");
        // The lerp now stays between its old value and its target (0.8 to 5 for 0 to 21 queued).
        Assert.All(patched, p => Assert.InRange(p.TargetSpeed, 0.6f, 5f));
    }

    // Below 0.25 s a frame the clamp never bites (dt·4 < 1), so the patched method must move the
    // mob exactly as the game does, frame for frame.
    [AtlasTheory]
    [InlineData(1f / 60)]
    [InlineData(1f / 15)]
    [InlineData(0.2f)]
    public void A_mob_moves_exactly_as_in_the_game_at_normal_frame_rates(float dt)
    {
        int frames = (int)(30 / dt); // 30 s
        var vanilla = Interpolation.Run(Interpolation.Make(100.5, 110, 200.5), Interpolation.Vanilla, dt, frames, 1.5);
        var patched = Interpolation.Run(Interpolation.Make(100.5, 110, 200.5), Interpolation.Patched, dt, frames, 1.5);
        Assert.Equal(vanilla, patched);
        Assert.All(patched, p => Assert.True(Interpolation.Finite(p)));
    }

    // An interpolation already broken (by the game before the mod loaded, or any other code)
    // recovers on the next frame instead of staying NaN until its entity unloads.
    [AtlasTheory]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(0.6f, float.NaN)]
    [InlineData(0.6f, float.NegativeInfinity)]
    public void A_broken_interpolation_recovers(float targetSpeed, float dtAccum)
    {
        var b = Interpolation.Make(100.5, 110, 200.5);
        b.targetSpeed = targetSpeed;
        b.dtAccum = dtAccum;
        var trace = Interpolation.Run(b, Interpolation.Patched, 1f / 60, 60, 1.5);
        Assert.All(trace, p => Assert.True(Interpolation.Finite(p), $"non-finite: {p}"));
    }
}
