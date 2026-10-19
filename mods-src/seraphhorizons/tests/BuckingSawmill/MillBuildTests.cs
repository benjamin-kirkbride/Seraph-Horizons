using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The mill's build file (<c>config/builds/buckingmill.json</c>) on the shared machine base, with its blade
/// kit's rule (<see cref="BladeKits"/>): two sashes, a crankshaft and levers in any order, a click each, and one
/// blade kit of any metal once both sashes are in, its uses its own durability.</summary>
public class MillBuildTests
{
    private const string Iw = "immersivewoodworking:";
    private const string Sash = Iw + "sawmillsash", Crankshaft = Iw + "sawmillcrankshaft", Levers = Iw + "sawmilllevers";

    private static MachineBuild Build() =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "buckingmill.json")))
            .WithUses(BladeKits.Part, code => code switch { Iw + "sawmillblade-copper" => 200, Iw + "sawmillblade-steel" => 800, _ => 0 });

    private static MachineAssembly Fitted(params string[] codes)
    {
        var a = new MachineAssembly(Build());
        foreach (var code in codes)
            if (BladeKits.MetalOf(code) != null)
                Assert.Equal(BladeVerdict.Fits, BladeKits.Fit(a, code, null));
            else
                Assert.Equal(AssemblyVerdict.Fitted, a.Fit(code, 1).Verdict);
        return a;
    }

    [Fact]
    public void Blade_kits_are_recognised_by_code()
    {
        Assert.Equal("tinbronze", BladeKits.MetalOf(Iw + "sawmillblade-tinbronze"));
        Assert.Null(BladeKits.MetalOf(Iw + "sawmillblade-"));
        Assert.Null(BladeKits.MetalOf(Iw + "sawmillcarriage"));
        Assert.Null(BladeKits.MetalOf("game:sawmillblade-iron"));
        Assert.Null(BladeKits.MetalOf(null));
    }

    [Fact]
    public void Ready_takes_two_sashes_a_crankshaft_levers_and_one_kit()
    {
        var a = Fitted(Sash, Sash, Crankshaft, Levers);
        Assert.True(a.Built);
        Assert.False(a.Ready);
        Assert.Equal(BladeVerdict.Fits, BladeKits.Fit(a, Iw + "sawmillblade-iron", null));
        Assert.True(a.Ready);
        Assert.Equal(Iw + "sawmillblade-iron", a.Wearing.Fitted(BladeKits.Part)!.Value.Code);
    }

    [Fact]
    public void Any_order_works_for_the_mechanism()
    {
        Assert.True(Fitted(Levers, Crankshaft, Sash, Sash, Iw + "sawmillblade-copper").Ready);
        Assert.True(Fitted(Sash, Sash, Iw + "sawmillblade-copper", Crankshaft, Levers).Ready);
    }

    [Fact]
    public void The_levers_are_needed()
    {
        var a = Fitted(Sash, Sash, Crankshaft, Iw + "sawmillblade-copper");
        Assert.False(a.Built);
        var need = Assert.Single(a.Build.NextNeeds());
        Assert.Equal(Levers, need.Material.Codes[0]);
        a.Fit(Levers, 1);
        Assert.True(a.Ready);
    }

    [Fact]
    public void One_part_a_click_even_with_two_sashes_held()
    {
        var a = new MachineAssembly(Build());
        Assert.Equal(1, a.Fit(Sash, 2).Take);
        Assert.Equal(1, a.Fit(Sash, 1).Take);
        Assert.Equal(AssemblyVerdict.AlreadyFitted, a.Check(Sash, 1).Verdict);
    }

    [Fact]
    public void The_blade_kit_needs_both_sashes()
    {
        var a = new MachineAssembly(Build());
        Assert.Equal(BladeVerdict.NeedsSashes, BladeKits.Fit(a, Iw + "sawmillblade-iron", null));
        a.Fit(Sash, 1);
        Assert.Equal(BladeVerdict.NeedsSashes, BladeKits.Fit(a, Iw + "sawmillblade-iron", null));
        a.Fit(Sash, 1);
        Assert.Equal(BladeVerdict.Fits, BladeKits.Fit(a, Iw + "sawmillblade-iron", null));
        // the build still wants its crankshaft and levers: the kit is not one of its stage's materials
        Assert.False(a.Built);
        Assert.Equal(2, a.Build.NextNeeds().Count);
    }

    [Fact]
    public void One_kit_only_of_any_metal()
    {
        var a = Fitted(Sash, Sash, Iw + "sawmillblade-iron");
        Assert.Equal(BladeVerdict.BladeFitted, BladeKits.Fit(a, Iw + "sawmillblade-iron", null));
        Assert.Equal(BladeVerdict.BladeFitted, BladeKits.Fit(a, Iw + "sawmillblade-steel", null));
        Assert.Equal(BladeVerdict.NotABlade, BladeKits.CanFit(a, Iw + "sawmillcarriage"));
    }

    [Fact]
    public void A_kit_lasts_its_own_durability_and_comes_out_with_what_is_left()
    {
        var a = Fitted(Sash, Sash, Crankshaft, Levers);
        BladeKits.Fit(a, Iw + "sawmillblade-steel", 600);
        Assert.Equal(600, a.Wearing.Fitted(BladeKits.Part)!.Value.Left);
        Assert.Equal(0.75, a.Wearing.ShareLeft(BladeKits.Part), 3);
        Assert.False(a.Wearing.Wear(BladeKits.Part, 100));
        Assert.Equal((Iw + "sawmillblade-steel", 500.0), a.Take(BladeKits.Part));
        Assert.False(a.Ready);
        // its place is free for a new one, its own metal's full durability
        Assert.Equal(BladeVerdict.Fits, BladeKits.Fit(a, Iw + "sawmillblade-copper", null));
        Assert.Equal(200, a.Wearing.Fitted(BladeKits.Part)!.Value.Left);
        Assert.True(a.Wearing.Wear(BladeKits.Part, 200));
        Assert.Null(a.Wearing.Fitted(BladeKits.Part));
    }

    [Fact]
    public void Breaking_returns_every_part_and_the_kit_with_its_durability()
    {
        var a = Fitted(Sash, Sash, Levers);
        BladeKits.Fit(a, Iw + "sawmillblade-steel", 300);
        var drops = a.Returns();
        Assert.Contains(new StageDrop(Sash, 2), drops);
        Assert.Contains(new StageDrop(Levers, 1), drops);
        Assert.Contains(new StageDrop(Iw + "sawmillblade-steel", 1, 300), drops);
        Assert.DoesNotContain(drops, d => d.Code == Crankshaft);
    }

    // The creative shortcut fills the parts still missing in one click, then a steel kit.
    [Fact]
    public void The_creative_shortcut_fills_the_parts_then_a_steel_kit()
    {
        var a = Fitted(Levers, Sash);
        Assert.True(a.FillNext());
        Assert.True(a.Built);
        Assert.False(a.Ready);
        Assert.True(a.FillNext());
        Assert.True(a.Ready);
        Assert.Equal((Iw + "sawmillblade-steel", 800.0), a.Wearing.Fitted(BladeKits.Part));
        Assert.False(a.FillNext());
        Assert.Contains(new StageDrop(Sash, 2), a.Returns());
    }

    [Fact]
    public void Restored_state_keeps_what_fits_the_build()
    {
        var (build, wearing) = Fitted(Sash, Sash, Crankshaft, Iw + "sawmillblade-steel").Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.True(restored.Shows("sash2"));
        Assert.True(restored.Shows("crankshaft"));
        Assert.False(restored.Shows("levers"));
        Assert.Equal(800, restored.Wearing.Fitted(BladeKits.Part)!.Value.Left);
    }
}
