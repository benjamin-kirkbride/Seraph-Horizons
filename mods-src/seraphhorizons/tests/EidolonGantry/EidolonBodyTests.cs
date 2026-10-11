using System.Text.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry.Core;
using Xunit;

namespace SeraphHorizons.Tests.EidolonGantry;

/// <summary>The eidolon's body on the gantry's spine (<see cref="BodyParts"/>, <see cref="BodyBill"/>): the
/// torso, the five layers in a material of the builder's choice, the mind; held to
/// config/eidolon-stages.json (Eidolon/tools/make_shape.py) and the torso's grid recipe.</summary>
public class EidolonBodyTests
{
    private const string Beam = "game:supportbeam-oak";
    private const string Gear = EidolonFittings.StainlessGear;

    private static JsonElement StagesFile()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "eidolon-stages.json")));
        return doc.RootElement.Clone();
    }

    private static BodyIngredient Parse(string text)
    {
        int space = text.IndexOf(' ');
        return space > 0 ? new BodyIngredient(text[(space + 1)..], int.Parse(text[..space])) : new BodyIngredient(text, 1);
    }

    [Fact]
    public void The_bill_is_the_stages_files_one_source()
    {
        var stages = StagesFile().GetProperty("stages").EnumerateArray()
            .Where(s => s.GetProperty("code").GetString() != "gantry").ToList();
        Assert.Equal(BodyBill.Order, stages.Select(s => s.GetProperty("code").GetString()));
        Assert.Equal(["torso", .. EidolonFittings.Layers.Select(EidolonFittings.TextureCode), "mind"], BodyBill.Order);
        foreach (var stage in stages)
        {
            string code = stage.GetProperty("code").GetString()!;
            // a layer's {tokens} are the builder's choice of material; the file's are the default build's
            var material = stage.TryGetProperty("material", out var m)
                ? m.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
                : new Dictionary<string, string>();
            var listed = stage.GetProperty("ingredients").EnumerateArray()
                .Select(e => Parse(material.Aggregate(e.GetString()!, (s, kv) => s.Replace("{" + kv.Key + "}", kv.Value))));
            Assert.Equal(BodyBill.Of(code), listed);
            if (BodyBill.LayerOf(code) is { } layer)
            {
                // the model's measure is the counts' source: FittingCounts holds what it rounds to
                Assert.Equal(FittingCounts.Of(layer), stage.GetProperty("measure").EnumerateArray().Select(x => x.GetProperty("count").GetInt32()));
                Assert.Equal(EidolonFittings.TextureCode(layer), stage.GetProperty("texture").GetString());
            }
        }
        var torso = stages.Single(s => s.GetProperty("code").GetString() == "torso");
        Assert.Equal(EidolonFittings.TorsoRecipe.Select(i => new BodyIngredient(i.Code, i.Count)),
            torso.GetProperty("recipe").EnumerateArray().Select(e => Parse(e.GetString()!)));
    }

    [Fact]
    public void The_torsos_grid_recipe_is_the_torso_recipe()
    {
        var recipes = JArray.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "eidolontorso-recipe.json")),
            new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        var recipe = (JObject)Assert.Single(recipes);
        Assert.Equal(BodyBill.Torso, (string?)recipe["output"]!["code"]);
        string pattern = ((string)recipe["ingredientPattern"]!).Replace(",", "");
        var keys = (JObject)recipe["ingredients"]!;
        var used = pattern.Where(c => c != '_').Select(c => keys[c.ToString()]!)
            .Select(i => new FittingIngredient((string)i["code"]!, (int?)i["quantity"] ?? 1));
        Assert.Equal(EidolonFittings.TorsoRecipe.OrderBy(i => i.Code), used.OrderBy(i => i.Code));
        // the metal parts are a block (game:metal-parts), the rest items
        Assert.All(keys.Properties(), k => Assert.Equal((string)k.Value["code"]! == EidolonFittings.MetalParts ? "block" : "item", (string?)k.Value["type"]));
        // the eidolon schematic gates it, as the gantry's frame
        var gates = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schematic-gates.json")),
            new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        var eidolon = gates["gates"]!.Single(g => (string?)g["machine"] == "eidolon");
        Assert.Contains(eidolon["outputs"]!, o => (string?)o["code"] == BodyBill.Torso);
    }

    [Fact]
    public void The_bill_keeps_the_epics_jonas_parts()
    {
        // the torso is crafted (3 of the 12 stainless gears and 4 of the 16 metal parts are in its recipe)
        var all = BodyBill.Order.SelectMany(s => BodyBill.Of(s)).GroupBy(i => i.Code).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        Assert.Equal([new BodyIngredient(BodyBill.Torso, 1)], BodyBill.Of("torso"));
        Assert.Equal(9, all[Gear]);
        Assert.Equal(12, all[EidolonFittings.MetalParts]);
        Assert.Contains(new BodyIngredient(EidolonFittings.Vessel, 1), BodyBill.Of("bones"));
        Assert.Equal([new BodyIngredient(BodyBill.TemporalGear, 1)], BodyBill.Of(BodyBill.Last));
        // each code once a stage, in every material, so a click's ingredient is its code
        foreach (var layer in EidolonFittings.Layers)
            foreach (var material in EidolonFittings.Materials(layer))
            {
                var bill = BodyBill.Of(BodyBill.StageOf(layer), material);
                Assert.Equal(bill.Count, bill.Select(i => i.Code).Distinct().Count());
            }
    }

    private static BodyParts WithTorso()
    {
        var body = new BodyParts();
        Assert.Equal(1, body.Fit(BodyBill.Torso, 1));
        return body;
    }

    private static void FitAll(BodyParts body, string stage, string? material = null)
    {
        foreach (var i in BodyBill.Of(stage, material))
            Assert.Equal(i.Count - body.Fitted(stage, i.Code), body.Fit(i.Code, 64));
        Assert.True(body.StageComplete(stage));
    }

    [Fact]
    public void Stages_go_in_order_any_order_within_and_a_click_takes_what_is_held_up_to_what_is_needed()
    {
        var body = new BodyParts();
        Assert.Equal("torso", body.Next);
        Assert.False(body.Started);
        // a later stage's item, and one no stage takes
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit("game:jonasframes-gearbox02", 1, out _, out _));
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit("game:metalplate-brass", 1, out _, out _));
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit(BodyBill.TemporalGear, 1, out _, out _));
        Assert.Equal(BodyFitVerdict.NotAPart, body.CanFit("game:metalplate-copper", 1, out _, out _));
        Assert.Equal(BodyFitVerdict.NotAPart, body.CanFit(null, 1, out _, out _));
        // the torso is one placed item
        Assert.Equal(1, body.Fit(BodyBill.Torso, 1));
        Assert.True(body.HasTorso);
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit(BodyBill.Torso, 1, out _, out _));
        Assert.Equal("bones", body.Next);
        // the bones take 30 beams: three held go in, then a whole stack of sixteen, then eleven of the next
        Assert.Equal(3, body.Fit("supportbeam-oak", 3));
        Assert.Equal(BodyFitVerdict.Fits, body.CanFit(Beam, 16, out var stage, out int take));
        Assert.Equal(("bones", 16), (stage, take));
        Assert.Equal(16, body.Fit(Beam, 16));
        Assert.Equal(BodyFitVerdict.Fits, body.CanFit(Beam, 16, out stage, out take));
        Assert.Equal(("bones", 11), (stage, take));
        Assert.Equal(11, body.Fit(Beam, 16));
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit(Beam, 1, out _, out _));
        Assert.True(body.Started);
        Assert.False(body.StageComplete("bones"));
        FitAll(body, "bones", "oak");
        Assert.Equal("joints", body.Next);
        // the vessel is the bones' only: nothing more of it goes in
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit(EidolonFittings.Vessel, 1, out _, out _));
    }

    [Fact]
    public void A_layers_first_piece_fixes_its_material_and_the_rest_must_match()
    {
        var body = WithTorso();
        // before any material the layer shows its default, any material allowed
        Assert.Null(body.Material(FittingLayer.Bones));
        Assert.Contains(body.StillNeeded("bones"), i => i is { Code: Beam, Count: 30, AnyMaterial: true });
        // rotten wood is never bones; a beam of another wood than the first is refused
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:supportbeam-rotten", 1, out var stage, out _));
        Assert.Equal("bones", stage);
        // the Jonas parts go in whatever the material, and do not fix it
        Assert.Equal(1, body.Fit("game:jonasframes-gearbox01", 1));
        Assert.Null(body.Material(FittingLayer.Bones));
        Assert.Equal(2, body.Fit("game:supportbeam-veryaged", 2));
        Assert.Equal("veryaged", body.Material(FittingLayer.Bones));
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit(Beam, 8, out _, out _));
        Assert.Equal(0, body.Fit(Beam, 8));
        Assert.Contains(body.StillNeeded("bones"), i => i is { Code: "game:supportbeam-veryaged", Count: 28, AnyMaterial: false });
        FitAll(body, "bones", "veryaged");

        // joints: rods and nails of one metal
        Assert.Equal(2, body.Fit("game:rod-stainlesssteel", 2));
        Assert.Equal(EidolonFittings.Stainless, body.Material(FittingLayer.Joints));
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:metalnailsandstrips-iron", 20, out _, out _));
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:rod-copper", 1, out _, out _));
        // a material of a later layer in this one's metal is out of order, not the wrong material
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit("game:metalplate-iron", 1, out _, out _));
        FitAll(body, "joints", EidolonFittings.Stainless);

        // plates in brass, then a sash of green cloth, then an iron skirt
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:metalplate-copper", 1, out _, out _));
        FitAll(body, "plates", EidolonFittings.Brass);
        Assert.Equal(1, body.Fit("game:cloth-green", 1));
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:leather-normal-green", 1, out _, out _));
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:cloth-red", 1, out _, out _));
        FitAll(body, "sash", "cloth-green");
        FitAll(body, "skirt", EidolonFittings.Iron);
        Assert.Equal("mind", body.Next);
        Assert.False(body.Complete);
        Assert.Equal(1, body.Fit(BodyBill.TemporalGear, 1));
        Assert.True(body.Complete);
        Assert.Equal(EidolonBuild.Of("veryaged", EidolonFittings.Stainless, EidolonFittings.Brass, "cloth-green", EidolonFittings.Iron), body.Build);
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit(BodyBill.TemporalGear, 1, out _, out _));
    }

    [Fact]
    public void The_build_is_the_layers_complete_so_far()
    {
        var body = WithTorso();
        Assert.Equal(EidolonBuild.Bare, body.Build);
        FitAll(body, "bones", "oak");
        body.Fit("game:metalplate-steel", 3);
        Assert.Equal("oak", body.Build[FittingLayer.Bones]);
        Assert.Null(body.Build[FittingLayer.Joints]);
        Assert.Equal(new Dictionary<string, string> { ["bones"] = "game:block/wood/debarked/oak" }, body.Build.Textures());
    }

    [Fact]
    public void Breaking_returns_everything_fitted_and_clearing_empties_the_spine()
    {
        var body = new BodyParts();
        body.FitNextStage();
        body.FitNextStage();
        body.Fit("game:rod-iron", 2);
        body.Fit("game:jonasframes-joint01", 1);
        var returns = body.Returns().ToDictionary(d => d.Code, d => d.Count);
        Assert.Equal(1, returns[BodyBill.Torso]);
        Assert.Equal(30, returns[Beam]);
        Assert.Equal(2, returns["game:rod-iron"]);
        Assert.Equal(1, returns["game:jonasframes-joint01"]);
        Assert.Equal(1 + BodyBill.Of("bones").Sum(i => i.Count) + 3, returns.Values.Sum());

        var restored = BodyParts.Restore(body.Snapshot());
        Assert.Equal(body.Returns(), restored.Returns());
        Assert.Equal("joints", restored.Next);
        Assert.Equal(EidolonFittings.Iron, restored.Material(FittingLayer.Joints));

        body.Clear();
        Assert.False(body.Started);
        Assert.Empty(body.Returns());
        Assert.Equal("torso", body.Next);
    }

    [Fact]
    public void Restoring_drops_what_a_stage_does_not_take_caps_counts_and_keeps_one_material()
    {
        var restored = BodyParts.Restore(new Dictionary<string, int>
        {
            ["plates/game:metalplate-brass"] = 99,
            ["plates/game:jonasframes-gearbox02"] = 1,
            ["plates/game:metalplate-copper"] = 1,
            ["wings/game:metalplate-steel"] = 1,
            ["torso/seraphhorizons:eidolontorso"] = 1,
            ["plates"] = 1,
        });
        Assert.Equal([new GantryDrop(BodyBill.Torso, 1), new GantryDrop("game:metalplate-brass", 17)], restored.Returns());
        Assert.Equal(EidolonFittings.Brass, restored.Material(FittingLayer.Plates));
    }

    [Fact]
    public void Creative_fills_stage_by_stage_in_the_default_or_chosen_material()
    {
        var body = WithTorso();
        body.Fit("game:supportbeam-pine", 1);
        foreach (var stage in BodyBill.Order.Skip(1))
        {
            Assert.Equal(stage, body.Next);
            Assert.Equal(stage, body.FitNextStage());
        }
        Assert.True(body.Complete);
        Assert.Null(body.FitNextStage());
        Assert.Equal(EidolonBuild.Of("pine", EidolonFittings.Steel, EidolonFittings.Steel, "leather-plain", EidolonFittings.Steel), body.Build);
    }

    // ---- Old builds ----

    /// <summary>The limb-by-limb bill a body was built to before fittings (#672), all steel.</summary>
    private static readonly Dictionary<string, (string Code, int Count)[]> OldBill = new()
    {
        ["torso"] =
        [
            ("game:jonasparts-tank01", 1), ("game:jonasparts-tank02", 1), ("game:jonasparts-pumphead", 1),
            ("game:metalplate-steel", 4), ("game:metalnailsandstrips-steel", 8), ("game:metal-parts", 4), (Gear, 3),
        ],
        ["pelvis"] = [("game:jonasframes-gearbox02", 1), ("game:metalplate-steel", 3), ("game:metalnailsandstrips-steel", 8), ("game:metal-parts", 3), (Gear, 2)],
        ["legs"] =
        [
            ("game:jonasframes-joint01", 2), ("game:jonasframes-spring01", 2), ("game:rod-steel", 4), ("game:metalplate-steel", 3),
            ("game:metalnailsandstrips-steel", 8), ("game:metal-parts", 3), (Gear, 2),
        ],
        ["arms"] =
        [
            ("game:jonasframes-gears01", 1), ("game:jonasframes-gears02", 1), ("game:jonasparts-cylinder01", 1), ("game:jonasparts-valve01", 1),
            ("game:rod-steel", 4), ("game:metalplate-steel", 2), ("game:metalnailsandstrips-steel", 8), ("game:metal-parts", 4), (Gear, 3),
        ],
        ["head"] =
        [
            (EidolonFittings.Vessel, 1), ("game:jonasframes-oscillator01", 1), ("game:jonasframes-gearbox01", 1), ("game:jonasparts-cylinder02", 1),
            ("game:jonasparts-connector01", 1), ("game:metalplate-steel", 1), ("game:metalnailsandstrips-steel", 4), ("game:metal-parts", 2), (Gear, 2),
        ],
    };

    private static Dictionary<string, int> OldSave(params string[] stages) =>
        stages.SelectMany(s => OldBill[s].Select(i => (Key: s + "/" + i.Code, i.Count))).ToDictionary(x => x.Key, x => x.Count);

    private static Dictionary<string, int> Totals(IEnumerable<GantryDrop> drops) =>
        drops.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Sum(d => d.Count));

    [Fact]
    public void An_old_body_loads_as_the_new_stages_in_steel_and_loses_nothing()
    {
        var save = OldSave("torso", "pelvis", "legs", "arms", "head");
        var body = BodyParts.Restore(save);
        // the old torso is the crafted torso now
        Assert.True(body.HasTorso);
        Assert.Equal("bones", body.Next);
        // the bones' frame and vessel are in; the bones wait for their beams
        foreach (var i in EidolonFittings.JonasParts(FittingLayer.Bones))
            Assert.Equal(i.Count, body.Fitted("bones", i.Code));
        Assert.Equal([new BodyIngredient(Beam, 30, AnyMaterial: true)], body.StillNeeded("bones"));
        // the joints are whole in steel, the plates have the old 13 of their 17
        Assert.True(body.StageComplete("joints"));
        Assert.Equal(EidolonFittings.Steel, body.Material(FittingLayer.Joints));
        Assert.Equal(13, body.Fitted("plates", "game:metalplate-steel"));
        Assert.Equal(EidolonFittings.Steel, body.Material(FittingLayer.Plates));
        // the rods and nails the joints do not take are spare, given back
        Assert.Equal([new GantryDrop("game:metalnailsandstrips-steel", 16), new GantryDrop("game:rod-steel", 4)],
            body.Spare.OrderBy(d => d.Code));
        // nothing is lost: breaking returns all the old parts but the torso's own, which are the torso
        var expected = Totals(save.Select(kv => new GantryDrop(kv.Key[(kv.Key.IndexOf('/') + 1)..], kv.Value)));
        foreach (var i in EidolonFittings.TorsoRecipe)
            expected[i.Code] -= i.Count;
        expected[BodyBill.Torso] = 1;
        Assert.Equal(expected.Where(kv => kv.Value > 0).ToDictionary(), Totals(body.Returns()));
        // and a new save of it stays as it is
        var again = BodyParts.Restore(body.Snapshot());
        Assert.Equal(Totals(body.Returns()), Totals(again.Returns()));
        Assert.Equal(body.Spare, again.Spare);
        // with oak beams the rest goes on in order: plates 4 more, then the sash
        FitAll(body, "bones", "oak");
        Assert.Equal("plates", body.Next);
        Assert.Equal(BodyFitVerdict.WrongMaterial, body.CanFit("game:metalplate-iron", 4, out _, out _));
        Assert.Equal(4, body.Fit("game:metalplate-steel", 4));
        Assert.Equal("sash", body.Next);
        Assert.NotEmpty(body.TakeSpare());
        Assert.Empty(body.Spare);
    }

    [Fact]
    public void An_old_body_with_its_torso_half_fitted_gives_it_back()
    {
        var save = OldSave("torso");
        save.Remove("torso/game:jonasparts-tank02");
        var body = BodyParts.Restore(save);
        Assert.False(body.Started);
        Assert.Equal("torso", body.Next);
        Assert.Equal(Totals(save.Select(kv => new GantryDrop(kv.Key["torso/".Length..], kv.Value))), Totals(body.Spare));
        Assert.Equal(Totals(body.Spare), Totals(body.Returns()));
    }

    [Fact]
    public void An_old_body_with_only_its_torso_keeps_it_as_the_torso()
    {
        var body = BodyParts.Restore(OldSave("torso"));
        Assert.True(body.HasTorso);
        Assert.Equal("bones", body.Next);
        // its 4 plates and 8 nails and strips: the plates keep theirs, the joints the nails
        Assert.Equal(4, body.Fitted("plates", "game:metalplate-steel"));
        Assert.Equal(8, body.Fitted("joints", "game:metalnailsandstrips-steel"));
        Assert.Empty(body.Spare);
    }
}
