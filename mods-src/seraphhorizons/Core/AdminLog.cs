using System.Globalization;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// An admin log file with channels switched on and off by command (<c>/sh ore log</c>,
/// <c>/sh trade log</c>): <c>Logs/seraphhorizons-ore.log</c>, <c>Logs/seraphhorizons-trade.log</c>.
/// The game's own loggers write a fixed file per log type and take no new ones, so this is a plain
/// appending file next to them. Lines come from worldgen threads as well as the main thread, hence
/// the lock; with every channel off nothing is opened or written.
/// </summary>
public sealed class ChannelLog : IDisposable
{
    private readonly object _lock = new();
    private readonly HashSet<string> _on = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _clock;
    private StreamWriter? _writer;

    public ChannelLog(string path, IEnumerable<string> channels, Func<DateTime>? clock = null)
    {
        Path = path;
        Channels = channels.ToArray();
        _clock = clock ?? (() => DateTime.Now);
    }

    public string Path { get; }

    /// <summary>Every channel this log knows.</summary>
    public IReadOnlyList<string> Channels { get; }

    public IReadOnlyCollection<string> On
    {
        get
        {
            lock (_lock) return _on.Order(StringComparer.Ordinal).ToArray();
        }
    }

    public bool IsOn(string channel)
    {
        lock (_lock) return _on.Contains(channel);
    }

    /// <summary>Switches channels (all of them when <paramref name="channel"/> is null); false for a
    /// channel the log doesn't know.</summary>
    public bool Set(string? channel, bool on)
    {
        lock (_lock)
        {
            if (channel != null && !Channels.Contains(channel)) return false;
            foreach (var c in channel == null ? Channels : [channel])
                if (on) _on.Add(c);
                else _on.Remove(c);
            if (_on.Count == 0) Close();
            return true;
        }
    }

    /// <summary>Writes a line if its channel is on: <c>2026-10-06 14:03:11 [channel] text</c>.</summary>
    public void Write(string channel, string text)
    {
        lock (_lock)
        {
            if (!_on.Contains(channel)) return;
            try
            {
                if (_writer == null)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    _writer = new StreamWriter(new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                }
                _writer.WriteLine(Format(_clock(), channel, text));
            }
            catch (IOException)
            {
                // A log that can't be written must not break worldgen or a trade.
            }
        }
    }

    public static string Format(DateTime time, string channel, string text) =>
        time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" + channel + "] " + text.Replace('\n', ' ');

    private void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _on.Clear();
            Close();
        }
    }
}
