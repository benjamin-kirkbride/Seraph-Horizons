using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.EntityInterpolationFix;

/// <summary>
/// EntityBehaviorInterpolatePosition, which smooths every remote entity's position on the client
/// between server packets, eases its playback speed toward a target once a frame:
/// <c>targetSpeed = GameMath.Lerp(targetSpeed, speed, dt * 4)</c>. The factor isn't clamped, so a
/// frame longer than 0.5 s overshoots, and at 1 FPS (a window in the background) the overshoot
/// triples and flips sign every frame. After about 80 such frames targetSpeed overflows float,
/// the position lerp's factor goes infinite, and the entity's position becomes NaN for good. The
/// player's repulsion then copies the NaN into the player's motion, and the next physics step
/// throws "Given pos contained NaN" (VintageStory-Issues#6758, the pack's #405).
///
/// The patch clamps that factor to 1, which keeps targetSpeed between its old value and its
/// target, and resets an interpolation already left non-finite to the behavior's start values.
/// </summary>
public class EntityInterpolationFixSystem : ModSystem
{
    public const string HarmonyId = "entityinterpolationfix";

    // Singleplayer runs the client and the server in one process, and Harmony patches are
    // process wide: patch on the first start, unpatch on the last dispose. The behavior only
    // runs on the client; patching on the server too changes nothing there, and lets the pack's
    // Atlas scenarios, which boot a server only, test the patched method.
    private static readonly object Gate = new();
    private static int _users;
    private static Harmony? _harmony;
    private bool _counted;

    public override void StartPre(ICoreAPI api)
    {
        lock (Gate)
        {
            _counted = true;
            if (_users++ > 0)
                return;

            var target = InterpolatePatch.Target();
            if (target == null || InterpolatePatch.TargetSpeedField == null || InterpolatePatch.DtAccumField == null)
            {
                api.Logger.Warning("[entityinterpolationfix] EntityBehaviorInterpolatePosition.OnRenderFrame not found as "
                                   + "expected; the game changed, so entity interpolation is left as the game does it");
                return;
            }
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(InterpolatePatch), nameof(InterpolatePatch.Prefix)),
                transpiler: new HarmonyMethod(typeof(InterpolatePatch), nameof(InterpolatePatch.Transpiler)));
            if (!InterpolatePatch.Applied)
                api.Logger.Warning("[entityinterpolationfix] EntityBehaviorInterpolatePosition.OnRenderFrame does not "
                                   + "lerp targetSpeed once; the game changed, so only non-finite states are reset");
        }
    }

    public override void Dispose()
    {
        lock (Gate)
        {
            if (!_counted)
                return;
            _counted = false;
            if (--_users > 0)
                return;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }
    }
}

public static class InterpolatePatch
{
    /// <summary>Whether the last transpile found the one targetSpeed lerp and clamped its factor.</summary>
    public static bool Applied { get; private set; }

    /// <summary>The behavior's starting targetSpeed, as its field initializer sets it.</summary>
    public const float StartSpeed = 0.6f;

    public static readonly FieldInfo? TargetSpeedField =
        AccessTools.Field(typeof(EntityBehaviorInterpolatePosition), nameof(EntityBehaviorInterpolatePosition.targetSpeed));

    public static readonly FieldInfo? DtAccumField =
        AccessTools.Field(typeof(EntityBehaviorInterpolatePosition), nameof(EntityBehaviorInterpolatePosition.dtAccum));

    private static readonly MethodInfo LerpFloat =
        AccessTools.Method(typeof(GameMath), nameof(GameMath.Lerp), new[] { typeof(float), typeof(float), typeof(float) });

    private static readonly MethodInfo MinFloat =
        AccessTools.Method(typeof(Math), nameof(Math.Min), new[] { typeof(float), typeof(float) });

    public static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(EntityBehaviorInterpolatePosition),
            nameof(EntityBehaviorInterpolatePosition.OnRenderFrame), new[] { typeof(float), typeof(EnumRenderStage) });

    /// <summary>An interpolation the unpatched method already left non-finite (or any other code
    /// did) goes back to the behavior's start values, so its entity moves again on the next
    /// packets instead of staying NaN until it unloads.</summary>
    public static void Prefix(EntityBehaviorInterpolatePosition __instance)
    {
        if (!float.IsFinite(__instance.targetSpeed) || !float.IsFinite(__instance.dtAccum))
        {
            __instance.targetSpeed = StartSpeed;
            __instance.dtAccum = 0f;
        }
    }

    /// <summary>Clamps the factor of the one <c>GameMath.Lerp(float, float, float)</c> whose
    /// result is stored in targetSpeed to at most 1. Leaves the method as it is unless there is
    /// exactly one such lerp.</summary>
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        Applied = false;
        var lerps = Enumerable.Range(0, code.Count - 1)
            .Where(i => code[i].Calls(LerpFloat) && code[i + 1].StoresField(TargetSpeedField))
            .ToList();
        if (lerps is not [var at])
            return code;

        // The factor is the last argument, on top of the stack right before the call.
        code.InsertRange(at, new[]
        {
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_R4, 1f),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Call, MinFloat),
        });
        Applied = true;
        return code;
    }
}
