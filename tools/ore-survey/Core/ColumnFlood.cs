namespace SeraphHorizons.OreSurvey.Core;

/// <summary>A chunk column, in chunks.</summary>
public readonly record struct Column(int X, int Z)
{
    public int Chebyshev(Column o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Z - o.Z));
}

/// <summary>
/// Which chunk columns a census has to generate to see all of one deposit (README, "Census").
/// It starts from the anchor and its margin; every column found holding the deposit's metal asks
/// for the columns within <see cref="Margin"/> of it, so the flood ends when an empty margin of
/// that many columns surrounds everything found. Columns further than <see cref="Cap"/> from the
/// anchor (Chebyshev) are never asked for; wanting one marks the flood <see cref="Capped"/>.
/// Game-independent: the caller generates each batch (<see cref="NextBatch"/>) and reports every
/// column of it (<see cref="Report"/>) before asking for the next.
/// </summary>
public sealed class ColumnFlood
{
    private readonly HashSet<Column> _asked = [];
    private readonly HashSet<Column> _reported = [];
    private readonly HashSet<Column> _found = [];
    private readonly List<Column> _pending = [];

    public Column Anchor { get; }
    public int Margin { get; }
    public int Cap { get; }

    /// <summary>A column holding the metal wanted one beyond the cap.</summary>
    public bool Capped { get; private set; }

    /// <summary>Columns asked for (generated, or read from the scan cache).</summary>
    public IReadOnlyCollection<Column> Asked => _asked;

    /// <summary>Columns holding the metal.</summary>
    public IReadOnlyCollection<Column> Found => _found;

    public ColumnFlood(Column anchor, int margin = 2, int cap = 16)
    {
        if (margin < 1) throw new ArgumentOutOfRangeException(nameof(margin));
        if (cap < margin) throw new ArgumentOutOfRangeException(nameof(cap));
        Anchor = anchor;
        Margin = margin;
        Cap = cap;
        Want(anchor);
    }

    /// <summary>All asked columns are reported and none is pending: the flood is complete.</summary>
    public bool Done => _pending.Count == 0 && _reported.Count == _asked.Count;

    /// <summary>The columns to generate next (empty when done or still waiting for reports).</summary>
    public IReadOnlyList<Column> NextBatch()
    {
        if (_reported.Count != _asked.Count - _pending.Count) return [];
        var batch = _pending.ToList();
        _pending.Clear();
        return batch;
    }

    /// <summary>A column of a batch was generated: whether it holds the deposit's metal.</summary>
    public void Report(Column c, bool hasMetal)
    {
        if (!_asked.Contains(c)) throw new InvalidOperationException($"column {c} was not asked for");
        if (!_reported.Add(c)) return;
        if (!hasMetal) return;
        _found.Add(c);
        Want(c);
    }

    // Asks for every column within the margin of c (c itself included) not asked yet.
    private void Want(Column c)
    {
        for (int dx = -Margin; dx <= Margin; dx++)
            for (int dz = -Margin; dz <= Margin; dz++)
            {
                var n = new Column(c.X + dx, c.Z + dz);
                if (n.Chebyshev(Anchor) > Cap)
                {
                    Capped = true;
                    continue;
                }
                if (_asked.Add(n)) _pending.Add(n);
            }
    }
}
