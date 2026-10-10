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

    /// <summary>Full blocks with the eidolon's box (1.7 wide, 3.75 tall, its feet's centre at the point
    /// asked), as in WidePathTests.</summary>
    private sealed class Voxels : IWideSpace
    {
        public readonly HashSet<(int, int, int)> Solid = [];
        public readonly HashSet<(int, int, int)> Lava = [];

        public static Voxels Floor(int reach)
        {
            var v = new Voxels();
            for (int x = -reach; x <= reach; x++)
            for (int z = -reach; z <= reach; z++)
                v.Solid.Add((x, -1, z));
            return v;
        }

        public bool Free(double x, double y, double z)
        {
            const double half = 0.85, height = 3.75, eps = 1e-6;
            for (int bx = (int)Math.Floor(x - half); bx <= (int)Math.Floor(x + half - eps); bx++)
            for (int by = (int)Math.Floor(y); by <= (int)Math.Floor(y + height - eps); by++)
            for (int bz = (int)Math.Floor(z - half); bz <= (int)Math.Floor(z + half - eps); bz++)
                if (Solid.Contains((bx, by, bz)))
                    return false;
            return true;
        }

        public float Cost(PathCell c) =>
            Lava.Contains((c.X - 1, c.Y, c.Z - 1)) || Lava.Contains((c.X - 1, c.Y, c.Z))
            || Lava.Contains((c.X, c.Y, c.Z - 1)) || Lava.Contains((c.X, c.Y, c.Z))
                ? WidePath.Impassable + 1 : 0;
    }

    private static readonly GuardPost Point = new(0.5, 0, 0.5);

    private static double Across(GuardPost a, GuardPost b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void OnOpenGroundEveryPostIsWithinRangeOnTheFloorAndThePointComesFirst()
    {
        var posts = EidolonGuard.Posts(Voxels.Floor(12), Point, []);
        Assert.Equal(Point, posts[0]);
        Assert.True(posts.Count > 20);
        Assert.All(posts, p =>
        {
            Assert.True(Across(p, Point) <= EidolonGuard.PostRange + 1e-9);
            Assert.Equal(0, p.Y);
        });
    }

    [Fact]
    public void APostNeedsAFloorRoomForTheBodyAndNoLava()
    {
        // Floor only on the point's side (x < 3); a pillar of granite at x -3; lava along z 3.
        var v = new Voxels();
        for (int x = -6; x <= 2; x++)
        for (int z = -6; z <= 6; z++)
            v.Solid.Add((x, -1, z));
        for (int y = 0; y < 5; y++)
            v.Solid.Add((-3, y, 0));
        for (int x = -6; x <= 6; x++)
            v.Lava.Add((x, 0, 3));
        var posts = EidolonGuard.Posts(v, Point, []);
        Assert.NotEmpty(posts);
        Assert.All(posts, p =>
        {
            Assert.True(v.Free(p.X, p.Y, p.Z), $"{p} in a block");
            Assert.False(v.Free(p.X, p.Y - 1, p.Z), $"{p} on no floor");
            Assert.True(v.Cost(WidePath.NodeAt(p.X, p.Y, p.Z)) <= WidePath.Impassable, $"{p} in lava");
        });
        Assert.DoesNotContain(posts, p => p.X > 3.5); // a box at x 3.5 still reaches the floor's edge
    }

    [Fact]
    public void APostOnALedgeOrInAPitStandsOnItsFloor()
    {
        // A step up of one block east of x 2.
        var v = Voxels.Floor(12);
        for (int x = 3; x <= 12; x++)
        for (int z = -12; z <= 12; z++)
            v.Solid.Add((x, 0, z));
        var posts = EidolonGuard.Posts(v, Point, []);
        Assert.Contains(posts, p => p.Y == 1);
        Assert.All(posts, p => Assert.False(v.Free(p.X, p.Y - 1, p.Z)));
    }

    [Fact]
    public void APostKeepsClearOfOtherGuardsPostsAndWhereTheyStand()
    {
        GuardPost[] taken = [Point, new(3.5, 0, 0.5), new(-1.2, 0, -2.7)];
        var posts = EidolonGuard.Posts(Voxels.Floor(12), Point, taken);
        Assert.NotEmpty(posts);
        Assert.All(posts, p => Assert.All(taken, t => Assert.True(Across(p, t) >= EidolonGuard.PostSpacing, $"{p} by {t}")));
        // A guard on the floor above (another storey) does not take a post.
        Assert.True(EidolonGuard.Clear(Point, [new GuardPost(0.5, 5, 0.5)]));
        Assert.False(EidolonGuard.Clear(Point, [new GuardPost(1.5, 0, 1.5)]));
    }

    [Fact]
    public void GuardsTakingPostsOneAfterAnotherNeverShareOne()
    {
        var space = Voxels.Floor(12);
        var random = new Random(680);
        var taken = new List<GuardPost>();
        for (int i = 0; i < 4; i++)
        {
            var post = EidolonGuard.Post(space, Point, taken, random);
            Assert.NotNull(post);
            Assert.All(taken, t => Assert.True(Across(post!.Value, t) >= EidolonGuard.PostSpacing));
            taken.Add(post!.Value);
        }
    }

    [Fact]
    public void WithNoRoomThereIsNoPost()
    {
        // Walled in: granite all round the point but its own column.
        var v = Voxels.Floor(12);
        for (int x = -6; x <= 6; x++)
        for (int z = -6; z <= 6; z++)
            if (x != 0 || z != 0)
                for (int y = 0; y < 8; y++)
                    v.Solid.Add((x, y, z));
        Assert.Null(EidolonGuard.Post(v, Point, [], new Random(1)));
    }

    [Fact]
    public void ItKeepsAPostForARandomWhileAndStopsNearIt()
    {
        var random = new Random(7);
        for (int i = 0; i < 100; i++)
        {
            double s = EidolonGuard.WanderSeconds(random);
            Assert.InRange(s, EidolonGuard.WanderMinSeconds, EidolonGuard.WanderMaxSeconds);
        }
        Assert.True(EidolonGuard.AtPost(0.5, 0.2, 0.2));
        Assert.False(EidolonGuard.AtPost(1, 0, 0));
        Assert.True(EidolonGuard.AtHome(1, 0, 0));
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
