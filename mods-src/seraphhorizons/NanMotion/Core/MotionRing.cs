namespace SeraphHorizons.Mod.NanMotion.Core;

/// <summary>One physics frame of the watched entity, as the start of its physics step saw it.</summary>
public readonly record struct MotionFrame(
    long ElapsedMs,
    double MotionX, double MotionY, double MotionZ,
    double PosX, double PosY, double PosZ,
    float Yaw,
    bool OnGround,
    bool TriesToMove,
    float WalkSpeed);

/// <summary>
/// The last <see cref="Capacity"/> physics frames (NaN motion diagnostics, #405): a fixed array
/// written in a circle, so adding a frame every physics step allocates nothing.
/// </summary>
public sealed class MotionRing
{
    private readonly MotionFrame[] _frames;
    private int _next;

    public MotionRing(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "at least one frame");
        _frames = new MotionFrame[capacity];
    }

    public int Capacity => _frames.Length;

    /// <summary>How many frames it holds, up to <see cref="Capacity"/>.</summary>
    public int Count { get; private set; }

    public void Add(in MotionFrame frame)
    {
        _frames[_next] = frame;
        _next = (_next + 1) % _frames.Length;
        if (Count < _frames.Length)
            Count++;
    }

    /// <summary>The frames held, oldest first.</summary>
    public List<MotionFrame> Frames()
    {
        var list = new List<MotionFrame>(Count);
        int start = Count < _frames.Length ? 0 : _next;
        for (int i = 0; i < Count; i++)
            list.Add(_frames[(start + i) % _frames.Length]);
        return list;
    }

    /// <summary>The frames as report lines, oldest first, each non-finite value flagged.</summary>
    public IEnumerable<string> Describe()
    {
        var frames = Frames();
        if (frames.Count == 0)
        {
            yield return "(no physics frame recorded)";
            yield break;
        }
        long last = frames[^1].ElapsedMs;
        foreach (var f in frames)
            yield return $"t{f.ElapsedMs - last,6}ms motion {NanFormat.Vec(f.MotionX, f.MotionY, f.MotionZ)} "
                         + $"pos {NanFormat.Vec(f.PosX, f.PosY, f.PosZ)} yaw {NanFormat.Num(f.Yaw)} "
                         + $"onGround {f.OnGround} triesToMove {f.TriesToMove} walkSpeed {NanFormat.Num(f.WalkSpeed)}";
    }
}
