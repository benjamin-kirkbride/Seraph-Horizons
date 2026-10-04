using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod;

/// <summary>
/// Immersive Woodworking: its chopper and sawmill exist in the creative inventory only as empty
/// frames, which take every part by hand. This adds a second creative entry of each frame that
/// places the machine assembled, with a steel head or blade kit: the frame's own stack carrying
/// <see cref="MetalAttribute"/>.
///
/// Nothing of Immersive Woodworking is patched or referenced. Its two frame blocktypes get the
/// creative stack and two behaviors of this mod (<see cref="AddToBlocktypes"/>): a block behavior
/// that names the stack after the finished machine, and a block entity behavior that, when the
/// frame is placed from such a stack, writes the installed parts into the block entity through
/// its own tree attributes, the way a saved one loads. A block entity that no longer saves those
/// attributes is left an empty frame, with a warning.
///
/// The chopper's bed gets a wood (<see cref="BedWood"/>), as one installed by hand has: without
/// one it would come back from a broken frame as a chopping block of no wood, which places as a
/// bare block. With <c>UnifiedWoodworking</c> on, that bed comes back an advanced splitting block
/// like every chopper's bed (<see cref="Woodworking.ChopperBed"/>).
/// </summary>
public static class AssembledMachines
{
    public const string ModId = "immersivewoodworking";
    public const string BlockBehaviorName = "seraphhorizons.AssembledMachineName";
    public const string EntityBehaviorName = "seraphhorizons.AssembledMachine";

    /// <summary>On a frame's stack: the metal of the head or blade kit the placed machine gets.</summary>
    public const string MetalAttribute = "assembledWith";
    public const string DefaultMetal = "steel";

    /// <summary>The wood of the assembled chopper's bed, as Immersive Woodworking saves it.</summary>
    public const string BedWood = "oak";
    public const string BedWoodDomain = "game";

    /// <param name="Asset">The frame's blocktype.</param>
    /// <param name="FrameCode">The frame variant in the creative inventory.</param>
    /// <param name="NameKey">Immersive Woodworking's name for the finished machine.</param>
    /// <param name="Flags">The block entity's saved flags, one per installed part.</param>
    /// <param name="ToolKey">The block entity's saved head or blade kit stack.</param>
    /// <param name="ToolCode">That item's code, <c>{metal}</c> for the metal.</param>
    /// <param name="Strings">The block entity's saved strings the assembled machine gets.</param>
    public record Machine(AssetLocation Asset, string FrameCode, string NameKey, string[] Flags, string ToolKey, string ToolCode,
        Dictionary<string, string>? Strings = null);

    /// <summary>The sawmill's flywheel is an optional part and is left out.</summary>
    public static readonly Machine[] Machines =
    [
        new(new(ModId, "blocktypes/chopper/frame.json"), "chopper-frame-north", ModId + ":block-chopper",
            ["hasDrive", "hasArm", "hasBed"], "headStack", ModId + ":chopperhead-{metal}",
            new() { ["bedWood"] = BedWood, ["bedWoodDomain"] = BedWoodDomain }),
        new(new(ModId, "blocktypes/sawmill/frame.json"), "sawmill-frame-north", ModId + ":block-sawmill",
            ["hasSash", "hasCrankshaft", "hasLevers", "hasCarriage"], "bladeStack", ModId + ":sawmillblade-{metal}"),
    ];

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>The behavior classes. Registered on both sides whatever the setting: the server
    /// decides whether the frames carry them, and a client must know the classes then.</summary>
    public static void RegisterClasses(ICoreAPI api)
    {
        api.RegisterBlockBehaviorClass(BlockBehaviorName, typeof(BlockBehaviorAssembledMachineName));
        api.RegisterBlockEntityBehaviorClass(EntityBehaviorName, typeof(BEBehaviorAssembledMachine));
    }

