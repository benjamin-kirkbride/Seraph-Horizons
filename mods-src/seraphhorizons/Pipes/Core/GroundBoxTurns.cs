namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// How far a ground-stored stack's boxes turn with its model (README "Unified pipes", "On the
/// ground"), game-independent. The game's ground storage turns the model by its <c>MeshAngle</c>
/// (set from where the player stood, a multiple of a quarter turn) but returns its collision and
/// selection boxes as the item type declares them, unturned; a stack whose item opts in
/// (<see cref="Attribute"/>) gets them turned by the same angle about the block's centre.
/// </summary>
public static class GroundBoxTurns
{
    /// <summary>The item attribute that opts a ground-storable item in: <c>true</c> on the pipe
    /// section, whose box is a block long and narrow.</summary>
    public const string Attribute = "groundStorageTurnsBoxes";

    private const double QuarterTurn = Math.PI / 2;

    // A float angle set from a multiple of PI/2 (or read back from a save) is off by far less.
    private const double Tolerance = 1e-3;

    /// <summary>The quarter turns, 0 to 3, in <paramref name="meshAngle"/> (radians, any sign or
    /// size), or null when it is not a whole number of quarter turns (a randomised centre rotation),
    /// which no axis-aligned box can follow.</summary>
    public static int? QuarterTurns(float meshAngle)
    {
        if (!float.IsFinite(meshAngle))
            return null;
        double turns = meshAngle / QuarterTurn;
        double whole = Math.Round(turns);
        if (Math.Abs(turns - whole) > Tolerance)
            return null;
        return (int)(((long)whole % 4 + 4) % 4);
    }
}
