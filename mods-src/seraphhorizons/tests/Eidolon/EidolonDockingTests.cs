using System.Text.Json;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry.Core;
using Xunit;

namespace SeraphHorizons.Tests.Eidolon;

/// <summary>Docking an eidolon to strip and refit it (<see cref="EidolonDocking"/>, the docked body
/// <see cref="BodyParts.Docked"/>, and which elements a layer that is off leaves out,
/// <see cref="EidolonLayerElements"/>).</summary>
public class EidolonDockingTests
{
    private static readonly EidolonBuild Steel = EidolonBuild.OldBuild;

    [Fact]
    public void Dock_timing_is_the_animations()
    {
        Assert.Equal(46 / 30.0, EidolonDocking.ClampSeconds, 6);
        Assert.Equal(2.0, EidolonDocking.DockSeconds, 6);
        Assert.True(EidolonDocking.ClampSeconds < EidolonDocking.DockSeconds);
    }

    [Fact]
    public void Strips_only_docked_whole_and_with_a_layer_on()
    {
        Assert.Equal(StripVerdict.NotDocked, EidolonDocking.CanStrip(Steel, docked: false, 300, 300));
        Assert.Equal(StripVerdict.Damaged, EidolonDocking.CanStrip(Steel, docked: true, 299.9, 300));
        Assert.Equal(StripVerdict.Damaged, EidolonDocking.CanStrip(Steel, docked: true, double.NaN, 300));
        Assert.Equal(StripVerdict.Strips, EidolonDocking.CanStrip(Steel, docked: true, 300, 300));
        Assert.Equal(StripVerdict.Bare, EidolonDocking.CanStrip(EidolonBuild.Bare, docked: true, 300, 300));
        // not docked comes first: a damaged eidolon in the field is told to dock, not to be repaired
        Assert.Equal(StripVerdict.NotDocked, EidolonDocking.CanStrip(Steel, docked: false, 1, 300));
    }

    [Fact]
    public void Wakes_only_with_all_five_layers_on()
    {
        Assert.Equal(DockWakeVerdict.Wakes, EidolonDocking.CanWake(Steel));
        Assert.Equal(DockWakeVerdict.LayersMissing, EidolonDocking.CanWake(Steel.Stripped()!));
        Assert.Equal(DockWakeVerdict.LayersMissing, EidolonDocking.CanWake(EidolonBuild.Bare));
    }

    [Fact]
    public void Resumes_its_order_but_not_walking_or_docking()
    {
        Assert.True(EidolonDocking.Resumes("follow"));
        Assert.True(EidolonDocking.Resumes("guard"));
        Assert.False(EidolonDocking.Resumes(null));
        Assert.False(EidolonDocking.Resumes("goto"));
        Assert.False(EidolonDocking.Resumes("dock"));
    }

    [Fact]
    public void A_docked_body_is_its_build_with_torso_and_mind()
    {
        var parts = BodyParts.Docked(Steel);
        Assert.True(parts.Complete);
        Assert.Equal(Steel, parts.Build);
        Assert.Empty(parts.Loose(Steel));
        Assert.Equal(1, parts.Fitted("torso", BodyBill.Torso));
        Assert.Equal(1, parts.Fitted("mind", BodyBill.TemporalGear));
        Assert.Equal(17, parts.Fitted("plates", "game:metalplate-steel"));

        var stripped = Steel.Stripped()!.Stripped()!.Stripped()!; // skirt, sash, plates off
        parts = BodyParts.Docked(stripped);
        Assert.Equal("plates", parts.Next);
        Assert.Equal(stripped, parts.Build);
        Assert.Equal(0, parts.Fitted("plates", "game:metalplate-steel"));
        Assert.Equal(1, parts.Fitted("mind", BodyBill.TemporalGear));
    }

