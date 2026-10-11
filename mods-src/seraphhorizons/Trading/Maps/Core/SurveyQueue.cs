namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>
/// The deposits waiting to be checked (#693), by id, in the order they were asked for, each once.
/// <b>Background</b> checks (a player nearing a camp) run one at a time, at least
/// <see cref="PauseSeconds"/> after the last one that did any work ended, so generating terrain
/// ahead of players never piles up on the server. <b>Urgent</b> checks (a trade window opened on a
/// shelf still waiting for one) go first and at once, beside a background check if one is running;
/// asking for a waiting background check urgently moves it up. A check already running is not asked
/// for again. Game-independent: the caller gives the time and runs the checks.
/// </summary>
public sealed class SurveyQueue
{
    private readonly List<string> _urgent = new();
    private readonly List<string> _background = new();
    private readonly HashSet<string> _running = new();
    private string? _backgroundRunning;
    private double _backgroundDoneAt = double.NegativeInfinity;

    public SurveyQueue(double pauseSeconds) => PauseSeconds = Math.Max(0, pauseSeconds);

    /// <summary>Seconds between the end of one background check that did work and the start of the next.</summary>
    public double PauseSeconds { get; }

    public int Waiting => _urgent.Count + _background.Count;

    public int Running => _running.Count;

    public bool IsWaiting(string id) => _urgent.Contains(id) || _background.Contains(id);

    public bool IsRunning(string id) => _running.Contains(id);

    /// <summary>Asks for a check; false when it is running, or waiting already at that urgency or above.</summary>
    public bool Want(string id, bool urgent)
    {
        if (_running.Contains(id) || _urgent.Contains(id)) return false;
        if (!urgent)
        {
            if (_background.Contains(id)) return false;
            _background.Add(id);
            return true;
        }
        _background.Remove(id);
        _urgent.Add(id);
        return true;
    }

    /// <summary>The next check to start now, or null: the first urgent one; else, with no background
    /// check running and the pause over, the first background one.</summary>
    public string? Next(double nowSeconds)
    {
        if (_urgent.Count > 0)
        {
            string id = _urgent[0];
            _urgent.RemoveAt(0);
            _running.Add(id);
            return id;
        }
        if (_backgroundRunning != null || _background.Count == 0 || nowSeconds - _backgroundDoneAt < PauseSeconds) return null;
        string next = _background[0];
        _background.RemoveAt(0);
        _running.Add(next);
        _backgroundRunning = next;
        return next;
    }

    /// <summary>A check has ended. <paramref name="worked"/>: it generated or measured anything (a
    /// check found needless starts no pause).</summary>
    public void Done(string id, double nowSeconds, bool worked = true)
    {
        _running.Remove(id);
        if (id != _backgroundRunning) return;
        _backgroundRunning = null;
        if (worked) _backgroundDoneAt = nowSeconds;
    }
}
