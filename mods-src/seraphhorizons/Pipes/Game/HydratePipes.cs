using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Pipes;

/// <summary>
/// Hydrate or Diedrate's pipes go (the <c>UnifiedPipes</c> switch; README "Unified pipes"): its
/// copper and lead pipes (<c>hydrateordiedrate:pipe-*</c>) and shut-off valves
/// (<c>shutoffvalve-*</c>), which only ever joined its hand pump to a wellspring. Pipes and Power
/// Expanded's pipes do that now (<see cref="HandPumpBridge"/>).
/// <list type="bullet">
/// <item>A JSON patch, <c>patches/pipes-hydrateordiedrate.json</c>: no recipe for the pipe, the
/// pipe section or the valve; the three left out of the creative inventory and the handbook; the
/// hand pump's recipe takes ppex copper or lead straight pipe; the hand pump's handbook page
/// says it works on ppex pipes; and ppex's pipes and valves get <c>"replaceable": 500</c>, as
/// Hydrate's pipe had, so a pipe run down a well shaft leaves the shaft open to the spring's count
/// (<c>WellBlockUtils.SolidAllows</c>). With the switch off, or without Hydrate or Diedrate,
/// <see cref="DisablePatches"/> empties the file in <c>Start</c>, before the patch loader.</item>
/// <item>Placed pipes and valves are deleted as they load, with nothing given back (the pack's
/// call): a Harmony postfix on each block entity's <c>Initialize</c> queues the removal for the
/// next server tick, as <c>OldTrunkBlocks</c> does for trunk blocks. The block types stay
/// registered, so the removal finds them. A pipe placed by hand from a stack left over goes the
/// same way.</item>
/// </list>
/// Hydrate's licence forbids redistributing it: its types are found by name, nothing is
/// referenced at build time, and the patch only names its asset paths.
/// </summary>
public static class HydratePipes
{
    public const string ModId = "hydrateordiedrate";
    public const string PipeEntityType = "HydrateOrDiedrate.Piping.Pipe.BlockEntityPipe";
    public const string ValveEntityType = "HydrateOrDiedrate.Piping.ShutoffValve.BlockEntityShutoffValve";

    /// <summary>The block code paths (domain <c>hydrateordiedrate</c>) removed from the world.</summary>
    public static readonly string[] RemovedPrefixes = ["pipe-", "shutoffvalve-"];

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/pipes-hydrateordiedrate.json");

    private static ICoreServerAPI? _api;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Whether a block code is one of Hydrate or Diedrate's pipes or valves.</summary>
    public static bool IsRemoved(AssetLocation? code) =>
        code is { Domain: ModId } && RemovedPrefixes.Any(p => code.Path.StartsWith(p, StringComparison.Ordinal));

    // Server side: the removal and the bridge's server patches, under their own id.
    public const string HarmonyId = "seraphhorizons.hydratepipes";
    private static Harmony? _harmony;

    /// <summary>Both sides, in <c>Start</c> (types of other mods are loaded by then): with the
    /// switch off or without Hydrate or Diedrate, empties the patch file (on the server, before the
    /// patch loader); with it on, patches the hand pump's search (<see cref="HandPumpBridge.Patch"/>)
    /// and, on the server, the removal and the bridge's server patches. The handle it returns
    /// undoes this side's part when disposed (the mod system's <c>Dispose</c>).</summary>
    public static IDisposable? Start(ICoreAPI api, bool on)
    {
        if (!(on && Applies(api)))
        {
            if (api.Side == EnumAppSide.Server)
                DisablePatches(api);
            return null;
        }
        var side = new Side(HandPumpBridge.Patch(api.Logger), api.Side == EnumAppSide.Server);
        if (api is not ICoreServerAPI sapi)
            return side;
        _api = sapi;
        _harmony = new Harmony(HarmonyId);
        int patched = 0;
        foreach (var name in new[] { PipeEntityType, ValveEntityType })
        {
            var type = AccessTools.TypeByName(name);
            // Declared on the type itself: the base BlockEntity.Initialize would catch every block entity.
            var initialize = type == null ? null : AccessTools.DeclaredMethod(type, nameof(BlockEntity.Initialize), [typeof(ICoreAPI)]);
            if (initialize == null)
            {
                api.Logger.Warning("[seraphhorizons] Unified pipes: Hydrate or Diedrate's {0}.Initialize is not there, so its placed blocks stay", name);
                continue;
            }
            _harmony.Patch(initialize, postfix: new HarmonyMethod(typeof(HydratePipes), nameof(InitializePostfix)));
            patched++;
        }
        HandPumpBridge.PatchServer(_harmony, api);
        api.Logger.Notification("[seraphhorizons] Unified pipes: Hydrate or Diedrate's pipes and valves are removed as they load ({0} of 2 block entities patched)", patched);
        return side;
    }

    private sealed class Side(bool bridge, bool server) : IDisposable
    {
        private bool _bridge = bridge;

        public void Dispose()
        {
            if (_bridge)
                HandPumpBridge.Unpatch();
            _bridge = false;
            if (!server)
                return;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            HandPumpBridge.UnbindServer();
            _api = null;
        }
    }

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }

    private static void InitializePostfix(BlockEntity __instance, ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || _api == null)
            return;
        var pos = __instance.Pos.Copy();
        _api.Event.RegisterCallback(_ => Remove(api.World, pos), 0);
    }

    /// <summary>Removes the Hydrate or Diedrate pipe or valve at <paramref name="pos"/>, if it is
    /// still there; nothing drops (a pipe's disguise block goes with it).</summary>
    public static bool Remove(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        if (!IsRemoved(block?.Code))
            return false;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        world.Logger.Notification("[seraphhorizons] Unified pipes: removed Hydrate or Diedrate's {0} at {1}", block!.Code, pos);
        return true;
    }
}
