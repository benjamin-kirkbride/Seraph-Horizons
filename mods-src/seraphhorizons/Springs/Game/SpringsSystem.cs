using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeraphHorizons.Mod.Springs.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// Springs (README "Springs"): the game's rivulet generator (<c>GenRivulets</c>) places a solid
/// spring of the surrounding rock (<see cref="BlockWaterSpring"/>) where it placed a single water
/// or rapids source, in new chunks only. A spring pours as that source did while no pipe couples to
/// it, feeds ppex pipes coupled to it with well water up to its rate instead, and gives a player
/// well water to drink or carry away.
///
/// With Harmony (own id, patched once per process, both sides; the game's types are referenced, the
/// pipes' found by name):
/// <list type="bullet">
/// <item>a transpiler on <c>GenRivulets.tryGenRivulet</c> and <c>tryGenMountainSideRivers</c>: their
/// one call to <c>IWorldGenBlockAccessor.ScheduleBlockUpdate</c>, made right after the source is
/// set, goes through <see cref="OnRivuletPlaced"/>, which puts the spring in its place first (lava
/// rivulets stay);</item>
/// <item>a postfix on <c>BlockBehaviorFiniteSpreadingLiquid.GetMaxNeighbourLiquidLevel</c>: a
/// pouring spring of a flowing cell's liquid, above it or beside it, counts as a source block, so
/// what it poured stays (<see cref="SpringPour.Feed"/>), a rapids spring only for the cell on the
/// one face it pours out of;</item>
/// <item>a postfix on ExpandedLib's <c>BlockNetworkNode.IsValidNonNetworkConnection</c>: a pipe end
/// against a spring is sealed, not an open end leaking its run;</item>
/// <item>a prefix on ppex's <c>BlockEntityMpFluidPump.DoWork</c>: a mechanical pump draws on a spring's
/// run with no intake on it, and makes the spring give more (<see cref="SpringPumpBridge"/>).</item>
/// </list>
/// Off: nothing is patched, rivulets are the game's, and every spring turns back into the source it
/// replaced as it loads (<see cref="BlockEntityWaterSpring"/>, <see cref="Revert"/>). The blocks are
/// registered either way, so they can.
/// </summary>
public class SpringsSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.springs";
    public const string BlockClass = "seraphhorizons.WaterSpring";
    public const string GuidePageCode = "seraphhorizons-springs";
    public const string GuideTitleKey = "seraphhorizons:springs-guide-title";
    public const string GuideTextKey = "seraphhorizons:springs-guide-text";
    public const string NodeBlockType = "ExpandedLib.Networks.BlockNetworkNode";

    public static readonly AssetLocation BlockAsset = new(SpringCodes.Domain, "blocktypes/waterspring.json");

    private static bool _on;
    private static SpringsConfig _settings = new();
    private static ILogger? _logger;
    // (kind, rock) to the spring's block id, for the generator's thread: built once, then only read.
    private static Dictionary<(SpringKind, string), int> _springIds = new();
    private static string? _entityClass;
    private static long _generated;
    private static long _rivulets;
    private static int _hooked;
    private static bool _placeFailed;

    private Harmony? _harmony;
    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _hidePage;

    /// <summary>Whether springs are on (the switch; this side's config).</summary>
    public static bool On => _on;

    /// <summary>The rates in use (sanitised).</summary>
    public static SpringsConfig Settings => _settings;

    /// <summary>Springs placed by the generator in this process.</summary>
    public static long Generated => Interlocked.Read(ref _generated);

    /// <summary>Rivulets the generator has set in this process, of any liquid (lava too).</summary>
    public static long Rivulets => Interlocked.Read(ref _rivulets);

    /// <summary>The generator's calls routed through <see cref="OnRivuletPlaced"/> (2 when both
    /// methods are patched).</summary>
    public static int Hooked => _hooked;

    public override double ExecuteOrder() => 0.12;

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass(BlockClass, typeof(BlockWaterSpring));
        api.RegisterBlockEntityClass(BlockClass, typeof(BlockEntityWaterSpring));
        var config = SeraphHorizonsSystem.ConfigFor(api);
        _settings = config.SpringsSettings ?? new SpringsConfig();
        foreach (var fix in _settings.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Springs: ModConfig/{SeraphHorizonsSystem.ConfigFile}, SpringsSettings: {fix}");
        _on = config.Springs;
        _logger = api.Logger;
        if (_on && !Harmony.HasAnyPatches(HarmonyId))
            Patch(_harmony = new Harmony(HarmonyId), api.Logger);
        if (_on)
            SpringTaps.Bind(api.Logger);
    }

    // Lang files are loaded, mod assets included, before this phase on both sides.
    public override void AssetsLoaded(ICoreAPI api) => FillRates(_settings);

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        var ids = new Dictionary<(SpringKind, string), int>();
        foreach (var block in api.World.Blocks)
            if (block is BlockWaterSpring { Code: not null } spring && SpringCodes.Parse(spring.Code.Path) is { } parsed)
            {
                ids[(parsed.Kind, parsed.Rock)] = spring.Id;
                _entityClass ??= spring.EntityClass;
            }
        _springIds = ids;
        if (_on)
            api.Logger.Notification("[seraphhorizons] Springs: {0} spring blocks; the rivulet generator places them ({1} of 2 methods hooked)", ids.Count, _hooked);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (_on)
            return;
        // The woodworking guide sets the list in Start, before this; the others add to it.
        var hidden = api.ObjectCache.TryGetValue(WoodworkingGuide.HiddenGuidesKey, out var listed)
                     && listed is IEnumerable<(string, string)> pages
            ? pages.ToList()
            : [];
        hidden.Add((GuidePageCode, GuideTitleKey));
        api.ObjectCache[WoodworkingGuide.HiddenGuidesKey] = hidden;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (_on || api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is not { } handbook)
            return;
        _handbook = handbook;
        _hidePage = pages => pages.RemoveAll(p => p.PageCode == GuidePageCode);
        handbook.OnInitCustomPages += _hidePage;
    }

    public override void Dispose()
    {
        if (_handbook != null && _hidePage != null)
            _handbook.OnInitCustomPages -= _hidePage;
        _handbook = null;
        _hidePage = null;
        if (_harmony != null)
        {
            _harmony.UnpatchAll(HarmonyId);
            _harmony = null;
            _hooked = 0;
            SpringTaps.Unbind();
            SpringPumpBridge.Unbind();
        }
    }

    /// <summary>The guide's rates, from this side's settings, in place of its <c>{normal}</c>,
    /// <c>{rapid}</c>, <c>{pumpnormal}</c> and <c>{pumprapid}</c> (the extra at one turn a second;
    /// every language this mod has text in).</summary>
    public static void FillRates(SpringsConfig settings)
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in new[] { GuideTextKey, "seraphhorizons:blockdesc-waterspring-*" })
                if (entries.TryGetValue(key, out var text) && text.Contains('{'))
                    entries[key] = text
                        .Replace("{normal}", SpringsConfig.Format(settings.NormalLitresPerSecond))
                        .Replace("{rapid}", SpringsConfig.Format(settings.RapidLitresPerSecond))
                        .Replace("{pumpnormal}", SpringsConfig.Format(settings.NormalLitresPerSecond * settings.PumpBoostPerTurn))
                        .Replace("{pumprapid}", SpringsConfig.Format(settings.RapidLitresPerSecond * settings.PumpBoostPerTurn));
        }
    }

    // ---- patches ----

    private static void Patch(Harmony harmony, ILogger logger)
    {
        var transpiler = new HarmonyMethod(typeof(SpringsSystem), nameof(RivuletTranspiler));
        foreach (var name in new[] { "tryGenRivulet", "tryGenMountainSideRivers" })
        {
            var method = AccessTools.DeclaredMethod(typeof(GenRivulets), name);
            if (method == null)
                logger.Warning("[seraphhorizons] Springs: the game's GenRivulets.{0} is not there, so its rivulets stay water", name);
            else
                harmony.Patch(method, transpiler: transpiler);
        }

        var level = AccessTools.DeclaredMethod(typeof(BlockBehaviorFiniteSpreadingLiquid), nameof(BlockBehaviorFiniteSpreadingLiquid.GetMaxNeighbourLiquidLevel));
        if (level?.ReturnType == typeof(int))
            harmony.Patch(level, postfix: new HarmonyMethod(typeof(SpringsSystem), nameof(FeedPostfix)));
        else
            logger.Warning("[seraphhorizons] Springs: the game's BlockBehaviorFiniteSpreadingLiquid.GetMaxNeighbourLiquidLevel is not as expected, so a spring's water drains as it pours");

        var nodeType = AccessTools.TypeByName(NodeBlockType);
        var seal = nodeType == null ? null : AccessTools.DeclaredMethod(nodeType, "IsValidNonNetworkConnection", [typeof(Block), typeof(BlockFacing)]);
        if (seal?.ReturnType == typeof(bool))
            harmony.Patch(seal, postfix: new HarmonyMethod(typeof(SpringsSystem), nameof(SealPostfix)));
        else if (nodeType != null)
            logger.Warning("[seraphhorizons] Springs: ExpandedLib's BlockNetworkNode.IsValidNonNetworkConnection is not as expected, so a pipe end against a spring is an open end");

        SpringPumpBridge.Patch(harmony, logger);
    }

    // GenRivulets.tryGenRivulet / tryGenMountainSideRivers: their one ScheduleBlockUpdate(pos), right
    // after the source is set, becomes OnRivuletPlaced(blockAccessor, pos). Left as it is, with a
    // warning, when there is not exactly one.
    private static IEnumerable<CodeInstruction> RivuletTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var list = instructions.ToList();
        var schedule = AccessTools.Method(typeof(IWorldGenBlockAccessor), nameof(IWorldGenBlockAccessor.ScheduleBlockUpdate), [typeof(BlockPos)]);
        var calls = list.Where(i => i.Calls(schedule)).ToList();
        if (calls.Count != 1)
        {
            _logger?.Warning("[seraphhorizons] Springs: the game's GenRivulets.{0} calls ScheduleBlockUpdate {1} times, not once, so its rivulets stay water",
                original.Name, calls.Count);
            return list;
        }
        calls[0].opcode = OpCodes.Call;
        calls[0].operand = AccessTools.Method(typeof(SpringsSystem), nameof(OnRivuletPlaced));
        _hooked++;
        return list;
    }

    /// <summary>The generator has just set a rivulet's source at <paramref name="pos"/>: a water or
    /// rapids source becomes a spring of the rock round it, then the update is scheduled as the
    /// generator meant (the spring pours when its chunk is done).</summary>
    public static void OnRivuletPlaced(IWorldGenBlockAccessor blockAccessor, BlockPos pos)
    {
        Interlocked.Increment(ref _rivulets);
        if (_on && !_placeFailed)
        {
            try
            {
                PlaceSpring(blockAccessor, pos);
            }
            catch (Exception e)
            {
                _placeFailed = true;
                _logger?.Warning("[seraphhorizons] Springs: placing a spring for a rivulet failed, so rivulets stay water from now on: {0}", e.Message);
            }
        }
        blockAccessor.ScheduleBlockUpdate(pos);
    }

    /// <summary>Puts a spring at <paramref name="pos"/> in place of the water or rapids source
    /// there; returns whether it did.</summary>
    public static bool PlaceSpring(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var fluid = blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
        if (fluid == null || fluid.LiquidLevel != SpringCodes.SourceLevel
            || SpringCodes.KindOfFluid(fluid.Code?.Domain, fluid.Code?.Path) is not { } kind)
            return false;
        var rocks = new string?[BlockFacing.ALLFACES.Length];
        for (int i = 0; i < rocks.Length; i++)
        {
            var block = blockAccessor.GetBlock(pos.AddCopy(BlockFacing.ALLFACES[i]), BlockLayersAccess.Solid);
            rocks[i] = block?.Code == null ? null : HostRock.RockOf(block.Code.Domain, block.Code.Path, block.Variant?["rock"]);
        }
        var ids = _springIds;
        string rock = HostRock.Choose(rocks, r => ids.ContainsKey((kind, r)));
        if (!ids.TryGetValue((kind, rock), out int id))
            return false;
        blockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
        blockAccessor.SetBlock(id, pos);
        if (_entityClass != null)
            blockAccessor.SpawnBlockEntity(_entityClass, pos);
        Interlocked.Increment(ref _generated);
        return true;
    }

    /// <summary>The switch is off: the spring at <paramref name="pos"/> turns back into the source
    /// it replaced.</summary>
    public static void Revert(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlock(pos) is not BlockWaterSpring spring || spring.Source == null)
            return;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.SetBlock(spring.Source.Id, pos, BlockLayersAccess.Fluid);
        spring.Source.OnNeighbourBlockChange(world, pos, pos);
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        world.Logger.VerboseDebug("[seraphhorizons] Springs: the switch is off, so the spring at {0} is a {1} source again", pos, spring.Source.Code);
    }

    [ThreadStatic] private static BlockPos? _near;

    /// <summary>Whether the cell at <paramref name="at"/> holds a spring of <paramref name="kind"/>
    /// pouring out of its <paramref name="face"/>: untapped, and, for a rapids spring, that is the
    /// face it chose (<see cref="SpringPour.FeedsFace"/>; with no block entity yet, every
    /// face).</summary>
    public static bool PouringAt(IBlockAccessor blockAccessor, BlockPos at, SpringKind kind, int face)
    {
        if (blockAccessor.GetBlock(at, BlockLayersAccess.Solid) is not BlockWaterSpring spring || spring.Kind != kind)
            return false;
        var entity = blockAccessor.GetBlockEntity(at) as BlockEntityWaterSpring;
        return entity is not { Tapped: true } && SpringPour.FeedsFace(entity?.PourFace, face);
    }

    // BlockBehaviorFiniteSpreadingLiquid.GetMaxNeighbourLiquidLevel(ourblock, world, pos, out feeds):
    // a pouring spring of the cell's liquid above or beside it counts as a source block (a rapids
    // spring only when the cell is on the face it pours out of).
    private static void FeedPostfix(Block ourblock, IWorldAccessor world, BlockPos pos, ref bool oneNeighbourFeedsThis, ref int __result)
    {
        if (!_on || (__result >= SpringCodes.SourceLevel && oneNeighbourFeedsThis) || ourblock?.Code == null
            || world?.BlockAccessor is not { } ba || pos == null
            || SpringCodes.KindOfFluid(ourblock.Code.Domain, ourblock.Code.Path) is not { } kind)
            return;
        var near = _near ??= new BlockPos(0);
        near.Set(pos.X, pos.Y + 1, pos.Z);
        near.dimension = pos.dimension;
        bool above = PouringAt(ba, near, kind, SpringPour.Down);
        bool beside = false;
        if (!above)
            foreach (var facing in BlockFacing.HORIZONTALS)
            {
                near.Set(pos.X + facing.Normali.X, pos.Y, pos.Z + facing.Normali.Z);
                if (PouringAt(ba, near, kind, facing.Opposite.Index))
                {
                    beside = true;
                    break;
                }
            }
        (__result, oneNeighbourFeedsThis) = SpringPour.Feed(__result, oneNeighbourFeedsThis, ourblock.LiquidLevel, above, beside);
    }

    // BlockNetworkNode.IsValidNonNetworkConnection(neighbour, face): a spring seals the pipe's end.
    private static void SealPostfix(Block __0, ref bool __result)
    {
        if (!__result && __0 is BlockWaterSpring)
            __result = true;
    }
}
