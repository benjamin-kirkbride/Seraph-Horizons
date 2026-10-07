using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities;
using SeraphHorizons.Mod.Rosser;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Rosser/DebarkedTrunks: the Rosser switch's debarked tree trunk, a third
/// state of Logging Expanded's <c>branches</c> variant added by JSON patch, against the pinned
/// Logging Expanded, Immersive Woodworking, Carry On and Cartwright's Caravan. Each scenario makes
/// its trunk with <see cref="Trunks.Debark"/>, as the rosser will, and works it through Logging
/// Expanded's own interaction methods. Trunk entities run in this world, so a trunk reaches a
/// station through Carry On's hands and comes back into them; a debarked trunk lying on the
/// ground is a trunk entity, whose tools are <see cref="TrunkToolScenarios"/>'.
/// </summary>
public partial class WoodworkingRosserScenarios
{
    private const string LoggingMod = "loggingmod";

    /// <summary>The woods Logging Expanded gives trunks for that the pack can grow: those its tree
    /// manager has a placed log for.</summary>
    private List<string> PackTrunkWoods()
    {
        var woods = W.Blocks.Where(b => b?.Code is { Domain: LoggingMod } c && c.Path.StartsWith(TrunkCode.Prefix)
                                        && b.Variant["size"] == "md" && b.Variant["branches"] == "no" && b.Variant["side"] == "north")
            .Select(b => b.Variant["wood"]!).ToList();
        return woods.Where(w => Mod.Logging!.PlacedLogCode(w) is { } code && W.GetBlock(code) is { Id: > 0 }).ToList();
    }

    private static ItemStack? StoredLogStack(ItemStack trunk) => (trunk.Attributes["slots"] as Vintagestory.API.Datastructures.ITreeAttribute)?.GetItemstack("0");

    /// <summary>Carries <paramref name="trunk"/> in the shop's player's Carry On hands (trunk
    /// entities run: a trunk is never held), in place of whatever was carried.</summary>
    private static void Carry(Woodshop shop, ItemStack trunk)
    {
        shop.Holding((ItemStack?)null);
        TrunkCarry.Take((IServerPlayer)shop.P);
        Assert.True(TrunkCarry.TryGive((IServerPlayer)shop.P, trunk), "the trunk could not be carried");
    }

    /// <summary>Takes what the shop's player carries in Carry On's hands.</summary>
    private static ItemStack? TakeCarried(Woodshop shop) => TrunkCarry.Take((IServerPlayer)shop.P);

