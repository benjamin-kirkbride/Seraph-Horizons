namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// The players working a hand machine, as the quern keeps its grinders: each with the time the game
/// last said they held right-click on it. A player counts until <see cref="TimeoutMs"/> after that, or
/// until they let go. Every hand-worked machine keeps one (<c>BEMachine.HandWorked</c>): the press
/// brake's lever, the squaring shear's treadle, the mandrel station's winding, the riddle.
/// </summary>
public sealed class HandHolds(long timeoutMs = HandHolds.DefaultTimeoutMs)
{
    /// <summary>How long a player counts as working after the game last said they hold right-click
    /// (the quern forgets a grinding player after a second).</summary>
    public const long DefaultTimeoutMs = 600;

    private readonly Dictionary<string, long> _holds = [];

    public long TimeoutMs { get; } = timeoutMs;

    public void Hold(string player, long nowMs) => _holds[player] = nowMs;

    public void Release(string player) => _holds.Remove(player);

    public void Clear() => _holds.Clear();

    /// <summary>Forgets every player not heard from for <see cref="TimeoutMs"/>, and says whether
    /// anyone is still working the machine.</summary>
    public bool Any(long nowMs)
    {
        foreach (var stale in _holds.Where(h => nowMs - h.Value > TimeoutMs).Select(h => h.Key).ToList())
            _holds.Remove(stale);
        return _holds.Count > 0;
    }
}
