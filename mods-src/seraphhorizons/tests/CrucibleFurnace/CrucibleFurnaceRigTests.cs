using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CrucibleFurnace;

/// <summary>
/// The crucible furnace's shipped rig (written by CrucibleFurnace/tools/make_shape.py) read by the
/// gameplay (#796): its cells, anchors, build and bricks parse with the contract's values; every part
/// and driver parses with the furnace's <c>requires</c> vocabulary, and every reference pose gives the
/// matrices the Python reference maths wrote; the settings agree with the model (a pot's units, the
/// coke's layers, the chimney, the row); and the lid's boxes measured from the shape.
/// </summary>
public class CrucibleFurnaceRigTests
{
    private static string File(string name) => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static CrucibleFurnaceRig Rig() => CrucibleFurnaceRig.Parse(File("cruciblefurnace-rig.json"));

    [Fact]
    public void The_shipped_rig_parses_with_the_contracts_anchors()
    {
        var rig = Rig();
        Assert.Equal(24, rig.Cells.Count);                       // 18 of brickwork, 6 hollow over the hole's rows
        Assert.Equal(6, rig.HollowCells.Count());
        Assert.All(rig.HollowCells, c => Assert.Equal(2, c.Pos.Y));
        Assert.All(rig.HollowCells, c => Assert.InRange(c.Pos.Z, 0, 1));
        Assert.Equal(3, rig.Width);
        Assert.Equal(new Int3(0, 1, 1), rig.HoleCell);
        Assert.Equal(new Int3(0, 1, 2), rig.OutletCell);
        Assert.Equal(new Int3(0, 2, 2), rig.ChimneyBase);
        Assert.DoesNotContain(rig.Cells, c => c.Pos == rig.ChimneyBase);
        Assert.Equal(Side.North, rig.AshSide);
        Assert.Equal(2, rig.PotPoints.Count);
        Assert.True(rig.PotPoints[0].X < rig.PotPoints[1].X);   // pot 1 west
        // a click on the hole goes to the nearer pot
        Assert.Equal(0, rig.NearerPot(new Float3(0.2f, 2, 1.1f)));
        Assert.Equal(1, rig.NearerPot(new Float3(0.8f, 2, 1.1f)));
        Assert.Equal(0, rig.NearerPot(new Float3(-0.3f, 2, 1.4f)));
        Assert.Equal(1, rig.NearerPot(new Float3(1.3f, 2, 0.8f)));
        Assert.Equal(Side.East, CrucibleFurnaceRig.PlacedSide(Side.East));
    }

    [Fact]
    public void The_build_is_the_owners()
    {
        var rig = Rig();
        Assert.Equal(new[] { "brickwork", "lining", "lid" }, rig.Stages.Select(s => s.Requires));
        Assert.Equal(new[] { new StageMaterial("game:burnedbrick-*", 25) }, rig.Stages[0].Materials);
        Assert.True(rig.Stages[0].OneType);
        Assert.Equal(new[] { "hinge" }, rig.Stages[0].AlsoParts);
        Assert.Equal(new[] { new StageMaterial("game:refractorybrick-fired-tier3", 9), new StageMaterial("game:clay-fire", 4) }, rig.Stages[1].Materials);
        Assert.Equal(new[] { new StageMaterial("game:refractorybrick-fired-tier3", 4), new StageMaterial("game:metalplate-iron", 2) }, rig.Stages[2].Materials);
        Assert.Equal(new[] { "damperopen", "damperhalf", "dampershut" }, rig.Stages[2].Also);
        Assert.Equal(new StageMaterial("game:metalplate-iron", 1), rig.OutletCap);
        Assert.Equal(new[] { new StageMaterial("game:metalplate-iron", 1), new StageMaterial("game:rod-iron", 4) },
            rig.FrameIngredients);
        Assert.Equal(new[] { "fire", "black", "brown", "cream", "gray", "orange", "red", "tan", "clinker" }, rig.Bricks.Types.Select(t => t.Id));
        Assert.Equal("red", rig.Bricks.Default);
        Assert.Equal("brick", rig.Bricks.TextureCode);
        Assert.Equal(6, rig.Bricks.Types.Count(t => t.Overlays.Count == 1 && t.Base.EndsWith("/cream1", StringComparison.Ordinal)));
    }

