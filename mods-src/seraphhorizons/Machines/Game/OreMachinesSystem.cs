using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// What the ore machines share (#711, #694): the shared mechanical power consumer's class
/// (<see cref="BEBehaviorMachineMP"/>), their settings (OreMachinesSettings in
/// ModConfig/seraphhorizons.json: <see cref="OreMachinesConfig"/>), and their build files
/// (<c>assets/seraphhorizons/config/builds/{machine}.json</c>, <see cref="MachineBuildJson"/>), each
/// checked against its rig (<c>config/{machine}-rig.json</c>) as it loads. A machine reads its build
/// with <see cref="Build"/> and keeps a <see cref="MachineAssembly"/>.
/// </summary>
public class OreMachinesSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string BuildsFolder = "config/builds/";

    private ICoreAPI? _api;
    private OreMachinesConfig? _config;
    private Dictionary<string, MachineBuild>? _builds;

    public static OreMachinesSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<OreMachinesSystem>();

    /// <summary>This side's settings, sanitised (each fix logged once).</summary>
    public OreMachinesConfig Config => _config ??= LoadConfig(_api!);

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityBehaviorClass(BEBehaviorMachineMP.ClassName, typeof(BEBehaviorMachineMP));
    }

    public override void AssetsLoaded(ICoreAPI api) => _builds = LoadBuilds(api);

    /// <summary>The build of <paramref name="machine"/>, or null when it has no build file or the file
    /// is broken (logged as it loads).</summary>
    public MachineBuild? Build(string machine)
    {
        _builds ??= _api == null ? [] : LoadBuilds(_api);
        return _builds.GetValueOrDefault(machine);
    }

    /// <summary>Every machine with a build file, by id.</summary>
    public IReadOnlyDictionary<string, MachineBuild> Builds => _builds ??= _api == null ? [] : LoadBuilds(_api);

    private static OreMachinesConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).OreMachinesSettings ?? new OreMachinesConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning("[seraphhorizons] OreMachinesSettings: " + fix);
        return config;
    }

    private static Dictionary<string, MachineBuild> LoadBuilds(ICoreAPI api)
    {
        var builds = new Dictionary<string, MachineBuild>(StringComparer.Ordinal);
        foreach (var asset in api.Assets.GetMany(BuildsFolder, Domain, loadAsset: true))
        {
            string name = asset.Location.Path;
            try
            {
                var build = MachineBuildJson.Parse(asset.ToText());
                if (!name.EndsWith("/" + build.Machine + ".json", StringComparison.Ordinal))
                    throw new FormatException($"its machine is {build.Machine}, not its file name");
                if (api.Assets.TryGet(new AssetLocation(Domain, $"config/{build.Machine}-rig.json")) is { } rig)
                {
                    using var doc = RigJson.Parse(rig.ToText());
                    foreach (var problem in MachineBuildJson.CheckAgainstRig(build, doc.RootElement))
                        api.Logger.Warning("[seraphhorizons] {0}: {1}", name, problem);
                }
                builds[build.Machine] = build;
            }
            catch (FormatException e)
            {
                api.Logger.Error("[seraphhorizons] {0} is broken, so its machine cannot be built: {1}", name, e.Message);
            }
        }
        return builds;
    }
}
