namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Ported screw output (<c>ScrewPortOutput</c>): the order a ported Archimedes screw tries its
/// faces in when it moves an item on. Game-independent, so tests/ runs it without the game.
///
/// The game's <c>BlockEntityItemFlow.MoveItem</c> tries an item's tagged face first, and an item
/// pushed in from another screw is always tagged up, throwing it out of the top into air before it
/// tries to push it anywhere; the port is tried only when a random pick lands on it. Here a screw
/// with a horizontal push face (a ported one) pushes into a container on each vertical face first
/// (up: the next screw of the stack, a chest on top), then into each horizontal face (the port),
/// then throws the item out of the horizontal face into air, but only when no vertical face has a
/// container: a screw with one above (the bottom of a stack) waits for it to take the item rather
/// than throw it out of its port while the screw above is still full. It never throws one out of a
/// vertical face. A screw with no horizontal push face (a straight one) is left to the game.
/// </summary>
public static class ScrewPortRoute
{
    public enum Action
    {
        /// <summary>Into the container on that face (<c>TryPushInto</c>).</summary>
        PushInto,

        /// <summary>Out of that face into air, as an item entity (<c>TrySpitOut</c>).</summary>
        SpitOut,
    }

    /// <summary>The steps to try, in order, until one moves the item, or null when the screw has
    /// no horizontal push face and the game's own order stands.</summary>
    /// <param name="pushFaces">The screw's push faces, in the block type's order.</param>
    /// <param name="horizontal">Whether a face is horizontal.</param>
    /// <param name="container">Whether the block on a face is a container (asked of vertical faces only).</param>
    public static IReadOnlyList<(Action Action, TFace Face)>? Steps<TFace>(IReadOnlyList<TFace> pushFaces,
        Func<TFace, bool> horizontal, Func<TFace, bool> container)
    {
        if (!pushFaces.Any(horizontal))
            return null;
        var steps = new List<(Action, TFace)>();
        bool above = false;
        foreach (var face in pushFaces.Where(f => !horizontal(f)))
        {
            steps.Add((Action.PushInto, face));
            above |= container(face);
        }
        foreach (var face in pushFaces.Where(horizontal))
        {
            steps.Add((Action.PushInto, face));
            if (!above)
                steps.Add((Action.SpitOut, face));
        }
        return steps;
    }
}
