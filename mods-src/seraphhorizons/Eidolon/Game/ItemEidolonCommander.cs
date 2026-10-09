using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon command tool, <c>seraphhorizons:eidoloncommander</c> (#675; README "Eidolon", the
/// command tool). Right-click an eidolon its holder may command to bind the tool to it (sneak to
/// unbind); the tool keeps the bound eidolons' ids. The mode wheel (<see cref="EidolonCommandModes"/>)
/// picks an order; right-click anywhere else gives it to every bound eidolon within
/// <see cref="EidolonConfig.CommandRange"/> that the holder may command, after marking a block or an
/// area if the mode wants one (the marks are kept on the tool, per mode, and highlighted for its
/// holder by <see cref="EidolonCommanderSystem"/>); sneak + right-click clears the mode's marks. All
/// of it is decided on the server.
/// </summary>
public class ItemEidolonCommander : Item
{
    public const string ClassName = "seraphhorizons.EidolonCommander";
    public const string ModeKey = "seraphhorizons:mode";
    public const string BoundKey = "seraphhorizons:bound";
    public const string MarksKey = "seraphhorizons:marks";

    private SkillItem[]? _wheel;
    private int _wheelFor = -1;

    // ---- What the tool keeps ----

    /// <summary>The selected mode (follow by default).</summary>
    public static EidolonCommandMode? ModeOf(ItemStack stack) =>
        EidolonCommandModes.Get(stack.Attributes.GetString(ModeKey)) ?? EidolonCommandModes.All.FirstOrDefault();

    public static void SetMode(ItemStack stack, string code) => stack.Attributes.SetString(ModeKey, code);

    /// <summary>The ids of the eidolons the tool is bound to.</summary>
    public static List<long> Bound(ItemStack stack)
    {
        var ids = new List<long>();
        if (stack.Attributes.GetTreeAttribute(BoundKey) is { } tree)
            foreach (var entry in tree)
                if (long.TryParse(entry.Key, out long id))
                    ids.Add(id);
        return ids;
    }

    private static void SetBound(ItemStack stack, IEnumerable<long> ids)
    {
        var tree = new TreeAttribute();
        foreach (long id in ids)
            tree.SetBool(id.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
        stack.Attributes[BoundKey] = tree;
    }

    /// <summary>The marks the tool keeps for mode <paramref name="mode"/>.</summary>
    public static Marks GetMarks(ItemStack stack, string mode)
    {
        if (stack.Attributes.GetTreeAttribute(MarksKey)?.GetTreeAttribute(mode) is not { } tree)
            return Marks.None;
        static MarkPos? Read(ITreeAttribute t, string p) =>
            t.HasAttribute(p + "x") ? new MarkPos(t.GetInt(p + "x"), t.GetInt(p + "y"), t.GetInt(p + "z")) : null;
        return new Marks(Read(tree, "a"), Read(tree, "b"));
    }

    public static void SetMarks(ItemStack stack, string mode, Marks marks)
    {
        var all = stack.Attributes.GetOrAddTreeAttribute(MarksKey);
        if (marks.First == null)
        {
            all.RemoveAttribute(mode);
            return;
        }
        var tree = new TreeAttribute();
        static void Write(ITreeAttribute t, string p, MarkPos? pos)
        {
            if (pos is not { } m)
                return;
            t.SetInt(p + "x", m.X);
            t.SetInt(p + "y", m.Y);
            t.SetInt(p + "z", m.Z);
        }
        Write(tree, "a", marks.First);
        Write(tree, "b", marks.Second);
        all[mode] = tree;
    }

    // ---- The mode wheel ----

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        var modes = EidolonCommandModes.All;
        if (_wheel == null || _wheelFor != modes.Count)
        {
            DisposeWheel();
            var capi = api as ICoreClientAPI;
            _wheel = modes.Select(m =>
            {
                var item = new SkillItem { Code = new AssetLocation(EidolonCommanderSystem.Domain, m.Code), Name = Lang.Get(m.NameKey) };
                if (capi != null)
                    item.WithIcon(capi, capi.Gui.LoadSvgWithPadding(m.Icon, 48, 48, 5, ColorUtil.WhiteArgb));
                return item;
            }).ToArray();
            _wheelFor = modes.Count;
        }
        return _wheel;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel) =>
        slot.Itemstack is { } stack && ModeOf(stack) is { } mode ? Math.Max(0, EidolonCommandModes.IndexOf(mode.Code)) : 0;

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, int toolMode)
    {
        var modes = EidolonCommandModes.All;
        if (slot.Itemstack is { } stack && toolMode >= 0 && toolMode < modes.Count)
        {
            SetMode(stack, modes[toolMode].Code);
            slot.MarkDirty();
        }
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        DisposeWheel();
        base.OnUnloaded(api);
    }

    private void DisposeWheel()
    {
        foreach (var item in _wheel ?? [])
            item.Dispose();
        _wheel = null;
    }

    // ---- Using it ----

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
        bool firstEvent, ref EnumHandHandling handling)
    {
        if (slot.Empty)
            return;
        var eidolon = entitySel?.Entity as EntityLaborEidolon;
        if (entitySel != null && eidolon == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        handling = EnumHandHandling.PreventDefault;
        if (!firstEvent || api.Side != EnumAppSide.Server || (byEntity as EntityPlayer)?.Player is not IServerPlayer player)
            return;
        bool sneak = byEntity.Controls.ShiftKey;
        if (eidolon != null)
            Bind(player, slot, eidolon, unbind: sneak);
        else if (sneak)
            ClearMarks(player, slot);
        else
            Use(player, slot, blockSel?.Position);
    }

    /// <summary>Binds the tool to <paramref name="eidolon"/> (or unbinds it), if the player may command
    /// it; true when the binding changed. Server side.</summary>
    public bool Bind(IServerPlayer player, ItemSlot slot, EntityLaborEidolon eidolon, bool unbind = false)
    {
        if (slot.Itemstack is not { } stack)
            return false;
        var bound = Bound(stack);
        if (unbind)
        {
            if (!EidolonBindings.Unbind(bound, eidolon.EntityId))
            {
                Tell(player, "eidoloncommander-notbound");
                return false;
            }
            SetBound(stack, bound);
            slot.MarkDirty();
            Tell(player, "eidoloncommander-unbound", bound.Count);
            return true;
        }
        if (!eidolon.RefuseUnlessCommander(player))
            return false;
        switch (EidolonBindings.Bind(bound, eidolon.EntityId))
        {
            case BindResult.AlreadyBound:
                Tell(player, "eidoloncommander-already", bound.Count);
                return false;
            case BindResult.Full:
                Error(player, "eidoloncommander-full", EidolonBindings.Max);
                return false;
        }
        SetBound(stack, bound);
        slot.MarkDirty();
        Tell(player, "eidoloncommander-bound", bound.Count);
        return true;
    }

    /// <summary>The bound eidolons loaded within <paramref name="range"/> blocks of <paramref name="at"/>.</summary>
    public static List<EntityLaborEidolon> BoundNear(IWorldAccessor world, ItemStack stack, Vec3d at, double range) =>
        Bound(stack).Select(id => world.GetEntityById(id) as EntityLaborEidolon)
            .OfType<EntityLaborEidolon>()
            .Where(e => e.Alive && e.Pos.Dimension == 0 && e.Pos.DistanceTo(at) <= range)
            .ToList();

    /// <summary>
    /// The selected mode, used: marks the clicked block (<paramref name="clicked"/>, null for air) if
    /// the mode marks one, and once its marks are complete gives each bound eidolon in range, that the
    /// player may command, the mode's order. Returns how many were ordered. Server side.
    /// </summary>
    public int Use(IServerPlayer player, ItemSlot slot, BlockPos? clicked)
    {
        if (slot.Itemstack is not { } stack || ModeOf(stack) is not { } mode)
            return 0;
        var config = EidolonSystem.Of(api)?.Config ?? EidolonConfig.Defaults;
        var near = BoundNear(api.World, stack, player.Entity.Pos.XYZ, config.CommandRange);
        if (Bound(stack).Count == 0)
        {
            Error(player, "eidoloncommander-none-bound");
            return 0;
        }

        BlockPos? target = null;
        MarkArea? area = null;
        if (mode.Mark != EidolonMarkKind.None)
        {
            if (clicked == null)
            {
                Error(player, mode.Mark == EidolonMarkKind.Area ? "eidoloncommander-mark-corner" : "eidoloncommander-mark-block");
                return 0;
            }
            var (marks, step) = EidolonMarking.Click(mode.Mark, GetMarks(stack, mode.Code), new MarkPos(clicked.X, clicked.Y, clicked.Z), mode.MaxAreaSide);
            SetMarks(stack, mode.Code, marks);
            slot.MarkDirty();
            switch (step)
            {
                case MarkStep.FirstCorner:
                    Tell(player, "eidoloncommander-corner-first");
                    return 0;
                case MarkStep.TooLarge:
                    Error(player, "eidoloncommander-area-toolarge", mode.MaxAreaSide);
                    return 0;
                case MarkStep.Area:
                    area = marks.Area;
                    break;
                default:
                    target = clicked.Copy();
                    break;
            }
        }

        if (near.Count == 0)
        {
            Error(player, "eidoloncommander-none-near", (int)config.CommandRange);
            return 0;
        }
        int ordered = 0, refused = 0;
        foreach (var eidolon in near)
        {
            if (!eidolon.MayCommand(player))
            {
                refused++;
                continue;
            }
            var command = mode.Command(new EidolonCommandContext(eidolon, player, target, area));
            if (command.OrderCode == null)
            {
                if (command.RefusalKey != null)
                    Error(player, command.RefusalKey, command.RefusalArgs ?? []);
                continue;
            }
            eidolon.Orders?.SetOrder(command.OrderCode, command.Args);
            ordered++;
        }
        if (refused > 0)
            Error(player, "eidoloncommander-refused", refused);
        if (ordered > 0)
            Tell(player, "eidoloncommander-ordered", ordered, Lang.GetL(player.LanguageCode, mode.NameKey));
        return ordered;
    }

    private void ClearMarks(IServerPlayer player, ItemSlot slot)
    {
        if (slot.Itemstack is not { } stack || ModeOf(stack) is not { } mode || mode.Mark == EidolonMarkKind.None)
            return;
        SetMarks(stack, mode.Code, Marks.None);
        slot.MarkDirty();
        Tell(player, "eidoloncommander-marks-cleared");
    }

    private static void Tell(IServerPlayer player, string key, params object[] args) =>
        player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "seraphhorizons:" + key, args), EnumChatType.Notification);

    private static void Error(IServerPlayer player, string key, params object[] args) =>
        player.SendIngameError("seraphhorizons-" + key, Lang.GetL(player.LanguageCode, key.Contains(':') ? key : "seraphhorizons:" + key, args));

    // ---- Info ----

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is not { } stack)
            return;
        if (ModeOf(stack) is { } mode)
            dsc.AppendLine(Lang.Get("seraphhorizons:eidoloncommander-info-mode", Lang.Get(mode.NameKey)));
        int bound = Bound(stack).Count;
        dsc.AppendLine(bound == 0
            ? Lang.Get("seraphhorizons:eidoloncommander-info-unbound")
            : Lang.Get("seraphhorizons:eidoloncommander-info-bound", bound));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
    [
        new WorldInteraction { ActionLangCode = "seraphhorizons:eidoloncommander-help-order", MouseButton = EnumMouseButton.Right },
        new WorldInteraction { ActionLangCode = "seraphhorizons:eidoloncommander-help-unbind", MouseButton = EnumMouseButton.Right, HotKeyCode = "sneak" },
        new WorldInteraction { ActionLangCode = "seraphhorizons:eidoloncommander-help-mode", HotKeyCode = "toolmodeselect", MouseButton = EnumMouseButton.None },
        .. base.GetHeldInteractionHelp(inSlot),
    ];
}