    // The model's figures are the settings' defaults: a test the model's README asks for.
    [Fact]
    public void The_settings_agree_with_the_model()
    {
        var f = Rig().Figures;
        var c = new CrucibleFurnaceConfig();
        Assert.Equal(c.PotCapacityUnits, f.PotUnits);
        Assert.Equal(c.CokeCapacity, f.CokeLayers * f.CokePerLayer);
        Assert.Equal(c.MinChimneyHeight, f.MinChimney);
        Assert.Equal(c.MaxFurnacesInRow, f.RowMax);
        Assert.Equal(2, f.Pots);
    }

    [Fact]
    public void Every_requires_of_the_rig_is_known_and_each_draws_something()
    {
        var parts = Rig().MovingParts;
        var used = parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Equal(FurnaceRequires.KnownRequires.OrderBy(s => s), used.OrderBy(s => s));
        Assert.True(parts.IndexOf("frame") >= 0);
        Assert.True(parts.IndexOf(FurnaceRequires.Lid) >= 0);
    }

    [Fact]
    public void The_shipped_rig_is_refused_when_it_is_not_the_contracts()
    {
        var json = File("cruciblefurnace-rig.json");
        CrucibleFurnaceRig.Parse(json);
        Assert.Throws<FormatException>(() => CrucibleFurnaceRig.Parse(json.Replace("\"rowAxis\": \"x\"", "\"rowAxis\": \"z\"")));
        Assert.Throws<FormatException>(() => CrucibleFurnaceRig.Parse(json.Replace("\"default\": \"red\"", "\"default\": \"blue\"")));
        Assert.Throws<FormatException>(() => CrucibleFurnaceRig.Parse(json.Replace("\"requires\": \"outletcap\"", "\"requires\": \"chimney\"")));
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Rig().MovingParts;
        using var doc = JsonDocument.Parse(File("cruciblefurnace-rig-reference.json"));
        int poses = 0;
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            var input = new RigInput(pose.GetProperty("theta").GetDouble(), Depth: pose.GetProperty("depth").GetDouble());
            var mats = parts.Matrices(input);
            var expected = pose.GetProperty("matrices");
            Assert.Equal(parts.Parts.Count, expected.EnumerateObject().Count());
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        Assert.True(Math.Abs(mats[i][col * 4 + row] - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference says {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 10, $"only {poses} poses");
    }

    [Fact]
    public void The_lids_boxes_stand_in_the_hollow_cells()
    {
        var rig = Rig();
        int lid = rig.MovingParts.IndexOf(FurnaceRequires.Lid);
        var elements = LidBoxes.Elements(File("cruciblefurnace-shape.json"), rig.MovingParts.Parts[lid]);
        Assert.NotEmpty(elements);
        var open = LidBoxes.At(elements, rig.MovingParts, lid, 0)!.Value;
        var shut = LidBoxes.At(elements, rig.MovingParts, lid, 1)!.Value;
        var swept = LidBoxes.Swept(elements, rig.MovingParts, lid)!.Value;
        // open, it stands over the middle row, about 15.5 voxels high, over the floor (y 2)
        Assert.InRange(open.Y1, 1.9f, 2.05f);
        Assert.InRange(open.Y2, 2.85f, 3.05f);
        Assert.InRange(open.Z1, 1f, 2f);
        Assert.InRange(open.Z2, 1f, 2f);
        Assert.InRange(open.X1, -0.5f, -0.3f);
        Assert.InRange(open.X2, 1.3f, 1.5f);
        // shut, it is flush with the floor (the knuckles stand a little over it)
        Assert.InRange(shut.Y2, 2f, 2.05f);
        Assert.True(shut.Z1 < 1);
        // its swing reaches the front row's cells
        Assert.True(swept.Z1 < 1 && swept.Z2 >= open.Z2 - 1e-4 && swept.Y2 >= open.Y2 - 1e-4);
        var cells = CrucibleFurnaceRig.ByCell(swept, rig.HollowCells.Select(c => c.Pos));
        Assert.Contains(new Int3(0, 2, 0), cells.Keys);
        Assert.Contains(new Int3(-1, 2, 1), cells.Keys);
        Assert.All(cells.Values, b => Assert.True(b.X1 >= 0 && b.X2 <= 1 && b.Y1 >= 0 && b.Y2 <= 1 && b.Z1 >= 0 && b.Z2 <= 1));
        // nothing reaches the chimney's row
        Assert.True(swept.Z2 <= 2);
    }
}

/// <summary>The furnace's build (#796), config/builds/cruciblefurnace.json on the shared staged build: the rig's
/// stages in order with their materials and counts, the brick type rule, what breaking gives back, and saving.</summary>
public class FurnaceBuildTests
{
    private static CrucibleFurnaceRig Rig() =>
        CrucibleFurnaceRig.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cruciblefurnace-rig.json")));

    private static MachineBuild Build() =>
        MachineBuildJson.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "cruciblefurnace.json")));

    private static MachineAssembly New() => new(Build());

    [Fact]
    public void The_build_file_is_the_rigs_stages()
    {
        var rig = Rig();
        var build = Build();
        Assert.Null(build.Block);                         // no generic construction record in the export
        Assert.Equal(rig.Stages.Select(s => s.Requires), build.Stages.Select(s => s.Id));
        Assert.Equal(rig.Stages.Select(s => new[] { s.Requires }), build.Stages.Select(s => s.Adds.ToArray()));
        for (int s = 0; s < rig.Stages.Count; s++)
        {
            var stage = rig.Stages[s];
            Assert.Equal(stage.Materials.Select(m => m.Quantity), build.Stages[s].Materials.Select(m => m.Quantity));
            Assert.Equal(stage.OneType, build.Stages[s].Materials.Any(m => m.OneType));
            for (int m = 0; m < stage.Materials.Count; m++)
            {
                var codes = build.Stages[s].Materials[m].Codes;
                // a brick of every type the rig has a texture for, the default first (the creative shortcut's)
                var expected = stage.OneType
                    ? rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).OrderBy(c => c != rig.Bricks.ItemOf(rig.Bricks.Default)).ToList()
                    : [stage.Materials[m].Code];
                Assert.Equal(expected.Order(), codes.Order());
                Assert.Equal(expected[0], codes[0]);
                Assert.All(codes, c => Assert.True(stage.Materials[m].Matches(c)));
            }
        }
    }

    [Fact]
    public void Each_stage_takes_what_it_needs_in_order()
    {
        var b = New();
        Assert.Equal(0, b.Build.Next);
        Assert.Equal(AssemblyVerdict.TooFew, b.Fit("game:burnedbrick-red", 16, 64).Verdict);
        var fit = b.Fit("game:burnedbrick-red", 64, 64);
        Assert.Equal((AssemblyVerdict.Fitted, 25), (fit.Verdict, fit.Take));   // what the brickwork needs of a stack
        Assert.True(b.Shows("brickwork"));
        Assert.Equal(1, b.Build.Next);
        // the lining takes its two materials in any order
        Assert.Equal(4, b.Fit("game:clay-fire", 10, 64).Take);
        Assert.Equal(AssemblyVerdict.AlreadyFitted, b.Fit("game:clay-fire", 1, 64).Verdict);
        Assert.Equal(9, b.Fit("game:refractorybrick-fired-tier3", 20, 64).Take);
        Assert.Equal(2, b.Build.Next);
        Assert.False(b.Shows("lid"));
        Assert.Equal(4, b.Fit("game:refractorybrick-fired-tier3", 20, 64).Take);
        Assert.Equal(2, b.Fit("game:metalplate-iron", 3, 64).Take);
        Assert.True(b.Built);
        Assert.True(b.Shows("lid"));
        Assert.True(b.Shows("damperhalf"));               // gameplay's, not the build's
        Assert.Equal(AssemblyVerdict.AlreadyFitted, b.Fit("game:burnedbrick-red", 1, 64).Verdict);
    }

    [Fact]
    public void Stages_go_in_order()
    {
        var b = New();
        Assert.Equal(AssemblyVerdict.OutOfOrder, b.Fit("game:refractorybrick-fired-tier3", 9, 64).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, b.Fit("game:metalplate-iron", 2, 64).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, b.Fit("game:ingot-iron", 2, 64).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, b.Fit(null, 2, 64).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, b.Fit("game:clay-fire", 4, 64).Verdict);
        Assert.Empty(b.Returns());
        Assert.False(b.Shows("lining"));
        Assert.True(b.Shows(null));
        Assert.True(b.Shows("pot1"));                     // gameplay's, not the build's
    }

    [Fact]
    public void The_first_brick_sets_the_type_and_others_are_refused()
    {
        var rig = Rig();
        var b = New();
        Assert.Equal(AssemblyVerdict.Fitted, b.Fit("game:burnedbrick-black", 3, 3).Verdict);   // a whole stack short of it
        Assert.Equal("black", rig.Bricks.TypeOf(b.Build.CodeIn(0, 0)));
        Assert.Equal(AssemblyVerdict.OtherKind, b.Fit("game:burnedbrick-red", 64, 64).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, b.Fit("game:burnedbrick-blue", 64, 64).Verdict);   // no texture for it
        Assert.Equal(AssemblyVerdict.Fitted, b.Fit("burnedbrick-black", 64, 64).Verdict);       // the game's domain
        // a fresh furnace takes any known type first
        foreach (var type in rig.Bricks.Types)
            Assert.Equal(AssemblyVerdict.Fitted, New().Fit("game:burnedbrick-" + type.Id, 25, 64).Verdict);
    }

    [Fact]
    public void Breaking_gives_back_every_fitted_item_and_saving_keeps_them()
    {
        var b = New();
        b.Fit("game:burnedbrick-tan", 25, 64);
        b.Fit("game:refractorybrick-fired-tier3", 64, 64);
        Assert.Equal(new[] { new StageDrop("game:burnedbrick-tan", 25), new StageDrop("game:refractorybrick-fired-tier3", 9) }, b.Returns());
        var (build, wearing) = b.Snapshot();
        var back = MachineAssembly.Restore(b.Spec, build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.Equal(b.Returns(), back.Returns());
        Assert.Equal("game:burnedbrick-tan", back.Build.CodeIn(0, 0));
        Assert.Equal(1, back.Build.Next);
    }

    [Fact]
    public void The_creative_shortcut_fills_the_next_stage()
    {
        var b = New();
        Assert.True(b.FillNext());
        Assert.Equal("game:burnedbrick-red", b.Build.CodeIn(0, 0));   // the rig's default type
        Assert.True(b.FillNext());
        Assert.True(b.FillNext());
        Assert.True(b.Built);
        Assert.False(b.FillNext());
        // breaking gives back every item, by its code
        Assert.Equal(new[] { new StageDrop("game:burnedbrick-red", 25), new StageDrop("game:refractorybrick-fired-tier3", 13),
            new StageDrop("game:clay-fire", 4), new StageDrop("game:metalplate-iron", 2) }, b.Returns());
    }
}

