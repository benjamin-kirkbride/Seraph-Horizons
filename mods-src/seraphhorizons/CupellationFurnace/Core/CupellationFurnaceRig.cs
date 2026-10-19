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
/// <param name="Flue">The outlet's cell, with the chimney (or the cap) on its top, the cap, the brick types the
/// brickwork may be, and the row's chimney rules.</param>
/// <param name="BlastCell">The blast inlet's cell; the pipe is laid beyond <see cref="BlastFace"/>.</param>
/// <param name="BlastFace">The face the pipe meets.</param>
/// <param name="BlastGas">The gas the tuyere takes from the pipe network (<c>Air</c>).</param>
/// <param name="BlastRate">Litres a second it draws while it holds a hot charge.</param>
/// <param name="BlastPressure">The least pressure (atm) of the network it draws at.</param>
/// <param name="TestCharges">Charges a test lasts.</param>
public sealed record CupellationFurnaceRig(FurnaceFlue Flue, Int3 BlastCell, CellFace BlastFace, string BlastGas, double BlastRate,
    double BlastPressure, int TestCharges) : FiredRig(Flue)
{
    public static CupellationFurnaceRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var cells = Cells(root);
        var flue = FurnaceFlue.Parse(root, cells);
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
        int charges = 0;
        foreach (var s in Required(Required(root, "build", JsonValueKind.Object), "stages", JsonValueKind.Array).EnumerateArray())
            if (Str(s, "requires") == CupellationRequires.Test && s.TryGetProperty("wearing", out var w) && w.ValueKind == JsonValueKind.Object)
                charges = Int(w, "charges");
        var rig = new CupellationFurnaceRig(flue, blast, face,
            Str(furnace, "blastGas") ?? throw new FormatException("furnace.blastGas is missing"),
            Num(furnace, "blastRate"), Num(furnace, "blastPressure"), charges);
        if (!(rig.BlastRate > 0) || !(rig.BlastPressure >= 0))
            throw new FormatException("furnace.blastRate must be positive and furnace.blastPressure not negative");
        if (rig.TestCharges < 1)
            throw new FormatException("the test stage's wearing.charges must be positive");
        return rig;
    }
}