    [AtlasScenario, ReadsBootLog]
    public void Debarked_trunks_exist_for_every_trunk_with_the_clean_ones_shape_and_debarked_bark()
    {
        Assert.True(World.Api.LoadModConfig(SeraphHorizonsSystem.ConfigFile)["Rosser"].AsBool(false));
        var problems = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("rosser", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("debarked trunk"))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(problems.Count == 0, "Logged:\n" + string.Join("\n", problems));

        var trunks = W.Blocks.Where(b => b?.Code is { Domain: LoggingMod } c && c.Path.StartsWith(TrunkCode.Prefix)).ToList();
        int clean = trunks.Count(b => b.Variant["branches"] == "no");
        Assert.Equal(55 * 6 * 4, clean);
        Assert.Equal(clean, trunks.Count(b => b.Variant["branches"] == "debarked"));

        var woods = PackTrunkWoods();
        output.WriteLine($"Pack trunk woods: {string.Join(", ", woods)}");
        Assert.Contains("oak", woods);
        Assert.Contains("redwood", woods);
        foreach (var wood in woods)
        foreach (var side in new[] { "north", "east" })
        foreach (var size in new[] { "xs", "md", "xxl" })
        {
            var cleanBlock = BlockOf($"loggingmod:treetrunk-{wood}-{size}-no-{side}");
            var debarked = BlockOf($"loggingmod:treetrunk-{wood}-{size}-debarked-{side}");
            Assert.Equal($"loggingmod:treetrunk-{size}-no", debarked.Shape.Base.ToString());
            Assert.Equal(cleanBlock.Shape.rotateY, debarked.Shape.rotateY);

            // Multiblock size, behaviours (Carry On's Carryable), held looks and Cartwright's
            // attachment all come from the blocktype, by-type patterns included.
            Assert.Equal(cleanBlock.BlockBehaviors.Select(b => b.GetType().Name), debarked.BlockBehaviors.Select(b => b.GetType().Name));
            Assert.Contains(debarked.BlockBehaviors, b => b.GetType().Name.Contains("Carryable"));
            Assert.Equal(MultiblockSize(cleanBlock), MultiblockSize(debarked));
            Assert.Equal(cleanBlock.HeldRightTpIdleAnimation, debarked.HeldRightTpIdleAnimation);
            Assert.Equal(cleanBlock.TpHandTransform?.Translation.ToString(), debarked.TpHandTransform?.Translation.ToString());
            Assert.True(debarked.Attributes?["attachableToEntity"].Exists, "no Cartwright's attachment");
            Assert.True(debarked.Attributes!["isTreeTrunk"].AsBool());
            Assert.Equal(new ItemStack(cleanBlock).GetName() + " (Debarked)", new ItemStack(debarked).GetName());
            Assert.Empty(debarked.CreativeInventoryTabs ?? []);
        }

        // The server keeps no block textures (Block.Textures is null there, the clean trunk's too), so
        // the patched asset is read instead: every wood's entry resolves "wood-h" (the bark faces)
        // and "wood" (the ends) to the debarked textures on a debarked trunk (the game's solveByType
        // merges a nested wood-hByType and woodByType into them),
        // and every pack wood's texture file exists. Woods from mods the pack does not have point
        // into Wildcraft: Trees' domain, as Logging Expanded's bark does: harmless, never felled.
        var asset = Newtonsoft.Json.Linq.JObject.Parse(World.Api.Assets.Get(new AssetLocation("loggingmod:blocktypes/treetrunk.json")).ToText());
        Assert.Contains("debarked", asset["variantgroups"]![2]!["states"]!.Select(t => (string)t!));
        Assert.Equal("loggingmod:treetrunk-{size}-no", (string?)asset["shapeByType"]?["*-debarked-*"]?["base"]);
        var textures = (Newtonsoft.Json.Linq.JObject)asset["texturesByType"]!;
        Newtonsoft.Json.Linq.JToken Entry(string wood) =>
            textures.Properties().First(p => WildcardUtil.Match(p.Name, $"treetrunk-{wood}-md-debarked-north")).Value;
        string DebarkedBark(string wood) => ((string?)Entry(wood)["wood-hByType"]?["*-debarked-*"]?["base"])!.Replace("{wood}", wood);
        // the ends: the vanilla debarked log's end grain (no bark ring), where the game has one
        string? DebarkedEnds(string wood) => ((string?)Entry(wood)["woodByType"]?["*-debarked-*"]?["base"])?.Replace("{wood}", wood);
        // A server-only install (CI's) ships no textures at all, so the files can only be looked for
        // under a full game install, which is told by the clean trunk's own bark being there.
        bool Exists(AssetLocation texture) => World.Api.Assets.Exists(texture.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        var hasTextures = Exists(new AssetLocation("game:block/wood/bark/oak"));
        bool TextureExists(AssetLocation texture) => !hasTextures || Exists(texture);
        foreach (var wood in woods)
        {
            var bark = new AssetLocation(DebarkedBark(wood));
            Assert.Equal($"game:block/wood/debarked/{wood}", bark.ToString());
            Assert.True(TextureExists(bark), $"no texture {bark}");
            var ends = new AssetLocation(DebarkedEnds(wood) ?? "");
            Assert.Equal($"game:block/wood/treetrunk/debarked/{wood}", ends.ToString());
            Assert.True(TextureExists(ends), $"no texture {ends}");
            // the clean trunk's ends are the bark-ringed ones, untouched
            Assert.Equal($"game:block/wood/treetrunk/{wood}", (string?)Entry(wood)["wood"]?["base"]);
        }
        Assert.Equal("wildcrafttree:block/wood/debarked/yew", DebarkedBark("yew"));
        // Wildcraft: Trees is not in the pack: its woods keep Logging Expanded's ends.
        Assert.Null(DebarkedEnds("yew"));
        // No cherry wood in the game; its trunk takes oak's debarked textures, as its end grain is oak's.
        Assert.Equal("game:block/wood/debarked/oak", DebarkedBark("cherry"));
        Assert.Equal("game:block/wood/treetrunk/debarked/oak", DebarkedEnds("cherry"));
    }

    private static string Describe(IDictionary<string, CompositeTexture>? textures) =>
        textures == null ? "null" : string.Join(", ", textures.Select(t => $"{t.Key}={t.Value?.Base}"));

    private static string MultiblockSize(Block block)
    {
        var behavior = block.GetBehavior<Vintagestory.GameContent.BlockBehaviorMultiblock>()!;
        return string.Join(",", new[] { "SizeX", "SizeY", "SizeZ" }.Select(f => AccessTools.Field(behavior.GetType(), f)?.GetValue(behavior)));
    }

    [AtlasScenario]
    public void Debark_keeps_wood_size_side_and_logs_and_drops_the_branch_count()
    {
        var branchy = Trunk("oak", 12, branched: true, size: "md");
        branchy.Attributes.SetFloat("resinMaxMl", 300);
        var debarking = Trunks.Debark(branchy, W)!;
        Assert.Equal("loggingmod:treetrunk-oak-md-debarked-north", debarking.Trunk.Collectible.Code.ToString());
        Assert.Equal(3, debarking.Branches);
        Assert.Equal(12, debarking.Logs);
        Assert.False(debarking.Trunk.Attributes.HasAttribute(Trunks.BranchCountKey));
        Assert.Equal(300f, debarking.Trunk.Attributes.GetFloat("resinMaxMl"));
        Assert.True(DebarkedTrunks.IsMarked(StoredLogStack(debarking.Trunk)));
        // The input is not changed.
        Assert.Equal("yes", branchy.Block.Variant["branches"]);
        Assert.Equal(3, branchy.Attributes.GetInt(Trunks.BranchCountKey));
        Assert.False(DebarkedTrunks.IsMarked(StoredLogStack(branchy)));

        Assert.True(Trunks.IsDebarked(debarking.Trunk));
        Assert.False(Trunks.IsBranched(debarking.Trunk));
        var again = Trunks.Debark(debarking.Trunk, W)!;
        Assert.Equal(debarking.Trunk.Collectible.Code, again.Trunk.Collectible.Code);
        Assert.Equal(0, again.Branches);
        Assert.Null(Trunks.Debark(new ItemStack(BlockOf("game:log-placed-oak-ud")), W));

        // The mill shows it debarked, at its class's model size.
        Assert.Equal("loggingmod:treetrunk-oak-lg-debarked-north", Trunks.ShownBlock(W, debarking.Trunk)!.Code.ToString());
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_trunk_storage_rack_stores_and_returns_a_debarked_trunk()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-200, 3, 240));
        var pos = shop.Cell(0);
        World.SetBlock("loggingmod:trunkstorage-oak-empty-north", pos);
        await World.Ticks(2);
        var rack = shop.Entity(pos);
        var logging = Mod.Logging!;
        Assert.True(logging.IsRack(rack));

