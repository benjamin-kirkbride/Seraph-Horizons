using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Age of Flax (fork, <c>ageofflaxfork</c>, asset domain <c>ageofflax</c>): rebalances its flax
/// processing against vanilla flax, makes its advanced tools take steel, and lets its breaks take
/// rendered fat.
///
/// Most of it is JSON patches shipped in this mod (<see cref="PatchAssets"/>): the ripple and
/// hatchel yields in <c>ageofflax:config/balance.json</c> (which Age of Flax reads again in every
/// tool's and the crop's <c>OnLoaded</c>, after the patches are applied) and its grid recipes.
/// They only apply with ageofflaxfork loaded (<c>dependsOn</c>); with the switch off, or Age of
/// Flax changed, <see cref="DisablePatches"/> empties them in <c>Start</c>, before the game's patch
/// loader runs in <c>AssetsLoaded</c>.
///
/// The seeds move from the ripple back to the plant. The ripple's seed drop is patched to 0, and
/// Age of Flax's <c>BlockCropFlax.GetDrops</c>, which drops only flax bundles at stages 8 and 9 and
/// ignores the blocktype's drops, gets a postfix adding vanilla flax's seeds at those stages
/// (<see cref="Stage9SeedAvg"/>, <see cref="Stage8SeedAvg"/>). The handbook lists a block's drops
/// from its blocktype, so the same seeds are added there too (<see cref="AddSeedsToBlocktype"/>).
/// </summary>
public static class AgeOfFlaxRebalance
{
    public const string ModId = "ageofflaxfork";
    public const string Domain = "ageofflax";
    public const string CropType = "AgeOfFlax.SRC.Common.Blocks.BlockCropFlax";

    public static readonly AssetLocation[] PatchAssets =
    [
        new("seraphhorizons", "patches/ageofflax-balance.json"),
        new("seraphhorizons", "patches/ageofflax-recipes.json"),
    ];

    public static readonly AssetLocation CropAsset = new("game", "blocktypes/plant/crop/flax.json");
    public static readonly AssetLocation Seeds = new("game", "seeds-flax");

    /// <summary>Vanilla 1.22 flax (<c>survival/blocktypes/plant/crop/flax.json</c>): ripe
    /// (<c>*-9</c>) drops avg 1.2 seeds, every other stage (<c>*</c>) avg 0.7. Age of Flax keeps its
    /// own seed drops below stage 8, and drops bundles at 8 and 9; this adds the seeds there.</summary>
    public const float Stage9SeedAvg = 1.2f, Stage8SeedAvg = 0.7f;

    /// <summary>Age of Flax's drying rack speeds drying by <c>drying.dryingRackSpeedMultiplier</c>
    /// in balance.json (3), not the 2x its text says. This tweak leaves it alone and fixes the text
    /// (<see cref="LangEdits"/>).</summary>
    public const float DryingRackSpeedMultiplier = 3f;

