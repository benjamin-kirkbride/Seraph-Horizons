using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Guarding (#680): which creatures are hostile, read as the game's creature JSON gives their
/// attack tasks (the tasks below are vanilla's, cut to what matters), and the guard's geometry.</summary>
public class EidolonGuardingTests
{
    private static readonly string[] Player = ["player"];

    // Drifter, locust, shiver: a melee attack on players, always.
    private static readonly CreatureTask[] Drifter =
        [new("meleeattack", Player), new("throwatentity", Player), new("seekentity", Player), new("wander", [])];

    // Bowtorn: a melee attack and its turret mode with no entityCodes (the game's default, players).
    private static readonly CreatureTask[] Bowtorn = [new("meleeattack", Player), new("turretmode", Player)];

    // Wolf: one attack on players unconditionally, others only when angry or fleeing.
    private static readonly CreatureTask[] Wolf =
    [
        new("meleeattack", ["deer-*", "goat-*", "player"]),
        new("meleeattack", ["chicken-*", "hare-*"], WhenIn: ["fleeondamage"]),
        new("seekentity", Player, WhenNotIn: ["saturated"]),
    ];

    // Sheep and boar: they fight back when hurt, and charge a player who comes close while wild (an
    // attack the game drops as they are bred).
    private static readonly CreatureTask[] Sheep =
    [
        new("meleeattack", Player, WhenIn: ["aggressiveondamage"]),
        new("meleeattack", Player, WhenNotIn: ["aggressiveondamage"], MaxGeneration: 2),
        new("seekentity", Player, WhenIn: ["aggressiveondamage"]),
        new("seekentity", Player, MaxGeneration: 2),
    ];

    // Fox: hunts chickens and hares always, players only when hurt.
    private static readonly CreatureTask[] Fox =
    [
        new("meleeattack", Player, WhenIn: ["aggressiveondamage"]),
        new("meleeattack", ["chicken-rooster", "hare-*"], WhenNotIn: ["saturated"]),
    ];

    // The mech helper and hacked locust: they strike what their owner fights, at anything.
    private static readonly CreatureTask[] MechHelper =
        [new("meleeattacktargetingentity", ["*"]), new("jealousmeleeattack", ["locust-*"])];

    private static bool Hostile(CreatureTask[] tasks, string[]? states = null, int generation = 0, bool owned = false, bool tamed = false,
        CreatureHostility hostility = CreatureHostility.Aggressive) =>
        EidolonHostility.IsHostile(tasks, s => states?.Contains(s) == true, generation, owned, tamed, hostility);

    [Fact]
    public void TheArchivesCreaturesAndWildPredatorsAreHostile()
    {
        Assert.True(Hostile(Drifter));
        Assert.True(Hostile(Bowtorn));
        Assert.True(Hostile(Wolf));
    }

    [Fact]
    public void GrazersAndSmallHuntersAreHostileOnlyWhenAngry()
    {
        Assert.False(Hostile(Sheep));
        Assert.True(Hostile(Sheep, ["aggressiveondamage"]));
        Assert.True(Hostile(Sheep, ["aggressivearoundentities"])); // a sow with piglets about
        Assert.False(Hostile(Fox));
        Assert.True(Hostile(Fox, ["aggressiveondamage"]));
        Assert.False(Hostile(Wolf.Skip(1).ToArray(), ["fleeondamage"])); // its hunting attacks never target players
    }

    [Fact]
    public void OwnedTamedOrBredCreaturesAreNeverHostile()
    {
        Assert.False(Hostile(Wolf, owned: true));
        Assert.False(Hostile(Wolf, tamed: true));
        Assert.False(Hostile(Wolf, generation: 1));
        Assert.False(Hostile(Sheep, ["aggressiveondamage"], generation: 3));
        Assert.False(Hostile(MechHelper));
    }

    [Fact]
    public void TheWorldsHostilitySettingHolds()
    {
        Assert.False(Hostile(Drifter, hostility: CreatureHostility.Off));
        Assert.False(Hostile(Drifter, hostility: CreatureHostility.Passive));
        Assert.True(Hostile(Drifter, ["aggressiveondamage"], hostility: CreatureHostility.Passive));
    }

    [Fact]
    public void GenerationLimitsOnATaskHold()
    {
        CreatureTask[] laterOnly = [new("meleeattack", Player, MinGeneration: 2)];
        Assert.False(Hostile(laterOnly));
    }

    [Theory]
    [InlineData("player", true)]
    [InlineData("*", true)]
    [InlineData("pla*", true)]
    [InlineData("players", false)]
    [InlineData("wolf-*", false)]
    public void TargetCodesAreReadAsTheGameReadsThem(string code, bool expected) =>
        Assert.Equal(expected, EidolonHostility.TargetsPlayers([code]));

    [Theory]
    [InlineData("meleeattack", true)]
    [InlineData("eidolonmeleeattack", true)]
    [InlineData("throwatentity", true)]
    [InlineData("turretmode", true)]
    [InlineData("seekentity", false)]
    [InlineData("fleeentity", false)]
    [InlineData("meleeattacktargetingentity", false)]
    public void OnlyAttackTasksCount(string code, bool expected) => Assert.Equal(expected, EidolonHostility.IsAttack(code));

    [Fact]
    public void TheGuardWatchesItsRadiusAndGoesForTheNearest()
    {
        Assert.True(EidolonGuard.Watches(10, 16));
        Assert.True(EidolonGuard.Watches(16, 16));
        Assert.False(EidolonGuard.Watches(25, 16));
        Assert.Equal(-1, EidolonGuard.Choose([]));
        Assert.Equal(1, EidolonGuard.Choose([9, 3, 5]));
        Assert.True(EidolonGuard.AtHome(1, 0.5, 0.5));
        Assert.False(EidolonGuard.AtHome(2, 0, 0));
        Assert.False(EidolonGuard.AtHome(0, 2, 0));
    }

    [Fact]
    public void TheGuardSettingsHaveSaneDefaultsAndBadValuesAreReset()
    {
        var config = new EidolonConfig { GuardRadius = 0, SlamDamage = float.NaN };
        Assert.Equal(2, config.Sanitise().Count);
        Assert.Equal(16, config.GuardRadius);
        Assert.Equal(EidolonConfig.Defaults.SlamDamage, config.SlamDamage);
        Assert.True(EidolonConfig.Defaults.SlamDamage > EidolonConfig.Defaults.DefenceDamage);
    }
}