/// <summary>Two pots in one hole (#796): each its own charge on one fire, melting only while the fire
/// is at its own recipe's temperature.</summary>
public class FurnacePotTests
{
    [Fact]
    public void Two_pots_of_different_recipes_melt_at_their_own_temperatures()
    {
        var recipes = PotRecipes.Default;
        var c = new CrucibleFurnaceConfig();
        var stainless = new FurnacePot();
        Assert.Equal(12, stainless.Add(recipes, Stainless.Ferrochrome, 12, 300).Count);
        Assert.Equal(2, stainless.Add(recipes, "game:ingot-iron", 2, 300).Count);
        Assert.Equal(8, stainless.Add(recipes, "game:metalbit-iron", 8, 300).Count);
        var iron = new FurnacePot();
        Assert.Equal(3, iron.Add(recipes, "game:ingot-iron", 5, 300).Count);
        Assert.True(stainless.ChargeMakesSomething(recipes) && iron.ChargeMakesSomething(recipes));
        // at 1535 °C the stainless (1530) melts, in time, and the iron (1538) does not start
        static double? Solid(string r) => null;
        for (int i = 0; i < 200 && stainless.Melted == null; i++)
        {
            FurnaceMelt.Step(stainless, recipes, 0.05, 1535, 1535, Solid, c);
            FurnaceMelt.Step(iron, recipes, 0.05, 1535, 1535, Solid, c);
        }
        Assert.Equal("stainless", stainless.Melted);
        Assert.Equal(300, stainless.MeltedUnits);
        Assert.Empty(stainless.Charge);
        Assert.Null(iron.Melted);
        Assert.Equal(0, iron.Progress);
        for (int i = 0; i < 200 && iron.Melted == null; i++)
            FurnaceMelt.Step(iron, recipes, 0.05, 1540, 1540, Solid, c);
        Assert.Equal("iron", iron.Melted);
        Assert.False(FurnaceMelt.Step(iron, recipes, 1, 1600, 1600, Solid, c));       // once
        var bad = new FurnacePot();
        bad.Add(recipes, Stainless.Ferrochrome, 2, 300);
        bad.Add(recipes, "game:ingot-iron", 2, 300);
        Assert.False(bad.ChargeMakesSomething(recipes));
        Assert.True(new FurnacePot().ChargeMakesSomething(recipes));
    }
}
