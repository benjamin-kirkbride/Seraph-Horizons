namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// The carried trunk's first-person frame, game-independent. Carry On draws a carried block in one
/// frame per view and applies the block's one <c>hands</c> transform in it
/// (<c>patches/trunkentities-carryon.json</c>). In third person the frame is its
/// <c>carryon:FrontCarry</c> point on the left forearm, which Logging Expanded's
/// <c>trunkcarry</c> and <c>trunkcarryheavy</c> raise to the shoulder: the frame's x points up,
/// y back and z right (to within the animation's tilt of about 20°). In first person the frame is
/// Carry On's own, built for its <c>holdheavy</c> carry in front of the chest
/// (<c>CarryFirstPersonTransform.GetFirstPersonHandsMatrix</c>, ending in a quarter turn about y):
/// x forward, y up, z right. So no one transform lays a trunk front to back in both: rotationX 90
/// (the trunk's length, its block's z, onto the frame's y) puts it along the walk in third person
/// and on end in first. The pack's postfix on Carry On's first-person matrix, for a carried trunk
/// only, turns its frame into the third-person one (<see cref="FirstPersonAdjust"/>) and moves its
/// point from Carry On's spot in front of the chest to where the shoulder is from the eye in third
/// person, so the same transform puts the trunk on the left shoulder, front to back, in both.
/// </summary>
public static class TrunkCarryPose
{
    /// <summary>How far, blocks, the first-person point moves back towards the camera: Carry On's
    /// is 0.4 in front of the eye, the forearm's in third person a little behind it.</summary>
    public const float FirstPersonBack = 0.55f;

    /// <summary>How far, blocks, the first-person point moves up: Carry On's is 0.35 below the eye,
    /// the raised forearm's about 0.3.</summary>
    public const float FirstPersonUp = 0.05f;

    /// <summary>How far, blocks, the first-person point moves right (negative: left, the forearm
    /// that carries is the left one).</summary>
    public const float FirstPersonRight = -0.15f;

    /// <summary>The column-major 4×4 matrix the pack multiplies onto Carry On's first-person
    /// matrix (on the right) for a carried trunk: in Carry On's frame (x forward, y up, z right) a
    /// move by (<see cref="FirstPersonBack"/> back, <see cref="FirstPersonUp"/> up,
    /// <see cref="FirstPersonRight"/> right), then a quarter turn about z, so the frame's x points
    /// up, y back and z right, as in third person.</summary>
    public static float[] FirstPersonAdjust() =>
    [
        0, 1, 0, 0, // x → up
        -1, 0, 0, 0, // y → back
        0, 0, 1, 0, // z → right
        -FirstPersonBack, FirstPersonUp, FirstPersonRight, 1,
    ];

    /// <summary><paramref name="m"/> (column-major) applied to the point (x, y, z).</summary>
    public static (float X, float Y, float Z) Apply(float[] m, float x, float y, float z) =>
        (m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]);
}
