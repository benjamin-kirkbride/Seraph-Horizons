using Newtonsoft.Json;
using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The fittings' layers, materials, bill and record, and the stats and armour they give.</summary>
public class EidolonFittingsTests
{
    private const string Stainless = EidolonFittings.Stainless;

    [Fact]
    public void LayersGoOnInsideOutAndComeOffOutsideIn()
    {
        Assert.Equal([FittingLayer.Bones, FittingLayer.Joints, FittingLayer.Plates, FittingLayer.Sash, FittingLayer.Skirt],
            EidolonFittings.Layers);
        Assert.Equal(EidolonFittings.Layers.Reverse(), EidolonFittings.StripOrder);
    }

    [Theory]
    [InlineData(FittingLayer.Bones, "oak", true)]
    [InlineData(FittingLayer.Bones, "aged", true)]
    [InlineData(FittingLayer.Bones, "veryaged", true)]
    [InlineData(FittingLayer.Bones, "mahogany", true)] // a mod's wood
    [InlineData(FittingLayer.Bones, "rotten", false)]
    [InlineData(FittingLayer.Bones, "veryrotten", false)]
    [InlineData(FittingLayer.Bones, "tarnishedmetal-iron", false)]
    [InlineData(FittingLayer.Joints, "iron", true)]
    [InlineData(FittingLayer.Joints, Stainless, true)]
    [InlineData(FittingLayer.Joints, "brass", false)]
    [InlineData(FittingLayer.Plates, "brass", true)]
    [InlineData(FittingLayer.Plates, "copper", false)]
    [InlineData(FittingLayer.Sash, "leather-plain", true)]
    [InlineData(FittingLayer.Sash, "leather-brown", false)] // no brown leather
    [InlineData(FittingLayer.Sash, "cloth-brown", true)]
    [InlineData(FittingLayer.Sash, "cloth-mordant", false)]
    [InlineData(FittingLayer.Skirt, Stainless, true)]
    [InlineData(FittingLayer.Skirt, "brass", false)]
    [InlineData(FittingLayer.Skirt, null, false)]
    public void EachLayerTakesItsMaterials(FittingLayer layer, string? material, bool allowed) =>
        Assert.Equal(allowed, EidolonFittings.Allows(layer, material));

    [Theory]
    [InlineData(FittingLayer.Bones, "oak", "game:block/wood/debarked/oak")]
    [InlineData(FittingLayer.Joints, "steel", "game:block/metal/plate/steel")]
    [InlineData(FittingLayer.Plates, Stainless, "game:block/metal/plate/stainlesssteel")]
    [InlineData(FittingLayer.Plates, "brass", "game:block/metal/plate/brass")]
    [InlineData(FittingLayer.Sash, "leather-plain", "game:block/leather/plain")]
    [InlineData(FittingLayer.Sash, "cloth-plain", "game:block/cloth/basic/normal")]
    [InlineData(FittingLayer.Sash, "cloth-red", "game:block/cloth/basic/red")]
    [InlineData(FittingLayer.Skirt, "iron", "game:block/metal/armor-generic/chain-iron")]
    [InlineData(FittingLayer.Skirt, Stainless, "seraphhorizons:block/metal/armor-generic/chain-stainlesssteel")]
    public void EachMaterialHasItsTexture(FittingLayer layer, string material, string path) =>
        Assert.Equal(path, EidolonFittings.TexturePath(layer, material));

    [Fact]
    public void ARefusedMaterialHasNoTextureNorItems()
    {
        Assert.Null(EidolonFittings.TexturePath(FittingLayer.Joints, "brass"));
        Assert.Empty(EidolonFittings.MaterialCodes(FittingLayer.Joints, "brass"));
        Assert.Empty(EidolonFittings.Bill(FittingLayer.Joints, "brass"));
    }

    [Fact]
    public void TextureCodesAreTheLayersNames()
    {
        Assert.Equal(["bones", "joints", "plates", "sash", "skirt"], EidolonFittings.Layers.Select(EidolonFittings.TextureCode));
        Assert.Equal(FittingLayer.Sash, EidolonFittings.LayerOfTextureCode("sash"));
        Assert.Null(EidolonFittings.LayerOfTextureCode("lattice"));
    }

