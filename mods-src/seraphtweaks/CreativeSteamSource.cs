using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.SeraphTweaks;

/// <summary>
/// Pipes and Power Expanded (ppex): a creative-only block that keeps every ppex pipe connected to
/// it full of steam, the steam counterpart of the game's creative auto rotor. It is set up the way
/// the auto rotor is: right-click raises the output pressure, Ctrl+right-click lowers it,
/// Shift+right-click raises the flow rate and Ctrl+Shift+right-click lowers it, each wrapping
/// around at its end.
///
/// The steam goes in the way a pipe takes it from anything else: through the pipe's own
/// <c>IPipeNode.TryProduce</c>, which fills the pipe's network up to the output pressure (never past
/// the weakest pipe's burst pressure, as for a boiler). ppex is not referenced at build time: the
/// two members this needs are found by name, and if either is missing the block is not loaded at
/// all and a warning says so.
/// </summary>
public static class CreativeSteamSource
{
    public const string ModId = "ppex";
    public const string BlockCode = "creativesteamsource";
    public const string ClassName = "seraphtweaks.CreativeSteamSource";
    public static readonly AssetLocation BlockAsset = new("seraphtweaks", "blocktypes/" + BlockCode + ".json");

    /// <summary>ppex's interface on every pipe block entity: <c>TryProduce(volume, temperature,
    /// gasType, maxOutputPressure, bypassLeakCap)</c> adds gas to the pipe's network.</summary>
    public const string PipeNodeType = "PipesAndPowerExpanded.BlockNetworkPipe.IPipeNode";

    /// <summary>The pipe blocks' base class (in ppex's library, exlib): <c>HasConnectorAt(face)</c>
    /// says whether the pipe opens towards a side, as ppex's boilers check before they push steam.</summary>
    public const string NetworkNodeType = "ExpandedLib.Blocks.Networks.BlockNetworkNode";

    // ppex's pressure is a network's gas volume over its capacity (shown in atm), so a setting of
    // 3 atm keeps the connected pipes at three times their volume. Pipes burst at 5 (iron) and
    // 10 (steel) by ppex's defaults; the auto rotor's settings run 1-10 as well.
    public const int MinPressure = 1, MaxPressure = 10, DefaultPressure = 3;
    public const int MaxFlowSetting = 10, DefaultFlowSetting = 3;
    public const float LitresPerFlowSetting = 10f;

    private static Type? _pipeNode;
    private static Type? _networkNode;
    private static MethodInfo? _tryProduce;
    private static MethodInfo? _hasConnectorAt;

    /// <summary>Whether <see cref="Bind"/> found what the block needs.</summary>
    public static bool Bound => _tryProduce != null && _hasConnectorAt != null;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>The block and block entity classes. Registered on both sides whatever the setting:
    /// the server decides whether the block exists, and a client must know the classes then.</summary>
    public static void RegisterClasses(ICoreAPI api)
    {
        api.RegisterBlockClass(ClassName, typeof(BlockCreativeSteamSource));
        api.RegisterBlockEntityClass(ClassName, typeof(BlockEntityCreativeSteamSource));
    }

    /// <summary>Finds ppex's members by name. Returns whether all of them were found.</summary>
    public static bool Bind(ILogger logger)
    {
        _pipeNode = AccessTools.TypeByName(PipeNodeType);
        _tryProduce = _pipeNode?.GetMethod("TryProduce",
            [typeof(float), typeof(float), typeof(string), typeof(float), typeof(bool)]);
        _networkNode = AccessTools.TypeByName(NetworkNodeType);
        _hasConnectorAt = _networkNode?.GetMethod("HasConnectorAt", [typeof(BlockFacing)]);
        if (_pipeNode is { IsInterface: true } && _tryProduce?.ReturnType == typeof(bool)
            && _networkNode != null && typeof(Block).IsAssignableFrom(_networkNode)
            && _hasConnectorAt?.ReturnType == typeof(bool))
            return true;

        _tryProduce = null;
        _hasConnectorAt = null;
        logger.Warning($"[seraphtweaks] {PipeNodeType}.TryProduce(float, float, string, float, bool) or "
                       + $"{NetworkNodeType}.HasConnectorAt(BlockFacing) is not as expected; ppex changed, so the "
                       + "creative steam source is not loaded");
        return false;
    }

    /// <summary>Leaves the block out of the game: marks its blocktype disabled before the game
    /// loads blocktypes (this runs in AssetsLoaded, before the game's own loader at 0.2).</summary>
    public static void Disable(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(BlockAsset);
        if (asset == null)
            return;
        var json = JObject.Parse(asset.ToText());
        json["enabled"] = false;
        asset.Data = Encoding.UTF8.GetBytes(json.ToString());
    }