    private static MethodInfo? _getDrops;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Finds <c>BlockCropFlax.GetDrops</c>. Returns whether it was found.</summary>
    public static bool Bind(ILogger logger)
    {
        var crop = AccessTools.TypeByName(CropType);
        _getDrops = crop == null || !typeof(BlockCrop).IsAssignableFrom(crop)
            ? null
            : AccessTools.DeclaredMethod(crop, nameof(Block.GetDrops),
                [typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float)]);
        if (_getDrops?.ReturnType == typeof(ItemStack[]))
            return true;
        _getDrops = null;
        logger.Warning($"[seraphhorizons] {CropType} does not override GetDrops as expected; Age of Flax changed, so "
                       + "it is not rebalanced");
        return false;
    }

    /// <summary>Empties this mod's Age of Flax patch files, so the patch loader applies none of
    /// them. Runs in Start: the assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        foreach (var location in PatchAssets)
        {
            var asset = api.Assets.TryGet(location);
            if (asset != null)
                asset.Data = "[]"u8.ToArray();
        }
    }

    /// <summary>Postfixes <c>BlockCropFlax.GetDrops</c> with <see cref="GetDropsPostfix"/>.</summary>
    public static void Patch(Harmony harmony) =>
        harmony.Patch(_getDrops, postfix: new HarmonyMethod(typeof(AgeOfFlaxRebalance), nameof(GetDropsPostfix)));

    /// <summary>Adds vanilla flax's seeds to a stage 8 or 9 plant's drops, rounded at random as
    /// Age of Flax rounds its bundles, and scaled by the same drop multiplier.</summary>
    public static void GetDropsPostfix(Block __instance, IWorldAccessor world, float dropQuantityMultiplier,
        ref ItemStack[] __result)
    {
        float avg = SeedAvg(__instance);
        if (avg <= 0f || world.GetItem(Seeds) is not { } seeds)
            return;
        int count = GameMath.RoundRandom(world.Rand, avg * dropQuantityMultiplier);
        if (count > 0)
            __result = [.. __result ?? [], new ItemStack(seeds, count)];
    }

    /// <summary>The seeds this tweak adds for a flax crop block, by its growth stage.</summary>
    public static float SeedAvg(Block crop) => (crop as BlockCrop)?.CurrentCropStage switch
    {
        9 => Stage9SeedAvg,
        8 => Stage8SeedAvg,
        _ => 0f,
    };

    /// <summary>Adds the seeds to the flax blocktype's stage 8 and 9 drops, which Age of Flax's
    /// patch made bundles only. Age of Flax's GetDrops does not read them; the handbook and the
    /// recipe browser do. Runs on the server in AssetsLoaded, after the patch loader (0.05) and
    /// before the blocktypes are read (0.2).</summary>
    public static void AddSeedsToBlocktype(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(CropAsset);
        if (asset == null)
            return;
        var json = JObject.Parse(asset.ToText());
        if (json["dropsByType"] is not JObject drops || drops["*-9"] is not JArray ripe || drops["*-8"] is not JArray stage8)
        {
            api.Logger.Warning("[seraphhorizons] The flax blocktype has no *-9 and *-8 drops; Age of Flax changed, so "
                               + "the handbook does not list the seeds");
            return;
        }
        AddSeeds(ripe, Stage9SeedAvg);
        AddSeeds(stage8, Stage8SeedAvg);
        asset.Data = Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void AddSeeds(JArray drops, float avg)
    {
        if (drops.Any(drop => (string?)drop["code"] is "seeds-flax" or "game:seeds-flax"))
            return;
        drops.Add(new JObject
        {
            ["type"] = "item",
            ["code"] = Seeds.ToString(),
            ["quantity"] = new JObject { ["avg"] = avg },
        });
    }

    private const string Ripple = "<a href=\"handbook://block-ageofflax:ripple-primitive-east\">ripple</a>";

    /// <summary>Age of Flax's English text (the only language it ships), made to match: the seeds
    /// come from the plant, the ripple only strips the grain; the drying rack dries 3x faster (it
    /// said 2x); the guide gives each tier's yields and materials; and the dried and broken bundles'
    /// descriptions, which a duplicate key in Age of Flax's lang file mixed up, are put right.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "ageofflax:itemdesc-flaxbundle-unprocessed",
            "Ripple to extract flax seeds",
            "Ripple to strip the flax grain"),
        // Age of Flax's file sets this key twice, so the dried bundle reads as the broken one should.
        new("en", "ageofflax:itemdesc-flaxbundle-dried",
            "Hatch to extract flax fibers",
            "Break to prepare for the hatchel"),
        new("en", "ageofflax:itemdesc-flaxbundle-broken",
            "",
            "Hatchel to extract flax fibers"),

        .. from tier in (string[])["primitive", "simple", "advanced"]
           from side in (string[])["north", "east", "south", "west"]
           select new LangEdit("en", $"ageofflax:blockdesc-ripple-{tier}-{side}",
               "A tool used for extracting seeds from flax",
               "A tool used for stripping the grain from flax"),
        .. from side in (string[])["north", "east", "south", "west"]
           select new LangEdit("en", $"ageofflax:blockdesc-dryingrack-{side}",
               "Will dry retted flax about 2x faster.",
               "Will dry retted flax 3x faster."),

        new("en", "ageofflax:item-handbooktext-flaxbundle-unprocessed",
            "Step 1 of 5: Extact seeds from the flax by holding",
            "Step 1 of 5: Strip the grain from the flax by holding"),
        new("en", "ageofflax:item-handbooktext-flaxbundle-unprocessed",
            Ripple + ".",
            Ripple + ". The ripple gives no seeds: those drop when you harvest the flax."),
        new("en", "ageofflax:item-handbooktext-flaxbundle-retted",
            "dry 2x faster.",
            "dry 3x faster."),
        new("en", "ageofflax:item-handbooktext-flaxbundle-broken",
            "Step 5 of 5: Extact flax fibers",
            "Step 5 of 5: Extract flax fibers"),

        new("en", "ageofflax:craftinginfo-flax-text",
            "After harvesting the flax you can extract the seeds and grain from the "
            + "<a href=\"handbook://item-ageofflax:flaxbundle-unprocessed\">flax bundle</a> by holding "
            + "<hk>rightmouse</hk> on a " + Ripple + ".",
            "Harvesting ripe flax gives <a href=\"handbook://item-ageofflax:flaxbundle-unprocessed\">flax "
            + "bundles</a> and the plant's seeds. Strip the grain from the bundles by holding <hk>rightmouse</hk> on "
            + "a " + Ripple + "; the ripple gives no seeds."),
        new("en", "ageofflax:craftinginfo-flax-text",
            "dry 2x faster.",
            "dry 3x faster."),
        new("en", "ageofflax:craftinginfo-flax-text",
            "Now the primitive versions of the ripple, hatchel and break do serve their purpose well, but using "
            + "more advanced versions of these tools will result in a better extraction",
            "The primitive ripple, break and hatchel (flint) work one bundle at a time, the simple ones (copper) "
            + "two and the advanced ones (steel) four. Better tools also extract more. For each bundle a ripple "
            + "strips about 1.7 flax grain at primitive, 2.5 at simple and 3.3 at advanced, and a hatchel combs out "
            + "about 2.2, 3.3 and 4.4 flax fibers. For a ripe plant (1.2 bundles on average) that is about two thirds "
            + "of the grain and fibers vanilla flax drops with primitive tools, the same with simple ones, and a "
            + "third more with advanced ones. "
            + "Every break takes raw or rendered fat"),
    ];
}
