namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>The form tailings are discarded in (#694): vanilla's layered gravel (coarse stages) or
/// layered sand (fine stages), in the ore's host rock.</summary>
public enum TailingsForm { Gravel, Sand }

/// <summary>A machine's tailings rule (#694, the owner's ruling: tailings follow the machine): the
/// form and how many layers (an eighth of a block each) one rock item discarded makes.</summary>
public sealed record TailingsRule(TailingsForm Form, double LayersPerRock);

/// <summary>
/// The layers a machine owes its heap: the fraction carried from one discarded rock to the next, so
/// 0.75 a rock makes three layers for four rocks. Saved as a double with the machine.
/// </summary>
public readonly record struct TailingsOwed(double Layers)
{
    public static readonly TailingsOwed None = new(0);

    /// <summary>The whole layers <paramref name="rocks"/> discarded make under <paramref name="rule"/>,
    /// and what is carried.</summary>
    public (int Whole, TailingsOwed Carried) Add(double rocks, TailingsRule rule)
    {
        if (!(rocks > 0) || !(rule.LayersPerRock > 0))
            return (0, this);
        double total = Layers + rocks * rule.LayersPerRock;
        int whole = (int)Math.Floor(total + 1e-9);
        return (whole, new TailingsOwed(Math.Max(0, total - whole)));
    }

    public static TailingsOwed Restore(double layers) => new(double.IsFinite(layers) ? Math.Clamp(layers, 0, 1) : 0);
}

/// <summary>Vanilla's tailings blocks: <c>game:gravel-{rock}-land-{1..7}</c> and <c>game:sand-{rock}-{1..7}</c>
/// (BlockVariheight, <c>blocktypes/stone/gravel-layered.json</c> and <c>sand-layered.json</c>), and the
/// full blocks <c>game:gravel-{rock}</c> and <c>game:sand-{rock}</c> at eight layers.</summary>
public static class TailingsBlocks
{
    public const int LayersPerBlock = 8;

    /// <summary>The block of <paramref name="layers"/> (1 to 8) of <paramref name="form"/> in <paramref name="rock"/>.</summary>
    public static string Code(TailingsForm form, string rock, int layers)
    {
        if (layers < 1 || layers > LayersPerBlock)
            throw new ArgumentOutOfRangeException(nameof(layers), layers, "1 to 8 layers");
        string kind = form == TailingsForm.Gravel ? "gravel" : "sand";
        if (layers == LayersPerBlock)
            return $"game:{kind}-{rock}";
        return form == TailingsForm.Gravel ? $"game:gravel-{rock}-land-{layers}" : $"game:sand-{rock}-{layers}";
    }

    /// <summary>The form, rock and layers of a tailings block's code (full or layered, any habitat), or
    /// null for anything else. A full gravel or sand block is 8 layers.</summary>
    public static (TailingsForm Form, string Rock, int Layers)? Parse(string? code)
    {
        code = CodePattern.Normalise(code);
        if (code == null || !code.StartsWith("game:", StringComparison.Ordinal))
            return null;
        var parts = code["game:".Length..].Split('-');
        if (parts.Length < 2)
            return null;
        TailingsForm form;
        if (parts[0] == "gravel")
            form = TailingsForm.Gravel;
        else if (parts[0] == "sand")
            form = TailingsForm.Sand;
        else
            return null;
        if (parts.Length == 2)
            return (form, parts[1], LayersPerBlock);
        if (form == TailingsForm.Gravel && parts.Length == 4 && int.TryParse(parts[3], out int g) && g is >= 1 and <= 7)
            return (form, parts[1], g);
        if (form == TailingsForm.Sand && parts.Length == 3 && int.TryParse(parts[2], out int s) && s is >= 1 and <= 7)
            return (form, parts[1], s);
        return null;
    }
}

/// <summary>What is in a heap cell, as far as the heap cares.</summary>
public enum TailingsCellKind
{
    /// <summary>Air or something a block replaces (grass, snow layers): the heap may start there.</summary>
    Open,
    /// <summary>A partial block of the heap's own form and rock (<see cref="TailingsCell.Layers"/> 1 to 7).</summary>
    Layered,
    /// <summary>Anything else: a full block, another rock's layers, a liquid, a machine.</summary>
    Blocked,
}

/// <summary>A heap cell as the heap sees it, and whether the cell under it is solid enough to bear a layer.</summary>
public readonly record struct TailingsCell(TailingsCellKind Kind, int Layers = 0, bool Supported = true);

/// <summary>
/// Where discarded tailings go (#694): a heap beside the machine's tailings point, layering up. The
/// heap fills its cells in <see cref="Offsets"/> order, each to a full block before the next: the
/// tailings cell, then the four cells round it at its level, then up the tailings cell's column. A
/// cell is used only when the cell under it bears a layer; a full heap takes no more (the player
/// clears it with a shovel), and the layers it could not place are reported, not kept.
/// </summary>
public static class TailingsHeap
{
    /// <summary>The heap's cells, from the tailings cell: (dx, dy, dz) in world axes.</summary>
    public static readonly IReadOnlyList<Int3> Offsets =
    [
        new(0, 0, 0), new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 1, 0), new(0, 2, 0),
    ];

    /// <summary>The writes that put <paramref name="layers"/> on the heap, reading each cell with
    /// <paramref name="read"/> (an index into <see cref="Offsets"/>): per cell its index and the layers
    /// it holds after (8 a full block); and how many layers found no room.</summary>
    public static (IReadOnlyList<(int Cell, int Layers)> Writes, int Unplaced) Place(Func<int, TailingsCell> read, int layers)
    {
        var writes = new List<(int, int)>();
        // a cell filled by this call bears the one over it, whatever was read before
        var filled = new HashSet<int>();
        for (int i = 0; i < Offsets.Count && layers > 0; i++)
        {
            var cell = read(i);
            int below = IndexOf(Offsets[i] + new Int3(0, -1, 0));
            bool supported = cell.Supported || below >= 0 && filled.Contains(below);
            int have = cell.Kind switch
            {
                TailingsCellKind.Open => 0,
                TailingsCellKind.Layered => Math.Clamp(cell.Layers, 1, TailingsBlocks.LayersPerBlock - 1),
                _ => -1,
            };
            if (have < 0 || !supported && have == 0)
            {
                if (cell.Kind == TailingsCellKind.Blocked && Full(cell))
                    filled.Add(i);
                continue;
            }
            int add = Math.Min(layers, TailingsBlocks.LayersPerBlock - have);
            layers -= add;
            writes.Add((i, have + add));
            if (have + add == TailingsBlocks.LayersPerBlock)
                filled.Add(i);
        }
        return (writes, layers);
    }

    // a blocked cell is solid enough to bear the next (a full block of the heap's from before)
    private static bool Full(TailingsCell cell) => cell.Layers >= TailingsBlocks.LayersPerBlock;

    private static int IndexOf(Int3 offset)
    {
        for (int i = 0; i < Offsets.Count; i++)
            if (Offsets[i] == offset)
                return i;
        return -1;
    }
}
