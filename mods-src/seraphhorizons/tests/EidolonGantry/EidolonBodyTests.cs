using System.Text.Json;
using SeraphHorizons.Mod.EidolonGantry.Core;
using Xunit;

namespace SeraphHorizons.Tests.EidolonGantry;

/// <summary>The eidolon's body stages on the gantry's spine (<see cref="BodyParts"/>, <see cref="BodyBill"/>),
/// held to config/eidolon-stages.json (Eidolon/tools/make_shape.py).</summary>
public class EidolonBodyTests
{
    [Fact]
    public void The_bill_is_the_stages_files()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "eidolon-stages.json")));
        var stages = doc.RootElement.GetProperty("stages").EnumerateArray()
            .Where(s => s.GetProperty("code").GetString() != "gantry").ToList();
        Assert.Equal(BodyBill.Order, stages.Select(s => s.GetProperty("code").GetString()));
        Assert.Equal(GantryRequires.BodyStages, BodyBill.Order);
        foreach (var stage in stages)
        {
            // a layer's {tokens} are the builder's choice of material; the bill is the default build's
            var material = stage.TryGetProperty("material", out var m)
                ? m.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
                : new Dictionary<string, string>();
            var listed = stage.GetProperty("ingredients").EnumerateArray().Select(e =>
            {
                var text = material.Aggregate(e.GetString()!, (s, kv) => s.Replace("{" + kv.Key + "}", kv.Value));
                int space = text.IndexOf(' ');
                return space > 0 ? new BodyIngredient(text[(space + 1)..], int.Parse(text[..space])) : new BodyIngredient(text, 1);
            });
            Assert.Equal(BodyBill.Of(stage.GetProperty("code").GetString()!), listed);
        }
    }

    [Fact]
    public void The_bill_has_the_epics_totals()
    {
        // the torso is crafted (3 of the 12 stainless gears and 4 of the 16 metal parts are in its recipe)
        var all = BodyBill.Stages.Values.SelectMany(b => b).GroupBy(i => i.Code).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        Assert.Equal([new BodyIngredient(BodyBill.Torso, 1)], BodyBill.Of("torso"));
        Assert.Equal(9, all[BodyBill.StainlessGear]);
        Assert.Equal(12, all[BodyBill.MetalParts]);
        Assert.Equal(43, 2 * all[BodyBill.SteelPlate] + all[BodyBill.SteelRod] + all[BodyBill.SteelNails] / 4);
        Assert.Contains(new BodyIngredient(BodyBill.Vessel, 1), BodyBill.Of("bones"));
        Assert.Equal([new BodyIngredient(BodyBill.TemporalGear, 1)], BodyBill.Of(BodyBill.Last));
        // each code once a stage, so a click's ingredient is its code
        Assert.All(BodyBill.Stages.Values, b => Assert.Equal(b.Count, b.Select(i => i.Code).Distinct().Count()));
    }

    [Fact]
    public void Stages_go_in_order_any_order_within_and_a_click_takes_what_is_held_up_to_what_is_needed()
    {
        var body = new BodyParts();
        Assert.Equal("torso", body.Next);
        Assert.False(body.Started);
        // a later stage's item, and one no stage takes
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit("game:jonasframes-gearbox02", 1, out _, out _));
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit(BodyBill.TemporalGear, 1, out _, out _));
        Assert.Equal(BodyFitVerdict.NotAPart, body.CanFit("game:rod-copper", 1, out _, out _));
        Assert.Equal(BodyFitVerdict.NotAPart, body.CanFit(null, 1, out _, out _));
        Assert.Equal(1, body.Fit(BodyBill.Torso, 1));
        Assert.Equal("bones", body.Next);
        // the bones take 10 beams: three held go in, then seven of a stack of sixteen
        Assert.Equal(3, body.Fit("supportbeam-oak", 3));
        Assert.Equal(BodyFitVerdict.Fits, body.CanFit(BodyBill.SupportBeam, 16, out var stage, out int take));
        Assert.Equal(("bones", 7), (stage, take));
        Assert.Equal(7, body.Fit(BodyBill.SupportBeam, 16));
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit(BodyBill.SupportBeam, 1, out _, out _));
        Assert.True(body.Started);
        Assert.DoesNotContain(body.StillNeeded("bones"), i => i.Code == BodyBill.SupportBeam);
        foreach (var i in body.StillNeeded("bones"))
            Assert.Equal(i.Count, body.Fit(i.Code, 64));
        Assert.True(body.StageComplete("bones"));
        Assert.Equal("joints", body.Next);
        // the vessel is the bones' only: nothing more of it goes in
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit(BodyBill.Vessel, 1, out _, out _));
    }

    [Fact]
    public void Breaking_returns_everything_fitted_and_clearing_empties_the_spine()
    {
        var body = new BodyParts();
        body.FitNextStage();
        body.FitNextStage();
        body.Fit(BodyBill.SteelRod, 2);
        body.Fit("game:jonasframes-joint01", 1);
        var returns = body.Returns().ToDictionary(d => d.Code, d => d.Count);
        Assert.Equal(1, returns[BodyBill.Torso]);
        Assert.Equal(10, returns[BodyBill.SupportBeam]);
        Assert.Equal(2, returns[BodyBill.SteelRod]);
        Assert.Equal(1, returns["game:jonasframes-joint01"]);
        Assert.Equal(BodyBill.Of("torso").Sum(i => i.Count) + BodyBill.Of("bones").Sum(i => i.Count) + 3, returns.Values.Sum());

        var restored = BodyParts.Restore(body.Snapshot());
        Assert.Equal(body.Returns(), restored.Returns());
        Assert.Equal("joints", restored.Next);

        body.Clear();
        Assert.False(body.Started);
        Assert.Empty(body.Returns());
        Assert.Equal("torso", body.Next);
    }

    [Fact]
    public void Restoring_drops_what_a_stage_does_not_take_and_caps_counts()
    {
        var restored = BodyParts.Restore(new Dictionary<string, int>
        {
            ["plates/game:metalplate-steel"] = 99,
            ["plates/game:jonasframes-gearbox02"] = 1,
            ["wings/game:metalplate-steel"] = 1,
            ["plates"] = 1,
        });
        Assert.Equal([new GantryDrop(BodyBill.SteelPlate, 17)], restored.Returns());
    }

    [Fact]
    public void Creative_fills_stage_by_stage_up_to_the_mind()
    {
        var body = new BodyParts();
        foreach (var stage in BodyBill.Order)
        {
            Assert.Equal(stage, body.Next);
            Assert.Equal(stage, body.FitNextStage());
        }
        Assert.True(body.Complete);
        Assert.Null(body.FitNextStage());
    }
}
