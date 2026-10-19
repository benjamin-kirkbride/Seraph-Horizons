using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// What the ore machines share besides the machine base (#711, #694; <see cref="MachinesSystem"/>): their
/// settings (OreMachinesSettings in ModConfig/seraphhorizons.json: <see cref="OreMachinesConfig"/>) and their
/// rigs (<c>config/{machine}-rig.json</c>, <see cref="OreMachineRig"/>).
/// </summary>
public class OreMachinesSystem : ModSystem
{
    public const string Domain = "seraphhorizons";

    private ICoreAPI? _api;
    private OreMachinesConfig? _config;

    public static OreMachinesSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<OreMachinesSystem>();

    /// <summary>This side's settings, sanitised (each fix logged once).</summary>
    public OreMachinesConfig Config => _config ??= LoadConfig(_api!);

    public override void Start(ICoreAPI api) => _api = api;

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

    private static OreMachinesConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).OreMachinesSettings ?? new OreMachinesConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning("[seraphhorizons] OreMachinesSettings: " + fix);
        return config;
    }
}