    [Theory]
    [InlineData(FittingLayer.Bones, "pine", "game:supportbeam-pine")]
    [InlineData(FittingLayer.Joints, Stainless, "game:rod-stainlesssteel,game:metalnailsandstrips-stainlesssteel")]
    [InlineData(FittingLayer.Plates, "iron", "game:metalplate-iron")]
    [InlineData(FittingLayer.Sash, "leather-blue", "game:leather-normal-blue")]
    [InlineData(FittingLayer.Sash, "cloth-plain", "game:cloth-plain")]
    [InlineData(FittingLayer.Skirt, Stainless, "game:metalchain-stainlesssteel")]
    public void EachMaterialIsFittedAsItsItems(FittingLayer layer, string material, string codes) =>
        Assert.Equal(codes.Split(','), EidolonFittings.MaterialCodes(layer, material));

    [Theory]
    [InlineData("game:supportbeam-oak", FittingLayer.Bones, "oak")]
    [InlineData("supportbeam-aged", FittingLayer.Bones, "aged")]
    [InlineData("game:rod-steel", FittingLayer.Joints, "steel")]
    [InlineData("game:metalnailsandstrips-stainlesssteel", FittingLayer.Joints, Stainless)]
    [InlineData("game:metalplate-brass", FittingLayer.Plates, "brass")]
    [InlineData("game:leather-normal-plain", FittingLayer.Sash, "leather-plain")]
    [InlineData("game:cloth-green", FittingLayer.Sash, "cloth-green")]
    [InlineData("game:metalchain-iron", FittingLayer.Skirt, "iron")]
    public void ACodeIsKnownAsItsLayerAndMaterial(string code, FittingLayer layer, string material) =>
        Assert.Equal((layer, material), EidolonFittings.Identify(code));

    [Theory]
    [InlineData("game:supportbeam-rotten")]
    [InlineData("game:supportbeam-tarnishedmetal-iron")]
    [InlineData("game:rod-brass")]
    [InlineData("game:metalplate-copper")]
    [InlineData("game:leather-sturdy-plain")]
    [InlineData("game:cloth-mordant")]
    [InlineData("game:metalchain-brass")]
    [InlineData("seraphhorizons:metalplate-steel")]
    [InlineData("game:jonasframes-joint01")]
    [InlineData(null)]
    public void OtherCodesAreNoMaterial(string? code) => Assert.Null(EidolonFittings.Identify(code));

    [Fact]
    public void EveryOfferedMaterialRoundTripsThroughItsItems()
    {
        foreach (var layer in EidolonFittings.Layers)
            foreach (var material in EidolonFittings.Materials(layer))
            {
                Assert.True(EidolonFittings.Allows(layer, material), $"{layer} {material}");
                foreach (var code in EidolonFittings.MaterialCodes(layer, material))
                    Assert.Equal((layer, material), EidolonFittings.Identify(code));
            }
    }

    [Fact]
    public void TheJonasPartsAreSpreadOverTorsoBonesAndJoints()
    {
        var bones = EidolonFittings.JonasParts(FittingLayer.Bones);
        Assert.Contains(new FittingIngredient(EidolonFittings.Vessel, 1), bones);
        Assert.Equal(6, bones.Count);
        var joints = EidolonFittings.JonasParts(FittingLayer.Joints);
        Assert.Contains(new FittingIngredient(EidolonFittings.StainlessGear, 9), joints);
        Assert.Contains(new FittingIngredient(EidolonFittings.MetalParts, 12), joints);
        Assert.Empty(EidolonFittings.JonasParts(FittingLayer.Plates));
        Assert.Empty(EidolonFittings.JonasParts(FittingLayer.Sash));
        Assert.Empty(EidolonFittings.JonasParts(FittingLayer.Skirt));

        // The whole body takes what the limb-by-limb build took: 12 stainless gears, 16 metal parts.
        var all = EidolonFittings.TorsoRecipe.Concat(bones).Concat(joints).ToList();
        Assert.Equal(12, all.Where(i => i.Code == EidolonFittings.StainlessGear).Sum(i => i.Count));
        Assert.Equal(16, all.Where(i => i.Code == EidolonFittings.MetalParts).Sum(i => i.Count));
    }

