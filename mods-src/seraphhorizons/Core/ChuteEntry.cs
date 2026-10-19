namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Chutes never send an item back (<c>ChutesNoBacktrack</c>, #1348): which faces a chute refuses
/// while it holds an item. Game-independent, so tests/ runs it without the game.
///
/// The game's <c>BlockEntityItemFlow.MoveItem</c> lets an item leave through the face it came in
/// by: an elbow or T whose push fails falls back to pushing into its intake, a two-way chute picks
/// a face at random (and throws out of the back into air before its own guard), a ported screw
/// pushes back out of its port. Behind a slow machine the item bounces until it reaches the game's
/// three sideways moves and wedges. The face an item came in by is recorded on the chute, and
/// pushing or throwing it out of that face is refused while the chute holds it: it waits instead.
/// </summary>
public static class ChuteEntry
{
    /// <summary>Whether a chute refuses to move its item out of <paramref name="face"/>: only when
    /// it holds an item, has one slot (a hopper's four are left alone) and recorded that the item
    /// came in by that face. Faces are the game's indices (<c>BlockFacing.Index</c>).</summary>
    /// <param name="entry">The face the item came in by, or null when unknown (put in by hand,
    /// or placed before the switch): the game's own order stands.</param>
    public static bool Refuses(int? entry, int face, bool holding, int slots) =>
        holding && slots == 1 && entry == face;

    /// <summary>The direction to tag an item with (<c>chuteDir</c>) when a machine pushes it out of
    /// its face <paramref name="face"/> into an item flow, as the game tags an item one chute pushes
    /// into the next: its travel direction, or up into an Archimedes screw.</summary>
    public static int Tag(int face, bool screw, int up) => screw ? up : face;
}
