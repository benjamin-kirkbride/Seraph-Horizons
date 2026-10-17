using System.Text;
using Newtonsoft.Json.Linq;
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
        api.RegisterBlockClass(BlockOreMachine.ClassName, typeof(BlockOreMachine));
        api.RegisterBlockClass(BlockOreMachineCell.ClassName, typeof(BlockOreMachineCell));
        api.RegisterBlockClass(BlockOreMachineCellPower.ClassName, typeof(BlockOreMachineCellPower));
        api.RegisterBlockEntityClass(BEOreMachineCell.ClassName, typeof(BEOreMachineCell));
    }

    private readonly Dictionary<string, OreMachineRig?> _rigs = new(StringComparer.Ordinal);

    /// <summary>The rig of <paramref name="machine"/> (<c>config/{machine}-rig.json</c>), parsed once with
    /// the machine's own part requires besides its stages'; null when it is missing or broken (logged
    /// once: the machine then cannot be placed).</summary>
    public OreMachineRig? Rig(string machine, IEnumerable<string>? extraRequires = null)
    {
        lock (_rigs)
        {
            if (_rigs.TryGetValue(machine, out var rig))
                return rig;
            rig = null;
            var loc = new AssetLocation(Domain, $"config/{machine}-rig.json");
            if (_api?.Assets.TryGet(loc) is not { } asset)
                _api?.Logger.Error("[seraphhorizons] {0} is missing; the {1} cannot be placed", loc, machine);
            else
                try
                {
                    rig = OreMachineRig.Parse(asset.ToText(), extraRequires);
                }
                catch (FormatException e)
                {
                    _api!.Logger.Error("[seraphhorizons] {0} is broken, so the {1} cannot be placed: {2}", loc, machine, e.Message);
                }
            _rigs[machine] = rig;
            return rig;
        }
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

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>Leaves a machine out of the game with its switch off (server side, from the machine's
    /// system's <c>AssetsLoaded</c>, as the draw bench's): marks its block and item type files and every
    /// recipe in its recipe files disabled before the game loads them.</summary>
    public static void Disable(ICoreAPI api, IEnumerable<AssetLocation> types, IEnumerable<AssetLocation> recipes)
    {
        foreach (var location in types)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), IgnoreComments);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
        foreach (var location in recipes)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var token = JToken.Parse(asset.ToText(), IgnoreComments);
            foreach (var recipe in token is JArray array ? array.OfType<JObject>() : token is JObject one ? [one] : [])
                recipe["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(token.ToString());
        }
    }

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