    [Fact]
    public void ALayersBillIsItsMaterialThenItsJonasParts()
    {
        var bill = EidolonFittings.Bill(FittingLayer.Joints, "iron");
        Assert.Equal(new FittingIngredient("game:rod-iron", FittingCounts.Of(FittingLayer.Joints)[0]), bill[0]);
        Assert.Equal(new FittingIngredient("game:metalnailsandstrips-iron", FittingCounts.Of(FittingLayer.Joints)[1]), bill[1]);
        Assert.Equal(EidolonFittings.JonasParts(FittingLayer.Joints), bill.Skip(2));

        var bones = EidolonFittings.Bill(FittingLayer.Bones, "oak");
        Assert.True(bones[0].IsBlock);
        Assert.All(bones.Skip(1), i => Assert.False(i.IsBlock));
    }

    [Fact]
    public void EveryLayerHasACountPerItem()
    {
        foreach (var layer in EidolonFittings.Layers)
        {
            var material = EidolonFittings.Materials(layer)[0];
            Assert.Equal(EidolonFittings.MaterialCodes(layer, material).Count, FittingCounts.Of(layer).Count);
            Assert.All(FittingCounts.Of(layer), n => Assert.True(n > 0));
        }
    }

    // ---- The record ----

    [Fact]
    public void LayersAreFittedInOrderOnly()
    {
        var b = EidolonBuild.Bare;
        Assert.Equal(FittingLayer.Bones, b.NextToFit);
        Assert.Null(b.With(FittingLayer.Joints, "steel"));
        b = b.With(FittingLayer.Bones, "oak")!;
        Assert.Null(b.With(FittingLayer.Bones, "pine"));
        Assert.Null(b.With(FittingLayer.Joints, "brass"));
        b = b.With(FittingLayer.Joints, "iron")!;
        Assert.Equal(FittingLayer.Plates, b.NextToFit);
        Assert.Equal(FittingLayer.Joints, b.NextToStrip);
        Assert.False(b.Complete);
    }

    [Fact]
    public void LayersComeOffOutermostFirstAndOnlyAtFullHealth()
    {
        var b = EidolonBuild.OldBuild;
        Assert.True(b.Complete);
        Assert.Equal(FittingLayer.Skirt, b.NextToStrip);
        Assert.False(b.CanStrip(299, 300));
        Assert.True(b.CanStrip(300, 300));
        var order = new List<FittingLayer>();
        while (b.NextToStrip is { } layer)
        {
            order.Add(layer);
            b = b.Stripped()!;
        }
        Assert.Equal(EidolonFittings.StripOrder, order);
        Assert.True(b.Empty);
        Assert.Null(b.Stripped());
        Assert.False(b.CanStrip(300, 300));
    }

    [Fact]
    public void AStrippedLayerTakesAnyAllowedMaterialAgain()
    {
        var b = EidolonBuild.OldBuild.Stripped()!.Stripped()!; // skirt, then sash
        b = b.With(FittingLayer.Sash, "cloth-blue")!.With(FittingLayer.Skirt, Stainless)!;
        Assert.Equal(EidolonBuild.Of("oak", "steel", "steel", "cloth-blue", Stainless), b);
    }

    [Fact]
    public void TheOldBuildIsAllSteelOakAndPlainLeather()
    {
        var b = EidolonBuild.OldBuild;
        Assert.Equal("oak", b[FittingLayer.Bones]);
        Assert.Equal("steel", b[FittingLayer.Joints]);
        Assert.Equal("steel", b[FittingLayer.Plates]);
        Assert.Equal("leather-plain", b[FittingLayer.Sash]);
        Assert.Equal("steel", b[FittingLayer.Skirt]);
    }

