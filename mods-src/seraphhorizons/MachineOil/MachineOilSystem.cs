using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.MachineOil;

/// <summary>
/// Machine oil (<c>MachineOil</c>): the heavy mechanical power machines (the game's helve hammer and
/// pulverizer, Immersive Woodworking's sawmill and chopper, this mod's bucking mill and rosser)
/// have an oil tank, start dry, and while dry load their shaft <c>DryResistanceMultiplier</c> times
/// as hard. Oil is poured in by right-clicking any cell of one holding a liquid container of a
/// listed oil, or a lump of tallow; jobs drain it, idle turning does not. Settings in
/// <c>MachineOilSettings</c>. The foreign machines are patched (<see cref="ForeignMachines"/>);
/// the two of this mod's own read <see cref="On"/> and <see cref="Config"/> themselves.
/// With the switch off nothing is patched, the mill and the rosser keep no tank, the handbook page
/// is hidden, and every machine turns as it did before.
/// </summary>
public class MachineOilSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.machineoil";
    public const string GuidePageCode = "seraphhorizons-machineoil";
    public const string GuideTitleKey = "seraphhorizons:machineoil-title";

    private ICoreAPI? _api;
    private MachineOilConfig? _config;
    private OilCodes? _liquids;
    private Harmony? _harmony;
    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _hidePage;

    public static MachineOilSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<MachineOilSystem>();

    /// <summary>The switch on this side. For the bucking mill and the rosser the server's decides:
    /// a client shows the tank the server sent, if any.</summary>
    public bool On => _api != null && SeraphHorizonsSystem.ConfigFor(_api).MachineOil;

    /// <summary>This side's settings, sanitised.</summary>
    public MachineOilConfig Config => _config ??= LoadConfig(_api!);

    public OilCodes Liquids => _liquids ??= new OilCodes(Config.OilLiquids);

    public override void Start(ICoreAPI api)
    {
        _api = api;
        // Once per process: in singleplayer the other side's system may have bound and patched
        // already, and its server is running on what it bound.
        if (!On || Harmony.HasAnyPatches(HarmonyId))
            return;
        ForeignMachines.Bind(api, Config, Liquids);
        _harmony = new Harmony(HarmonyId);
        ForeignMachines.Patch(_harmony);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (On)
            return;
        // The recipe export leaves the page out too (seraphhorizons' woodworking guide lists its
        // hidden pages under the same key in Start, before this).
        var hidden = api.ObjectCache.TryGetValue(WoodworkingGuide.HiddenGuidesKey, out var listed)
                     && listed is IEnumerable<(string, string)> pages
            ? pages.ToList()
            : [];
        hidden.Add((GuidePageCode, GuideTitleKey));
        api.ObjectCache[WoodworkingGuide.HiddenGuidesKey] = hidden;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!On)
        {
            if (api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is { } handbook)
            {
                _handbook = handbook;
                _hidePage = pages => pages.RemoveAll(p => p.PageCode == GuidePageCode);
                handbook.OnInitCustomPages += _hidePage;
            }
            return;
        }
        api.Event.RegisterGameTickListener(_ => SmokeTick(api), 400);
    }

    // The game's and Immersive Woodworking's machines; the mill and the rosser smoke from their own tick.
    private static void SmokeTick(ICoreClientAPI capi)
    {
        foreach (var (machine, state) in ForeignMachines.ClientStates(capi.World))
            if (state.Dry && ForeignMachines.Turning(machine))
                Oil.Smoke(capi, ForeignMachines.SmokeAt(machine));
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
            ForeignMachines.Unbind();
        }
    }

    private static MachineOilConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).MachineOilSettings ?? new MachineOilConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Machine oil: ModConfig/{SeraphHorizonsSystem.ConfigFile}, MachineOilSettings: {fix}");
        return config;
    }
}
