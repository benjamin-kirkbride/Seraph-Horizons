using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>How far a ground-stored pipe section's boxes turn with its model (<see cref="GroundBoxTurns"/>).</summary>
public class GroundBoxTurnsTests
{
    [Theory]
    // as the game sets MeshAngle on placement: a whole number of quarter turns, from -PI to PI
    [InlineData(0f, 0)]
    [InlineData((float)(Math.PI / 2), 1)]
    [InlineData((float)Math.PI, 2)]
    [InlineData((float)-Math.PI, 2)]
    [InlineData((float)(-Math.PI / 2), 3)]
    // turned by schematics (MeshAngle less a quarter turn each time) past a whole turn
    [InlineData((float)(-5 * Math.PI / 2), 3)]
    [InlineData((float)(4 * Math.PI), 0)]
    [InlineData((float)(7 * Math.PI / 2), 3)]
    public void A_whole_number_of_quarter_turns_turns_the_boxes(float meshAngle, int turns) =>
        Assert.Equal(turns, GroundBoxTurns.QuarterTurns(meshAngle));

    [Theory]
    // a randomised centre rotation, which no axis-aligned box follows
    [InlineData(0.3f)]
    [InlineData((float)(Math.PI / 4))]
    [InlineData((float)(Math.PI / 2 + 0.01))]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Any_other_angle_leaves_them(float meshAngle) =>
        Assert.Null(GroundBoxTurns.QuarterTurns(meshAngle));
}
