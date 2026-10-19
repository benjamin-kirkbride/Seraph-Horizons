namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// What the shared machine code (<c>BEMachine</c>, <c>BlockMachine</c>, <c>MachineRenderer</c>) reads of
/// a machine's rig, whatever its file's dialect: its cells (native, south-facing frame, the controller at
/// [0,0,0]), its moving parts, and its anchors by name (<see cref="MachineAnchors"/>: <c>power</c>,
/// <c>infeed</c>, <c>water</c>, the outlets). The ore machines' <see cref="OreMachineRig"/> reads every
/// anchor from its <c>{name}Cell</c>/<c>{name}Face</c> pairs; a workshop machine's own rig class maps
/// its keys (the draw bench's <c>powerCell</c> and <c>powerFace</c>) to the same names.
/// </summary>
public interface IMachineRig
{
    IReadOnlyList<RigCell> Cells { get; }

    /// <summary>The moving parts the renderer poses (and the static frame, which it leaves to the block).</summary>
    RigParts Parts { get; }

    /// <summary>Every anchor, by name.</summary>
    IReadOnlyDictionary<string, MachineAnchor> Anchors { get; }

    /// <summary>An anchor by name, or null when the rig has none.</summary>
    MachineAnchor? Anchor(string name);
}

/// <summary>What every <see cref="IMachineRig"/> gives from its cells and anchors.</summary>
public static class MachineRigs
{
    public const string Power = "power";

    /// <summary>The cells other than the controller's, in file order.</summary>
    public static IEnumerable<RigCell> OtherCells(this IMachineRig rig) => rig.Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The power anchor (its cell takes the axle), or null for a machine that takes none.</summary>
    public static MachineAnchor? PowerAnchor(this IMachineRig rig) => rig.Anchor(Power);

    /// <summary>The native axis of the shaft the power face takes: along x for an east or west face, z
    /// for north or south; null for up or down (a vertical shaft) or no power.</summary>
    public static Axis? ShaftAxis(this IMachineRig rig) => rig.PowerAnchor()?.Face switch
    {
        CellFace.East or CellFace.West => Axis.X,
        CellFace.North or CellFace.South => Axis.Z,
        _ => null,
    };

    /// <summary>The middle of the footprint, native: the renderer's distance check.</summary>
    public static Float3 Middle(this IMachineRig rig) => new(
        (float)rig.Cells.Average(c => c.Pos.X) + 0.5f,
        (float)rig.Cells.Average(c => c.Pos.Y) + 0.5f,
        (float)rig.Cells.Average(c => c.Pos.Z) + 0.5f);

    /// <summary>The cell above the highest cell over the footprint's middle: outside the model, so the
    /// parts are lit as the machine around them, not as the inside of one of its cells.</summary>
    public static Int3 LightCell(this IMachineRig rig)
    {
        var middle = rig.Middle();
        int x = (int)MathF.Floor(middle.X), z = (int)MathF.Floor(middle.Z);
        int top = rig.Cells.Where(c => c.Pos.X == x && c.Pos.Z == z).Select(c => c.Pos.Y)
            .DefaultIfEmpty(rig.Cells.Max(c => c.Pos.Y)).Max();
        return new Int3(x, top + 1, z);
    }

    /// <summary>A horizontal side as a cell face.</summary>
    public static CellFace Face(this Side side) => side switch
    {
        Side.North => CellFace.North,
        Side.East => CellFace.East,
        Side.South => CellFace.South,
        _ => CellFace.West,
    };
}