        var trunk = DebarkedTrunk("birch", 20, size: "md");
        Carry(shop, trunk.Clone());
        Assert.True(shop.Click(pos));
        Assert.Null(TrunkCarry.Carried(shop.P));
        Assert.Equal("debarked", logging.PeekTrunk(rack)!.Block.Variant["branches"]);
        var back = logging.PopTrunk(rack)!;
        Assert.True(back.Equals(W, trunk, GlobalConstants.IgnoredStackAttributes), "the trunk came back changed");
        Assert.True(DebarkedTrunks.IsMarked(StoredLogStack(back)));
    }

    public static TheoryData<string> SawhorseCodes => new()
    {
        "loggingmod:sawhorse-north",
        "loggingmod:sawhorseadvanced-oak-north",
    };

    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(SawhorseCodes))]
    public async Task A_sawhorse_gives_debarked_logs_from_a_debarked_trunk_with_no_bark_and_unloads_it_debarked(string sawhorse)
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-240, 3, 200 + (sawhorse.Contains("advanced") ? 30 : 0)));
        var pos = shop.Cell(0);
        World.SetBlock(sawhorse, pos);
        await World.Ticks(2);
        Carry(shop, DebarkedTrunk("oak", 8));
        Assert.True(shop.Click(pos));
        Assert.Equal(8, shop.LogsOn(pos));
        Assert.True(DebarkedTrunks.IsDebarkedLoad((InventoryBase)AccessTools.Property(shop.Entity(pos).GetType(), "Inventory").GetValue(shop.Entity(pos))!));

        // The axe: Logging Expanded's log work, giving debarked logs.
        shop.Holding(Woodshop.Axe);
        await shop.Work(pos);
        var made = await shop.Collect();
        output.WriteLine("axe: " + string.Join(", ", made.Select(m => $"{m.Value}x {m.Key}")));
        Assert.True(made.GetValueOrDefault("game:debarkedlog-oak-ud") > 0);
        Assert.Single(made);

        // The axe and hammer: debarked logs and no bark.
        shop.InOffhand(Woodshop.Hammer);
        await shop.Work(pos);
        shop.InOffhand(null);
        made = await shop.Collect();
        output.WriteLine("axe and hammer: " + string.Join(", ", made.Select(m => $"{m.Value}x {m.Key}")));
        Assert.True(made.GetValueOrDefault("game:debarkedlog-oak-ud") > 0);
        Assert.Single(made);

        // The spud has nothing to do.
        int before = shop.LogsOn(pos);
        shop.Holding(Woodshop.Spud);
        Assert.False(shop.Hold(pos, 0.8f));
        Assert.Empty(await shop.Collect());
        Assert.Equal(before, shop.LogsOn(pos));

        // The saw: boards, as ever.
        shop.Holding(Woodshop.Saw);
        await shop.Work(pos);
        made = await shop.Collect();
        Assert.True(made.GetValueOrDefault("game:plank-oak") > 0);
        Assert.Single(made);

        // Unloaded with an empty hand into Carry On's hands: the debarked trunk of what is left.
        int left = shop.LogsOn(pos);
        shop.Holding((ItemStack?)null);
        shop.Click(pos);
        await World.Ticks(2);
        var back = TakeCarried(shop);
        Assert.NotNull(back);
        Assert.Equal("debarked", back.Block.Variant["branches"]);
        Assert.Equal(left, Trunks.StoredLogs(back, W));
        Assert.True(DebarkedTrunks.IsMarked(StoredLogStack(back)));
        Assert.Equal(0, shop.LogsOn(pos));
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task The_bucking_mill_cuts_a_debarked_trunk_into_debarked_logs()
    {
        // On a floor of its own, its chunk columns loaded and kept: nothing else in this class keeps
        // the ground near spawn loaded (Sky's spots rely on it).
        var pos = await RosserSky(90, 30, reach: 10);
        var player = await Player("debarker");
        await StandBy(player, pos);
        var mill = await PlaceMill(pos, "south");
        Assemble(mill, player);
        KillItemsNear(pos);
        Assert.Null(Click(player, pos, DebarkedTrunk("oak", 4)));
        Assert.True(mill.Trunk is { } loaded && Trunks.IsDebarked(loaded));
        await Power(mill, fast: true);
        await World.Until(() => mill.Trunk == null, 6000);

        var items = ItemsNear(pos);
        Assert.Equal(Cutting.LogYield(4, Mod.Config.LogsPerStoredLog), items.GetValueOrDefault("game:debarkedlog-oak-ud"));
        Assert.DoesNotContain("game:log-placed-oak-ud", items.Keys);
    }
}