    [Fact]
    public void A_stripped_layer_is_refitted_item_by_item_in_any_material()
    {
        var parts = BodyParts.Docked(Steel);
        parts.Strip(FittingLayer.Skirt);
        parts.Strip(FittingLayer.Sash);
        parts.Strip(FittingLayer.Plates);
        Assert.Equal("plates", parts.Next);
        // the mind is in, but its gear goes nowhere while a layer is off
        Assert.Equal(BodyFitVerdict.OutOfOrder, parts.CanFit(BodyBill.TemporalGear, 1, out _, out _));
        // a sash before the plates is out of order; brass plates fit, and fix the material
        Assert.Equal(BodyFitVerdict.OutOfOrder, parts.CanFit("game:leather-normal-blue", 2, out _, out _));
        Assert.Equal(5, parts.Fit("game:metalplate-brass", 5));
        Assert.Equal(EidolonFittings.Brass, parts.Material(FittingLayer.Plates));
        Assert.Equal(BodyFitVerdict.WrongMaterial, parts.CanFit("game:metalplate-steel", 1, out _, out _));
        // part fitted, the layer is not on: the build is still without it, the five pieces loose
        var worn = parts.Build;
        Assert.Null(worn[FittingLayer.Plates]);
        Assert.Equal([new GantryDrop("game:metalplate-brass", 5)], parts.Loose(worn));
        Assert.Equal(12, parts.Fit("game:metalplate-brass", 64));
        Assert.Equal(EidolonFittings.Brass, parts.Build[FittingLayer.Plates]);
        Assert.Empty(parts.Loose(parts.Build));
        Assert.Equal("sash", parts.Next);
    }

    [Fact]
    public void Loose_pieces_are_taken_back_whole()
    {
        var bare = Steel.Stripped()!; // skirt off
        var parts = BodyParts.Docked(bare);
        Assert.Equal(1, parts.Fit("game:metalchain-iron", 1));
        var loose = parts.TakeLoose(bare);
        Assert.Equal([new GantryDrop("game:metalchain-iron", 1)], loose);
        Assert.Empty(parts.Loose(bare));
        Assert.Equal(bare, parts.Build);
        Assert.Null(parts.Material(FittingLayer.Skirt));
        // and any material goes on again
        int chain = FittingCounts.Of(FittingLayer.Skirt)[0];
        Assert.Equal(chain, parts.Fit("game:metalchain-stainlesssteel", chain));
        Assert.Equal(EidolonFittings.Stainless, parts.Build[FittingLayer.Skirt]);
    }

    [Fact]
    public void A_docked_body_survives_its_save()
    {
        var bare = Steel.Stripped()!.Stripped()!;
        var parts = BodyParts.Docked(bare);
        parts.Fit("game:cloth-red", 1);
        var restored = BodyParts.Restore(parts.Snapshot());
        Assert.Equal(bare, restored.Build);
        Assert.Equal([new GantryDrop("game:cloth-red", 1)], restored.Loose(bare));
        Assert.Equal("cloth-red", restored.Material(FittingLayer.Sash));
    }

