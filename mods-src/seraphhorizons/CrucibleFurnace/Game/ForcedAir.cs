using System.Collections.Concurrent;
using System.Reflection;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// Forced air from a pipe (README "Crucible furnace"): ExpandedLib's pipe nodes
/// (<c>ExpandedLib.Industry.Pipes.IPipeNode</c>: every ppex pipe, and smex's tuyere) report their
/// network's <c>Medium</c> and give up gas by <c>TryConsume(litres)</c>, as smex's blast furnace draws
/// its blast. Found by name on the block entity's type, so nothing of exlib is referenced; a pipe end
/// against a hole is not open to the air, so it does not leak. Without exlib, or with its interface
/// changed (logged once), there is no forced air.
/// </summary>
public static class ForcedAir
{
    public const string InterfaceName = "ExpandedLib.Industry.Pipes.IPipeNode";

    private sealed record Node(PropertyInfo Medium, MethodInfo TryConsume);

    private static readonly ConcurrentDictionary<Type, Node?> Nodes = new();
    private static int _warned;

    /// <summary>Whether <paramref name="be"/> is a pipe node.</summary>
    public static bool IsPipe(BlockEntity? be) => be != null && NodeOf(be.GetType()) != null;

    /// <summary>Draws up to <paramref name="litres"/> of air from the pipe node <paramref name="be"/>;
    /// the litres drawn (0 if it is no pipe, or carries anything but air).</summary>
    public static float Draw(BlockEntity? be, float litres, ILogger? log = null)
    {
        if (be == null || litres <= 0 || NodeOf(be.GetType(), log) is not { } node)
            return 0;
        try
        {
            if (node.Medium.GetValue(be) as string != "Air")
                return 0;
            return node.TryConsume.Invoke(be, [litres]) is float drawn ? drawn : 0;
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
                log?.Warning("[seraphhorizons] Crucible furnace: drawing air from {0} failed, so there is no forced air: {1}", be.GetType().FullName, e.Message);
            return 0;
        }
    }

    private static Node? NodeOf(Type type, ILogger? log = null) => Nodes.GetOrAdd(type, t =>
    {
        if (t.GetInterface(InterfaceName) is not { } iface)
            return null;
        var medium = iface.GetProperty("Medium");
        var consume = iface.GetMethod("TryConsume", [typeof(float)]);
        if (medium?.PropertyType == typeof(string) && consume?.ReturnType == typeof(float))
            return new Node(medium, consume);
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            log?.Warning("[seraphhorizons] Crucible furnace: {0} is not as expected (Medium, TryConsume(float)), so there is no forced air", InterfaceName);
        return null;
    });
}
