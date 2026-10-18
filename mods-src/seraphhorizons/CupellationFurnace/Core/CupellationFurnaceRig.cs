using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.CupellationFurnace.Core;

/// <summary>The cupellation furnace's part <c>requires</c> besides its stages' (<c>brickwork</c>, <c>lining</c>,
/// <c>fittings</c>) and its test's (<c>test</c>, the wearing part): gameplay's state (CupellationFurnace/README.md,
/// "Rig schema"). The frame block type lists them as its <c>extraRequires</c>.</summary>
public static class CupellationRequires
{
    public const string Brickwork = "brickwork", Lining = "lining", Fittings = "fittings", Test = "test";
    public const string Coal = "coal", Fire = "fire", Lead = "lead", Silver = "silver", Stream = "stream", Litharge = "litharge";
    public const string OutletCap = "outletcap";

    public static string DamperPart(Damper d) => FurnaceRequires.DamperPart(d);

    /// <summary>Every one of them, for <c>extraRequires</c>.</summary>
    public static readonly IReadOnlyList<string> Extra =
        [Coal, Fire, Lead, Silver, Stream, Litharge, "damperopen", "damperhalf", "dampershut", OutletCap];
}

/// <summary>
/// What gameplay reads of <c>config/cupellationfurnace-rig.json</c> besides the shared ore machine rig's cells,
/// anchors and parts (written by CupellationFurnace/tools/make_shape.py; CupellationFurnace/README.md, "Rig schema"):
/// the outlet's cell, the cap, the brick types, the row's chimney rules, the blast and the test's wear. Throws
/// <see cref="FormatException"/> naming what is wrong.
/// </summary>
/// <param name="OutletCell">The stack's top cell; the chimney (or the cap) on its top.</param>
/// <param name="BlastCell">The blast inlet's cell; the pipe is laid beyond <see cref="BlastFace"/>.</param>
/// <param name="BlastFace">The face the pipe meets.</param>
/// <param name="CapCode">The outlet cap's item.</param>
/// <param name="CapCount">How many of it.</param>
/// <param name="Bricks">The fired brick types the brickwork may be.</param>
/// <param name="MinChimney">Chimney blocks counted from the outlet up a row needs.</param>
/// <param name="RowMax">Furnaces side by side one chimney serves.</param>
/// <param name="Width">The furnace's width along x, the row's axis.</param>
/// <param name="BlastGas">The gas the tuyere takes from the pipe network (<c>Air</c>).</param>
/// <param name="BlastRate">Litres a second it draws while it holds a hot charge.</param>
/// <param name="BlastPressure">The least pressure (atm) of the network it draws at.</param>
/// <param name="TestCharges">Charges a test lasts.</param>
public sealed record CupellationFurnaceRig(Int3 OutletCell, Int3 BlastCell, CellFace BlastFace, string CapCode, int CapCount,
    BrickTypes Bricks, int MinChimney, int RowMax, int Width, string BlastGas, double BlastRate, double BlastPressure, int TestCharges)
{
    /// <summary>The first cell of the chimney, over the outlet.</summary>
    public Int3 ChimneyBase => OutletCell + new Int3(0, 1, 0);

    public static CupellationFurnaceRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        if (Str(root, "outletFace") != "up")
            throw new FormatException("outletFace must be \"up\"");
        var cells = Cells(root);
        var outlet = Int3Of(Required(root, "outletCell", JsonValueKind.Array), "outletCell");
        if (cells.All(c => c.Pos != outlet))
            throw new FormatException($"outletCell {outlet} is not a cell of the footprint");
        if (cells.Any(c => c.Pos == outlet + new Int3(0, 1, 0)))
            throw new FormatException("the cell over the outlet is the furnace's: the chimney stands there");
        var blast = Int3Of(Required(root, "blastCell", JsonValueKind.Array), "blastCell");
        if (cells.All(c => c.Pos != blast))
            throw new FormatException($"blastCell {blast} is not a cell of the footprint");
        var face = Str(root, "blastFace") switch
        {
            "north" => CellFace.North,
            "east" => CellFace.East,
            "south" => CellFace.South,
            "west" => CellFace.West,
            var f => throw new FormatException($"blastFace \"{f}\" is not a side"),
        };
        var furnace = Required(root, "furnace", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        double Num(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetDouble();
        if (Str(furnace, "rowAxis") != "x")
            throw new FormatException("furnace.rowAxis must be x");
        var build = Required(root, "build", JsonValueKind.Object);
        var cap = Required(build, "outletCap", JsonValueKind.Object);
        int charges = 0;
        foreach (var s in Required(build, "stages", JsonValueKind.Array).EnumerateArray())
            if (Str(s, "requires") == CupellationRequires.Test && s.TryGetProperty("wearing", out var w) && w.ValueKind == JsonValueKind.Object)
                charges = Int(w, "charges");
        var rig = new CupellationFurnaceRig(outlet, blast, face,
            Str(cap, "code") ?? throw new FormatException("build.outletCap has no code"), Int(cap, "quantity"),
            BrickTypes.Parse(Required(root, "bricks", JsonValueKind.Object)),
            Int(furnace, "minChimney"), Int(furnace, "rowMax"),
            cells.Max(c => c.Pos.X) - cells.Min(c => c.Pos.X) + 1,
            Str(furnace, "blastGas") ?? throw new FormatException("furnace.blastGas is missing"),
            Num(furnace, "blastRate"), Num(furnace, "blastPressure"), charges);
        if (rig.MinChimney < 1 || rig.RowMax < 1 || rig.CapCount < 1)
            throw new FormatException("furnace.minChimney, furnace.rowMax and build.outletCap.quantity must be positive");
        if (!(rig.BlastRate > 0) || !(rig.BlastPressure >= 0))
            throw new FormatException("furnace.blastRate must be positive and furnace.blastPressure not negative");
        if (rig.TestCharges < 1)
            throw new FormatException("the test stage's wearing.charges must be positive");
        return rig;
    }
}