    /// <summary>Pushes up to <paramref name="litres"/> of steam at up to <paramref name="pressure"/>
    /// into the pipes that open towards <paramref name="pos"/>, shared evenly between them.</summary>
    public static void Feed(IBlockAccessor blocks, BlockPos pos, float litres, float pressure)
    {
        if (!Bound || litres <= 0f)
            return;
        Span<int> faces = stackalloc int[6];
        int count = 0;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var at = pos.AddCopy(face);
            var block = blocks.GetBlock(at);
            if (!_networkNode!.IsInstanceOfType(block)
                || !(bool)_hasConnectorAt!.Invoke(block, [face.Opposite])!
                || !_pipeNode!.IsInstanceOfType(blocks.GetBlockEntity(at)))
                continue;
            faces[count++] = face.Index;
        }
        if (count == 0)
            return;

        float share = litres / count;
        // As a ppex boiler at this pressure would make it.
        float temperature = 100f * MathF.Pow(pressure + 1f, 0.25f);
        for (int i = 0; i < count; i++)
        {
            var pipe = blocks.GetBlockEntity(pos.AddCopy(BlockFacing.ALLFACES[faces[i]]));
            _tryProduce!.Invoke(pipe, [share, temperature, "Steam", pressure, false]);
        }
    }
}

/// <summary>The steam source's block: the auto rotor's right-click controls and their help.</summary>
public class BlockCreativeSteamSource : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityCreativeSteamSource source)
            return source.OnInteract(byPlayer);
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer) =>
    [
        new() { ActionLangCode = "seraphtweaks:blockhelp-creativesteamsource-pressure-up", MouseButton = EnumMouseButton.Right },
        new() { ActionLangCode = "seraphtweaks:blockhelp-creativesteamsource-pressure-down", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" },
        new() { ActionLangCode = "seraphtweaks:blockhelp-creativesteamsource-flow-up", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" },
        new() { ActionLangCode = "seraphtweaks:blockhelp-creativesteamsource-flow-down", MouseButton = EnumMouseButton.Right, HotKeyCodes = ["ctrl", "shift"] },
    ];
}

/// <summary>The steam source's settings and its feed, every <see cref="TickMs"/> on the server.</summary>
public class BlockEntityCreativeSteamSource : BlockEntity
{
    public const int TickMs = 200;

    /// <summary>Output pressure in atm, <see cref="CreativeSteamSource.MinPressure"/> to
    /// <see cref="CreativeSteamSource.MaxPressure"/>.</summary>
    public int PressureSetting { get; private set; } = CreativeSteamSource.DefaultPressure;

    /// <summary>Flow rate in steps of <see cref="CreativeSteamSource.LitresPerFlowSetting"/> L/s,
    /// 0 (off) to <see cref="CreativeSteamSource.MaxFlowSetting"/>.</summary>
    public int FlowSetting { get; private set; } = CreativeSteamSource.DefaultFlowSetting;

    public float Pressure => PressureSetting;
    public float LitresPerSecond => FlowSetting * CreativeSteamSource.LitresPerFlowSetting;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnServerTick, TickMs);
    }

    private void OnServerTick(float dt) =>
        CreativeSteamSource.Feed(Api.World.BlockAccessor, Pos, LitresPerSecond * dt, Pressure);

    /// <summary>As the auto rotor: right-click steps the pressure, with Shift the flow rate, and
    /// Ctrl steps down instead of up. Runs on both sides, as the auto rotor's does.</summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        int step = controls.CtrlKey ? -1 : 1;
        if (controls.ShiftKey)
            FlowSetting = GameMath.Mod(FlowSetting + step, CreativeSteamSource.MaxFlowSetting + 1);
        else
            PressureSetting = CreativeSteamSource.MinPressure + GameMath.Mod(
                PressureSetting - CreativeSteamSource.MinPressure + step,
                CreativeSteamSource.MaxPressure - CreativeSteamSource.MinPressure + 1);
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("sounds/toggleswitch"), Pos, -0.2, byPlayer, randomizePitch: false, 16f);
        return true;
    }

    /// <summary>Sets both settings, clamped to their ranges (for tests and commands).</summary>
    public void Configure(int pressure, int flow)
    {
        PressureSetting = Math.Clamp(pressure, CreativeSteamSource.MinPressure, CreativeSteamSource.MaxPressure);
        FlowSetting = Math.Clamp(flow, 0, CreativeSteamSource.MaxFlowSetting);
        MarkDirty(true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("pressure", PressureSetting);
        tree.SetInt("flow", FlowSetting);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PressureSetting = Math.Clamp(tree.GetInt("pressure", CreativeSteamSource.DefaultPressure),
            CreativeSteamSource.MinPressure, CreativeSteamSource.MaxPressure);
        FlowSetting = Math.Clamp(tree.GetInt("flow", CreativeSteamSource.DefaultFlowSetting),
            0, CreativeSteamSource.MaxFlowSetting);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("seraphtweaks:creativesteamsource-info-pressure", PressureSetting));
        dsc.AppendLine(Lang.Get("seraphtweaks:creativesteamsource-info-flow", LitresPerSecond));
    }
}
