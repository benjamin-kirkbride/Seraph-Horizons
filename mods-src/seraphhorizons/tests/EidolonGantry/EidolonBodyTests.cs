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
            var listed = stage.GetProperty("ingredients").EnumerateArray().Select(e =>
            {
                var text = e.GetString()!;
                int space = text.IndexOf(' ');
                return space > 0 ? new BodyIngredient(text[(space + 1)..], int.Parse(text[..space])) : new BodyIngredient(text, 1);
            });
            Assert.Equal(BodyBill.Of(stage.GetProperty("code").GetString()!), listed);
        }
    }

    [Fact]
    public void The_bill_has_the_epics_totals()
    {
        var all = BodyBill.Stages.Values.SelectMany(b => b).GroupBy(i => i.Code).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        Assert.Equal(12, all[BodyBill.StainlessGear]);
        Assert.Equal(16, all[BodyBill.MetalParts]);
        Assert.Equal(43, 2 * all[BodyBill.SteelPlate] + all[BodyBill.SteelRod] + all[BodyBill.SteelNails] / 4);
        Assert.Contains(new BodyIngredient(BodyBill.Vessel, 1), BodyBill.Of("head"));
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
        // the torso takes 4 plates: three held go in, then one of a stack of five
        Assert.Equal(3, body.Fit("metalplate-steel", 3));
        Assert.Equal(BodyFitVerdict.Fits, body.CanFit(BodyBill.SteelPlate, 5, out var stage, out int take));
        Assert.Equal(("torso", 1), (stage, take));
        Assert.Equal(1, body.Fit(BodyBill.SteelPlate, 5));
        // plates are needed later too, but not yet: the torso has all it takes of them
        Assert.Equal(BodyFitVerdict.OutOfOrder, body.CanFit(BodyBill.SteelPlate, 1, out _, out _));
        Assert.True(body.Started);
        Assert.DoesNotContain(body.StillNeeded("torso"), i => i.Code == BodyBill.SteelPlate);
        foreach (var i in body.StillNeeded("torso"))
            Assert.Equal(i.Count, body.Fit(i.Code, 64));
        Assert.True(body.StageComplete("torso"));
        Assert.Equal("pelvis", body.Next);
        // the pump head is the torso's only: nothing more of it goes in
        Assert.Equal(BodyFitVerdict.AlreadyFitted, body.CanFit("game:jonasparts-pumphead", 1, out _, out _));
    }

    [Fact]
    public void Breaking_returns_everything_fitted_and_clearing_empties_the_spine()
    {
        var body = new BodyParts();
        body.FitNextStage();
        body.Fit(BodyBill.SteelPlate, 2);
        body.Fit("game:jonasframes-gearbox02", 1);
        var returns = body.Returns().ToDictionary(d => d.Code, d => d.Count);
        Assert.Equal(6, returns[BodyBill.SteelPlate]);
        Assert.Equal(1, returns["game:jonasframes-gearbox02"]);
        Assert.Equal(3, returns[BodyBill.StainlessGear]);
        Assert.Equal(BodyBill.Of("torso").Sum(i => i.Count) + 3, returns.Values.Sum());

        var restored = BodyParts.Restore(body.Snapshot());
        Assert.Equal(body.Returns(), restored.Returns());
        Assert.Equal("pelvis", restored.Next);

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
            ["torso/game:metalplate-steel"] = 99,
            ["torso/game:jonasframes-gearbox02"] = 1,
            ["wings/game:metalplate-steel"] = 1,
            ["torso"] = 1,
        });
        Assert.Equal([new GantryDrop(BodyBill.SteelPlate, 4)], restored.Returns());
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
