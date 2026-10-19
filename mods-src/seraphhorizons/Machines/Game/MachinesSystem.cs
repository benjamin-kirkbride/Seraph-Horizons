using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// What every machine on the shared base shares (<see cref="BEMachine"/>; docs/modeling/machine-base.md):
/// the shared block, cell and power classes (<see cref="BlockMachine"/>, <see cref="BlockMachineCell"/>,
/// <see cref="BlockMachineCellPower"/>, <see cref="BEMachineCell"/>, the mechanical power consumer
/// <see cref="BEBehaviorMachineMP"/>), the build files (<c>assets/seraphhorizons/config/builds/{machine}.json</c>,
/// <see cref="MachineBuildJson"/>), each checked against its rig (<c>config/{machine}-rig.json</c>) as it
/// loads, and the rigs of machines whose rig is not the ore machines' dialect (<see cref="RegisterRig"/>). A
/// machine reads its build with <see cref="Build"/> and keeps a <see cref="MachineAssembly"/>.
/// </summary>
public class MachinesSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string BuildsFolder = "config/builds/";

    private ICoreAPI? _api;
    private Dictionary<string, MachineBuild>? _builds;
    private readonly Dictionary<string, MachineBuild> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IMachineRig?>> _rigs = new(StringComparer.Ordinal);

    public static MachinesSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<MachinesSystem>();

    // Before the machines' own systems (0.1 by default), which register their rigs in Start.
    public override double ExecuteOrder() => 0.05;

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityBehaviorClass(BEBehaviorMachineMP.ClassName, typeof(BEBehaviorMachineMP));
        api.RegisterBlockClass(BlockMachine.ClassName, typeof(BlockMachine));
        api.RegisterBlockClass(BlockMachineCell.ClassName, typeof(BlockMachineCell));
        api.RegisterBlockClass(BlockMachineCellPower.ClassName, typeof(BlockMachineCellPower));
        api.RegisterBlockEntityClass(BEMachineCell.ClassName, typeof(BEMachineCell));
    }

    /// <summary>Names the rig of a machine whose rig file is its own dialect (the press brake's, the draw
    /// bench's), for <see cref="BlockMachine.Rig"/>; a machine not registered is read as an ore machine's
    /// (<see cref="OreMachinesSystem.Rig"/>).</summary>
    public void RegisterRig(string machine, Func<IMachineRig?> rig) => _rigs[machine] = rig;

    /// <summary>The registered rig of <paramref name="machine"/>; null when it has none registered.</summary>
    public IMachineRig? RegisteredRig(string machine, out bool registered)
    {
        registered = _rigs.TryGetValue(machine, out var rig);
        return registered ? rig!() : null;
    }

    public override void AssetsLoaded(ICoreAPI api) => _builds = LoadBuilds(api);

    /// <summary>The build of <paramref name="machine"/>, or null when it has no build file or the file is
    /// broken (logged as it loads). Once the world's items are known, as the game being played has it
    /// (<see cref="MachineBuild.Resolve"/>: a material none of whose items exist left out).</summary>
    public MachineBuild? Build(string machine)
    {
        _builds ??= _api == null ? [] : LoadBuilds(_api);
        if (_builds.GetValueOrDefault(machine) is not { } build)
            return null;
        lock (_resolved)
        {
            if (_resolved.TryGetValue(machine, out var resolved))
                return resolved;
            if (_api?.World is not { } world || world.Items == null || world.Items.Count == 0)
                return build;
            resolved = build.Resolve(code =>
                !code.Contains('*') && ((CollectibleObject?)world.GetItem(new AssetLocation(code)) ?? world.GetBlock(new AssetLocation(code)))
                    is { Id: > 0, IsMissing: false });
            _resolved[machine] = resolved;
            return resolved;
        }
    }

    /// <summary>Every machine with a build file, by id.</summary>
    public IReadOnlyDictionary<string, MachineBuild> Builds => _builds ??= _api == null ? [] : LoadBuilds(_api);

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>Leaves a machine out of the game with its switch off (server side, from the machine's
    /// system's <c>AssetsLoaded</c>): marks its block and item type files and every recipe in its recipe
    /// files disabled before the game loads them.</summary>
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
