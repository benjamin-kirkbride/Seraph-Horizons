using System.Text;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A machine's block info as it is written (<c>MachineText</c>, every machine on <c>BEMachine</c>): its
/// lines, each shown always or only to a player holding a wrench (the detail: shaft speed and load, oil
/// figures, exact work, the footprint). Without a wrench, a last line says a wrench shows more, when there
/// is more to show.
/// </summary>
public sealed class MachineInfo
{
    private readonly StringBuilder _lines = new();
    private readonly StringBuilder _detail = new();

    /// <summary>The lines shown always.</summary>
    public StringBuilder Lines => _lines;

    /// <summary>The lines shown only with a wrench in hand.</summary>
    public StringBuilder Detail => _detail;

    public void Add(string line) => _lines.AppendLine(line);

    public void AddDetail(string line) => _detail.AppendLine(line);

    public bool HasDetail => _detail.Length > 0;

    /// <summary>Writes the lines to <paramref name="dsc"/>: the detail too for a player holding a wrench
    /// (<paramref name="wrench"/>); else <paramref name="hint"/> after them when there is detail to show.</summary>
    public void WriteTo(StringBuilder dsc, bool wrench, string hint)
    {
        dsc.Append(_lines);
        if (wrench)
            dsc.Append(_detail);
        else if (HasDetail)
            dsc.AppendLine(hint);
    }
}