    [Fact]
    public void NoRecordLoadsAsTheOldBuild() => Assert.Equal(EidolonBuild.OldBuild, EidolonBuild.FromAttributes(null));

    [Fact]
    public void TheRecordRoundTripsThroughItsAttributes()
    {
        var b = EidolonBuild.Of("veryaged", Stainless, "brass", "cloth-plain", "iron");
        var stored = b.ToAttributes();
        Assert.Equal(["bones", "joints", "plates", "sash", "skirt"], stored.Keys);
        Assert.Equal(b, EidolonBuild.FromAttributes(stored.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)));

        var partial = EidolonBuild.Bare.With(FittingLayer.Bones, "oak")!;
        Assert.Equal(partial, EidolonBuild.FromAttributes(partial.ToAttributes().ToDictionary(kv => kv.Key, kv => (string?)kv.Value)));
        Assert.Equal(EidolonBuild.Bare, EidolonBuild.FromAttributes(new Dictionary<string, string?>()));
    }

    [Fact]
    public void ABrokenRecordKeepsTheLayersBeforeTheFirstBadOne()
    {
        var stored = new Dictionary<string, string?>
        {
            ["bones"] = "oak", ["joints"] = "brass", ["plates"] = "steel", ["sash"] = "leather-plain", ["skirt"] = "steel",
        };
        var b = EidolonBuild.FromAttributes(stored);
        Assert.Equal("oak", b[FittingLayer.Bones]);
        Assert.Null(b[FittingLayer.Joints]);
        Assert.Null(b[FittingLayer.Plates]);
    }

    [Fact]
    public void TexturesAreThoseOfTheLayersOn()
    {
        var t = EidolonBuild.Bare.With(FittingLayer.Bones, "birch")!.Textures();
        Assert.Equal(new Dictionary<string, string> { ["bones"] = "game:block/wood/debarked/birch" }, t);
        Assert.Equal(5, EidolonBuild.OldBuild.Textures().Count);
    }

    // ---- Stats ----

    [Fact]
    public void AnAllSteelEidolonKeepsItsHealthAndBlows()
    {
        var settings = new EidolonConfig();
        var s = EidolonStats.Of(EidolonBuild.OldBuild, settings);
        Assert.Equal(300, s.MaxHealth, 9);
        Assert.Equal(settings.WalkSpeed, s.WalkSpeed);
        Assert.Equal(settings.RunSpeed, s.RunSpeed);
        Assert.Equal(settings.DefenceDamage, s.DefenceDamage);
        Assert.Equal(settings.SlamDamage, s.SlamDamage);
        Assert.Equal(4, s.Armour!.ProtectionTier);
        Assert.Equal(300 / (1.5 * 1.5 * 1.25), EidolonStats.BaseHealth(settings), 9);
    }

    [Fact]
    public void MultipliersStackAgainstIron()
    {
        var settings = new EidolonConfig();
        double baseHealth = EidolonStats.BaseHealth(settings);
        var allIron = EidolonStats.Of(EidolonBuild.Of("oak", "iron", "iron", "leather-plain", "iron"), settings);
        Assert.Equal(baseHealth, allIron.MaxHealth, 9);
        var allStainless = EidolonStats.Of(EidolonBuild.Of("oak", Stainless, Stainless, "leather-plain", Stainless), settings);
        Assert.Equal(baseHealth * 2 * 2 * 1.5, allStainless.MaxHealth, 9);
        Assert.Equal(5, allStainless.Armour!.ProtectionTier);
    }

    [Fact]
    public void BrassIsWeakerAndSofterButFaster()
    {
        var settings = new EidolonConfig();
        var s = EidolonStats.Of(EidolonBuild.Of("oak", "steel", "brass", "leather-plain", "steel"), settings);
        Assert.Equal(EidolonStats.BaseHealth(settings) * 0.8 * 1.5 * 1.25, s.MaxHealth, 9);
        Assert.Equal(settings.WalkSpeed * 1.25f, s.WalkSpeed, 6);
        Assert.Equal(settings.RunSpeed * 1.25f, s.RunSpeed, 6);
        Assert.Equal(settings.DefenceDamage * 0.8f, s.DefenceDamage, 4);
        Assert.Equal(settings.SlamDamage * 0.8f, s.SlamDamage, 4);
        Assert.Equal(2, s.Armour!.ProtectionTier);
    }

    [Fact]
    public void BonesAndSashAreLooksOnly()
    {
        var settings = new EidolonConfig();
        var a = EidolonStats.Of(EidolonBuild.Of("oak", "iron", "steel", "leather-plain", "steel"), settings);
        var b = EidolonStats.Of(EidolonBuild.Of("ebony", "iron", "steel", "cloth-red", "steel"), settings);
        Assert.Equal(a, b);
    }

    [Fact]
    public void LayersThatAreOffCountAsIronWithNoArmour()
    {
        var settings = new EidolonConfig();
        var s = EidolonStats.Of(EidolonBuild.Bare, settings);
        Assert.Equal(EidolonStats.BaseHealth(settings), s.MaxHealth, 9);
        Assert.Null(s.Armour);
        Assert.Equal(7.5f, s.Reduce(7.5f, 3, settings.ArmourLoss));
    }

    // ---- Armour: the game's maths (ModSystemWearableStats.handleDamaged) ----

    // The same maths in doubles, written out from the game's code, for the expected figures.
    private static double Expected(double damage, int tier, double rel, double flat, int protTier,
        double[] relLoss, double[] flatLoss, bool resistant)
    {
        for (int i = 1; i <= tier; i++)
        {
            bool above = i > protTier;
            double fl = above ? flatLoss[1] : flatLoss[0], rl = above ? relLoss[1] : relLoss[0];
            if (above && resistant) { fl /= 2; rl /= 2; }
            flat -= fl;
            rel *= 1 - rl;
        }
        return Math.Max(0, damage - flat) * (1 - Math.Max(0, rel));
    }

    [Theory]
    [InlineData(10, 0, 0.332)]          // (10 − 1.7) × 0.04
    [InlineData(10, 3, 1.064969)]       // flat 1.4, relative 0.96 × 0.97³
    [InlineData(10, 5, 2.202922)]       // two tiers past iron's, the losses halved
    [InlineData(1, 1, 0)]               // under the flat reduction: nothing
    public void IronPlateReducesAHitAsTheGameDoes(float damage, int tier, double expected)
    {
        var loss = new ArmourTierLoss();
        var iron = new EidolonConfig().PlateArmour.Iron;
        Assert.Equal(expected, EidolonArmour.Reduce(damage, tier, iron, loss), 4);
        Assert.Equal(Expected(damage, tier, 0.96, 1.7, 3, [0.03, 0.15], [0.1, 0.2], true),
            EidolonArmour.Reduce(damage, tier, iron, loss), 4);
    }

    [Fact]
    public void EveryPlateMetalMatchesTheGamesMathsAtEveryTier()
    {
        var settings = new EidolonConfig();
        foreach (var metal in EidolonFittings.PlateMetals)
        {
            var a = settings.PlateArmour.For(metal)!;
            for (int tier = 0; tier <= 8; tier++)
                foreach (float dmg in new[] { 0.5f, 3f, 10f, 40f })
                    Assert.Equal(
                        Expected(dmg, tier, a.RelativeProtection, a.FlatDamageReduction, a.ProtectionTier, [0.03, 0.15], [0.1, 0.2], true),
                        EidolonArmour.Reduce(dmg, tier, a, settings.ArmourLoss), 4);
        }
    }

    [Fact]
    public void ArmourNotResistantToHighTiersLosesTheWholeLossPastItsTier()
    {
        var loss = new ArmourTierLoss { HighDamageTierResistant = false };
        var a = new PlateArmour(1, 0.9, 1.5);
        Assert.Equal(Expected(20, 4, 0.9, 1.5, 1, [0.03, 0.15], [0.1, 0.2], false), EidolonArmour.Reduce(20, 4, a, loss), 4);
    }

    [Fact]
    public void BetterMetalsTakeMoreOffAHardHit()
    {
        var settings = new EidolonConfig();
        float Hit(string metal) => EidolonArmour.Reduce(30, 4, settings.PlateArmour.For(metal)!, settings.ArmourLoss);
        Assert.True(Hit(Stainless) < Hit("steel"));
        Assert.True(Hit("steel") < Hit("iron"));
        Assert.True(Hit("iron") < Hit("brass"));
        Assert.True(Hit("brass") < 30);
    }

    [Theory]
    [InlineData("BluntAttack", "Entity", true)]
    [InlineData("SlashingAttack", "Player", true)]
    [InlineData("PiercingAttack", "Internal", false)]
    [InlineData("Fire", "Block", false)]
    [InlineData("Gravity", "Fall", false)]
    public void ArmourActsOnAttacksOnly(string type, string source, bool applies) =>
        Assert.Equal(applies, EidolonArmour.Applies(type, source));

    // ---- Settings ----

    [Fact]
    public void FittingSettingsOutOfRangeFallBackToTheirDefaults()
    {
        var c = new EidolonConfig
        {
            AllSteelHealth = -5,
            PlateHealth = new PlateMetalFactors { Iron = 1, Steel = double.NaN, Stainless = 2, Brass = 0 },
            JointHealth = null!,
            PlateArmour = new PlateArmourByMetal { Iron = new PlateArmour(-1, 2, -3), Brass = null! },
            ArmourLoss = new ArmourTierLoss { RelativeLossAboveTier = 1.5 },
        };
        var fixes = c.Sanitise();
        Assert.Equal(300, c.AllSteelHealth);
        Assert.Equal(1.5, c.PlateHealth.Steel);
        Assert.Equal(0.8, c.PlateHealth.Brass);
        Assert.Equal(1.5, c.JointHealth.Steel);
        Assert.Equal(3, c.PlateArmour.Iron.ProtectionTier);
        Assert.Equal(0.96, c.PlateArmour.Iron.RelativeProtection);
        Assert.Equal(1.7, c.PlateArmour.Iron.FlatDamageReduction);
        Assert.Equal(2, c.PlateArmour.Brass.ProtectionTier);
        Assert.Equal(0.15, c.ArmourLoss.RelativeLossAboveTier);
        Assert.Equal(9, fixes.Count);
        Assert.Empty(new EidolonConfig().Sanitise());
    }

    [Fact]
    public void FittingSettingsReadFromJsonKeepTheDefaultsTheFileLacks()
    {
        // As the game reads ModConfig (Newtonsoft, filling the defaults' objects in place).
        var c = JsonConvert.DeserializeObject<EidolonConfig>(
            """{ "PlateHealth": { "Brass": 0.5 }, "PlateArmour": { "Steel": { "ProtectionTier": 6 } } }""")!;
        Assert.Empty(c.Sanitise());
        Assert.Equal(0.5, c.PlateHealth.Brass);
        Assert.Equal(1.5, c.PlateHealth.Steel);
        Assert.Equal(6, c.PlateArmour.Steel.ProtectionTier);
        Assert.Equal(0.97, c.PlateArmour.Steel.RelativeProtection);
        Assert.Equal(1.6, c.PlateArmour.Brass.FlatDamageReduction);
    }

    [Fact]
    public void ChangingASteelMultiplierKeepsAnAllSteelEidolonAtItsHealth()
    {
        var c = new EidolonConfig();
        c.PlateHealth.Steel = 3;
        Assert.Equal(300, EidolonStats.Of(EidolonBuild.OldBuild, c).MaxHealth, 9);
    }

    [Fact]
    public void A_random_build_is_complete_and_allowed()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var b = EidolonBuild.Random(new Random(seed));
            Assert.True(b.Complete);
            foreach (var layer in EidolonFittings.Layers)
                Assert.True(EidolonFittings.Allows(layer, b[layer]!));
        }
        Assert.NotEqual(EidolonBuild.Random(new Random(1)), EidolonBuild.Random(new Random(2)));
    }
}