    /// <summary>Adds the assembled creative stack and the two behaviors to each frame's blocktype.
    /// Runs on the server in AssetsLoaded, after the patch loader (0.05) and before the blocktypes
    /// are read (0.2).</summary>
    public static void AddToBlocktypes(ICoreAPI api)
    {
        foreach (var machine in Machines)
        {
            var asset = api.Assets.TryGet(machine.Asset);
            JObject? json = asset == null ? null : JObject.Parse(asset.ToText());
            if (json == null || !AddTo(json, machine))
            {
                api.Logger.Warning($"[seraphhorizons] {machine.Asset} is missing or not as expected; Immersive "
                                   + "Woodworking changed, so that machine has no assembled creative entry");
                continue;
            }
            asset!.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    /// <summary>Returns false, with <paramref name="json"/> possibly half edited, when the
    /// blocktype is not the frame this expects: one with a creative tab of its own.</summary>
    public static bool AddTo(JObject json, Machine machine)
    {
        // The game reads blocktype keys case-insensitively.
        JToken? Get(string key) => json.GetValue(key, StringComparison.OrdinalIgnoreCase);

        if (Get("creativeinventory") is not JObject tabs || tabs.Count == 0
            || Get("creativeinventoryStacksByType") != null || Get("creativeinventoryStacks") != null
            || Get("entityClass") == null)
            return false;

        if (Get("behaviors") is not JArray behaviors)
            json["behaviors"] = behaviors = [];
        behaviors.Add(new JObject
        {
            ["name"] = BlockBehaviorName,
            ["properties"] = new JObject { ["nameKey"] = machine.NameKey, ["toolCode"] = machine.ToolCode },
        });

        if (Get("entityBehaviors") is not JArray entityBehaviors)
            json["entityBehaviors"] = entityBehaviors = [];
        entityBehaviors.Add(new JObject
        {
            ["name"] = EntityBehaviorName,
            ["properties"] = new JObject
            {
                ["flags"] = new JArray(machine.Flags),
                ["toolKey"] = machine.ToolKey,
                ["toolCode"] = machine.ToolCode,
                ["strings"] = JObject.FromObject(machine.Strings ?? []),
            },
        });

        json["creativeinventoryStacksByType"] = new JObject
        {
            ["*-north"] = new JArray(new JObject
            {
                ["tabs"] = new JArray(tabs.Properties().Select(tab => tab.Name)),
                ["stacks"] = new JArray(new JObject
                {
                    ["type"] = "block",
                    ["code"] = machine.FrameCode,
                    ["attributes"] = new JObject { [MetalAttribute] = DefaultMetal },
                }),
            }),
        };

        // The handbook lists a block's creative stacks in place of the block itself; this keeps
        // the frame's page, which Immersive Woodworking's text links to, and adds none.
        if (Get("attributes") is not JObject attributes)
            json["attributes"] = attributes = [];
        if (attributes["handbook"] is not JObject handbook)
            attributes["handbook"] = handbook = [];
        handbook["ignoreCreativeInvStacks"] = true;
        return true;
    }

    /// <summary>The head or blade kit an assembled frame's stack stands for, or null for a plain
    /// frame or a metal the item does not come in.</summary>
    public static Item? Tool(IWorldAccessor world, ItemStack? frame, string? toolCode)
    {
        string? metal = frame?.Attributes?.GetString(MetalAttribute);
        return metal == null || toolCode == null ? null : world.GetItem(new AssetLocation(toolCode.Replace("{metal}", metal)));
    }
}

/// <summary>Names an assembled frame's stack after the finished machine, and says in its info
/// which head or blade kit it has.</summary>
public class BlockBehaviorAssembledMachineName(Block block) : BlockBehavior(block)
{
    private string? _nameKey;
    private string? _toolCode;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);
        _nameKey = properties["nameKey"].AsString();
        _toolCode = properties["toolCode"].AsString();
    }

    public override void GetHeldItemName(StringBuilder sb, ItemStack itemStack)
    {
        if (_nameKey == null || itemStack?.Attributes?.HasAttribute(AssembledMachines.MetalAttribute) != true)
            return;
        sb.Clear();
        sb.Append(Lang.Get(_nameKey));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (AssembledMachines.Tool(world, inSlot.Itemstack, _toolCode) is { } tool)
            dsc.AppendLine(Lang.Get("seraphhorizons:assembledmachine-info", new ItemStack(tool).GetName()));
    }
}

/// <summary>Assembles a frame placed from an assembled stack, on the server: every part flag set,
/// the machine's strings (the chopper's bed wood) and a new head or blade kit of the stack's metal,
/// written through the block entity's own tree attributes.</summary>
public class BEBehaviorAssembledMachine(BlockEntity blockentity) : BlockEntityBehavior(blockentity)
{
    private string[] _flags = [];
    private string? _toolKey;
    private string? _toolCode;
    private Dictionary<string, string> _strings = [];

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        _flags = properties["flags"].AsArray<string>([])!;
        _strings = properties["strings"].AsObject<Dictionary<string, string>?>(null) ?? [];
        _toolKey = properties["toolKey"].AsString();
        _toolCode = properties["toolCode"].AsString();
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        if (Api.Side != EnumAppSide.Server || _toolKey == null
            || byItemStack?.Attributes?.HasAttribute(AssembledMachines.MetalAttribute) != true)
            return;
        var tool = AssembledMachines.Tool(Api.World, byItemStack, _toolCode);
        var tree = new TreeAttribute();
        Blockentity.ToTreeAttributes(tree);
        if (tool == null || _flags.Length == 0 || !_flags.All(tree.HasAttribute))
        {
            Api.Logger.Warning($"[seraphhorizons] {Block.Code} does not save its parts as expected, or has no "
                               + $"{_toolCode} for this stack; it is placed as an empty frame");
            return;
        }
        foreach (string flag in _flags)
            tree.SetBool(flag, true);
        foreach (var (key, value) in _strings)
            tree.SetString(key, value);
        tree.SetItemstack(_toolKey, new ItemStack(tool));
        Blockentity.FromTreeAttributes(tree, Api.World);
        Blockentity.MarkDirty(true);
    }
}
