namespace SeraphHorizons.Mod.Core;

/// <summary>
/// MPE Gearbox source ratio (<c>GearboxSourceRatio</c>): which geared ratio a block that has just
/// discovered its network through a neighbour should carry. Game-independent, so tests/ runs it
/// without the game.
///
/// The game asks a neighbour for its ratio with <c>GetGearedRatio(face)</c> in two ways: when a
/// block connects (<c>tryConnect</c>), <c>face</c> points from the asker toward the neighbour;
/// when a power source creates its network and spreads it (<c>CreateJoinAndDiscoverNetwork</c>),
/// it is the neighbour's own connector face. Both give the same answer for a block with one ratio
/// on every face, which is every vanilla block. MPE Gearbox's gearbox answers only the first way
/// right, so a source discovering through one gets the ratio of the gearbox's far side. A block
/// coupled straight to a neighbour turns as fast as the neighbour's face that touches it: that is
/// the first way's answer, <paramref name="atTouchingFace"/>.
/// </summary>
public static class GearboxCoupling
{
    /// <summary>MPE Gearbox's block entity behavior, by full name: the mod is not referenced at
    /// build time.</summary>
    public const string GearboxBehaviorType = "MPEGearbox.BEBehaviorGearbox12";

    /// <summary>The ratio to set on the block, or null to leave <paramref name="stored"/> as it
    /// is: only when the neighbour is a gearbox, on the same network, the block itself is not one
    /// (its stored ratio is its low side's, not the ratio of the face it touches), and the gearbox's
    /// answer at the touching face is a usable ratio that differs.</summary>
    /// <param name="neighbourIsGearbox">The neighbour in the discovery direction is MPE Gearbox's.</param>
    /// <param name="selfIsGearbox">The block that discovered is one too.</param>
    /// <param name="sameNetwork">Both are on the same, existing network.</param>
    /// <param name="stored">The block's ratio as the game left it.</param>
    /// <param name="atTouchingFace">The neighbour's ratio at the face that touches the block
    /// (<c>GetGearedRatio</c> toward the neighbour, as <c>tryConnect</c> asks it).</param>
    public static float? RatioToSet(bool neighbourIsGearbox, bool selfIsGearbox, bool sameNetwork, float stored,
        float atTouchingFace)
    {
        if (!neighbourIsGearbox || selfIsGearbox || !sameNetwork)
            return null;
        if (!float.IsFinite(atTouchingFace) || atTouchingFace <= 0f || atTouchingFace == stored)
            return null;
        return atTouchingFace;
    }
}