    // A docked body's save from when the bones took 10 support beams (before #831 raised them to 30).
    private static Dictionary<string, int> UnderOldBill(BodyParts docked)
    {
        var snapshot = docked.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);
        snapshot["bones/game:supportbeam-oak"] = 10;
        return snapshot;
    }

    [Fact]
    public void A_docked_body_saved_under_an_older_bill_is_made_again_from_the_eidolons_build()
    {
        var stale = BodyParts.Restore(UnderOldBill(BodyParts.Docked(Steel)));
        // the saved copy is short: its bones part fitted, so its build is bare
        Assert.Equal("bones", stale.Next);
        Assert.Equal(EidolonBuild.Bare, stale.Build);
        // measured against the eidolon (or the most it can wear), none of its layers is loose
        Assert.Empty(stale.Loose(Steel));
        Assert.Equal(Steel, stale.MostWorn);
        Assert.Empty(stale.Loose(stale.MostWorn));
        // and the short body never strips it
        Assert.Null(stale.Dressing(Steel));

        var parts = stale.Reconciled(Steel);
        Assert.True(parts.Complete);
        Assert.Equal(Steel, parts.Build);
        Assert.Equal(FittingCounts.Of(FittingLayer.Bones)[0], parts.Fitted("bones", "game:supportbeam-oak"));
        Assert.Empty(parts.Loose(Steel));
        Assert.Empty(parts.TakeLoose(Steel));
        Assert.Null(parts.Dressing(Steel));
        Assert.Equal(parts.Snapshot(), parts.Reconciled(Steel).Snapshot());
    }

    [Fact]
    public void Reconciling_keeps_the_pieces_fitted_beyond_the_eidolon_and_the_spare()
    {
        var worn = Steel.Stripped()!.Stripped()!; // skirt, sash off
        var docked = BodyParts.Docked(worn);
        docked.Fit("game:cloth-red", 1);
        var saved = UnderOldBill(docked);
        saved["spare/game:rod-iron"] = 2;
        var parts = BodyParts.Restore(saved).Reconciled(worn);
        Assert.Equal(worn, parts.Build);
        Assert.Equal("sash", parts.Next);
        Assert.Equal("cloth-red", parts.Material(FittingLayer.Sash));
        Assert.Equal([new GantryDrop("game:cloth-red", 1), new GantryDrop("game:rod-iron", 2)], parts.Loose(worn));
        // the sash finished on the spine goes onto the eidolon, adding to what it wears
        Assert.Equal(FittingCounts.Of(FittingLayer.Sash)[0] - 1, parts.Fit("game:cloth-red", 64));
        Assert.Equal(worn.With(FittingLayer.Sash, "cloth-red"), parts.Dressing(worn));
    }

    [Fact]
    public void The_most_a_docked_eidolon_can_wear_counts_in_a_layer_part_fitted()
    {
        var worn = Steel.Stripped()!; // skirt off
        var parts = BodyParts.Docked(worn);
        parts.Fit("game:metalchain-iron", 1);
        // with the eidolon not loaded the chain waits rather than a worn layer being given back
        Assert.Equal(worn.With(FittingLayer.Skirt, EidolonFittings.Iron), parts.MostWorn);
        Assert.Empty(parts.Loose(parts.MostWorn));
        Assert.Equal([new GantryDrop("game:metalchain-iron", 1)], parts.Loose(worn));
    }

    [Fact]
    public void Stripping_a_layer_returns_its_bill_by_the_bill()
    {
        // what the gantry gives back is the layer's bill: its material, then its Jonas parts
        var bill = EidolonFittings.Bill(FittingLayer.Joints, EidolonFittings.Stainless);
        Assert.Contains(bill, i => i.Code == "game:rod-stainlesssteel" && i.Count == 4);
        Assert.Contains(bill, i => i.Code == "game:metalnailsandstrips-stainlesssteel" && i.Count == 20);
        Assert.Contains(bill, i => i.Code == EidolonFittings.StainlessGear && i.Count == 9);
        var parts = BodyParts.Docked(EidolonBuild.Of("oak", EidolonFittings.Stainless, EidolonFittings.Iron, "leather-plain", EidolonFittings.Iron));
        foreach (var i in bill)
            Assert.Equal(i.Count, parts.Fitted("joints", i.Code));
    }

    // ---- What a layer that is off leaves out of the shape ----

    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));

    [Fact]
    public void Each_layers_elements_are_the_stages_files()
    {
        var elements = EidolonLayerElements.Parse(Read("eidolon-stages.json"));
        foreach (var layer in EidolonFittings.Layers)
            Assert.NotEmpty(elements.Of(layer));
        Assert.Contains("chainskirt-front1", elements.Of(FittingLayer.Skirt));
        Assert.Empty(elements.Hidden(Steel));
        var hidden = elements.Hidden(Steel.Stripped()!.Stripped()!);
        Assert.Equal([.. elements.Of(FittingLayer.Skirt), .. elements.Of(FittingLayer.Sash)], hidden);
        Assert.Equal(EidolonFittings.Layers.Sum(l => elements.Of(l).Count), elements.Hidden(EidolonBuild.Bare).Count);
    }

    // The game removes an element with its children: a layer taken off must take no element of a layer
    // still on, nor the torso's. Walks the shape's hierarchy.
    [Fact]
    public void A_layer_off_takes_nothing_of_the_layers_before_it()
    {
        var elements = EidolonLayerElements.Parse(Read("eidolon-stages.json"));
        using var stages = JsonDocument.Parse(Read("eidolon-stages.json"));
        var order = stages.RootElement.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("code").GetString()!).ToList();
        var stageOf = new Dictionary<string, string>();
        foreach (var s in stages.RootElement.GetProperty("stages").EnumerateArray())
            foreach (var e in s.GetProperty("elements").EnumerateArray())
                stageOf[e.GetString()!] = s.GetProperty("code").GetString()!;
        using var shape = JsonDocument.Parse(Read("eidolon-shape.json"));
        var wrong = new List<string>();
        void Walk(JsonElement element, string? parentStage)
        {
            string name = element.GetProperty("name").GetString()!;
            string? stage = stageOf.GetValueOrDefault(name) ?? parentStage;
            if (stage != null && parentStage != null && order.IndexOf(stage) < order.IndexOf(parentStage))
                wrong.Add($"{name} ({stage}) under a {parentStage} element");
            if (element.TryGetProperty("children", out var children))
                foreach (var c in children.EnumerateArray())
                    Walk(c, stage);
        }
        foreach (var e in shape.RootElement.GetProperty("elements").EnumerateArray())
            Walk(e, null);
        Assert.Empty(wrong);
        // what goes with the joints beyond the layers after them: the mind's eye light on its bracket
        Assert.DoesNotContain("Eye-out", elements.Hidden(Steel.Stripped()!));
    }
}
