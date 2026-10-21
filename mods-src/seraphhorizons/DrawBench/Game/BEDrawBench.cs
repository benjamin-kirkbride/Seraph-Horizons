using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.DrawBench;

/// <summary>
/// The draw bench's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/drawbench.json</c> with the die its durable wearing part, its cells and power cell, its
/// oil (<see cref="OilMachine.DrawBench"/>), its block info, its renderer). It keeps the hollow section on
/// the bench as its item stack and the draw (<see cref="MachineJob"/>: W, the pipe sections drawn, 0..4).
/// The server draws from the power cell's shaft angle, drops a pipe section at the output face as each
/// section's draw finishes (W crossing m + <see cref="Drawing.HandOut"/>), draining a section's oil, and
/// wears the die when the hollow is done (W = 4); the client poses the rig from the shaft and its own W
/// (<see cref="DrawBenchClock"/>). The rules are DrawBench/Core's.
/// </summary>
public class BEDrawBench : BEMachine
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation SectionSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation DrawSound = new("game", "sounds/effect/gears");
    // The server syncs W at least this often (sections); the renderer follows the shaft between.
    private const double SyncStep = 0.05;
    private const float DustInterval = 0.15f;
    private const float DripInterval = 0.35f;

    private ItemStack? _hollow;
    private MachineJob _job = MachineJob.None;
    private float _soundSeconds;
    private float? _serverTurnsLead;
    private float? _serverTurnsCopper;
    private string[]? _serverDieDraws;
    private readonly DrawBenchClock _clock = new();
    private double _lastPsi;
    private float _dustTimer;
    private float _dripTimer;

    public bool JobOn => _job.On && _hollow != null;
    public ItemStack? Hollow => _hollow;
    public MachineJob Job => _job;

    /// <summary>The fitted die: its code and durability left; null without one (taken out, or spent).</summary>
    public (string Code, double Left)? Die => Assembly?.Wearing.Fitted(Drawing.DiePart);

    /// <summary>The fitted die's metal (<c>iron</c>, <c>steel</c>), or null.</summary>
    public string? DieMetal => Drawing.DieMetalOf(Die?.Code);

    /// <summary>The die's durability left; 0 without a die.</summary>
    public int DieLeft => Die is { } d ? (int)Math.Round(d.Left) : 0;

    public double TurnsPerSection(int k) => k switch
    {
        1 => _serverTurnsLead ?? Config.TurnsPerSectionLead,
        2 => _serverTurnsCopper ?? Config.TurnsPerSectionCopper,
        _ => 0,
    };

    /// <summary>The metals the fitted die draws (none without a die), by the server's settings.</summary>
    public IReadOnlyList<string> DieDraws =>
        DieMetal == null ? [] : _serverDieDraws ?? Config.MetalsFor(DieMetal);

    private DrawBenchSystem System => DrawBenchSystem.Of(Api);
    private DrawBenchConfig Config => System.Config;
    private new DrawBenchRig? Rig => base.Rig as DrawBenchRig;

    /// <summary>The die's uses are the setting's durability, as the die item's own.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world) =>
        base.BuildSpec(world)?.WithUses(Drawing.DiePart, DrawBenchSystem.Of(world.Api).Config.DieDurability);

    protected override OilMachine? OilKind => OilMachine.DrawBench;
    protected override float ConfigMinSpeed => Config.MinSpeed;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 200;
    public override int RenderRange => 48;

    /// <summary>Whole with its die in: until then it is the frame, puts the token load on its shaft, and takes
    /// the creative shortcut (a new steel die last).</summary>
    public override bool Complete => Ready;
    protected override bool LoadsShaft => Ready;
    protected override bool CreativeFills => !Ready;

    /// <summary>The configured resistance of the metal on the bench (lead's when empty).</summary>
    protected override double ShaftLoad => Config.Resistance(_job.Class);

    public override bool HasWork => JobOn;
    public override double? WorkProgress => JobOn ? _job.Progress : null;
    protected override Float3? SmokePoint => Rig?.Die;

    // ---- Interaction ----

    /// <summary>Ctrl + right-click takes the die back out, with what durability it has left, once no hollow
    /// is on the bench (the creative shortcut comes first while a stage or the die is missing).</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (!controls.CtrlKey || controls.ShiftKey || CreativeShortcut(byPlayer))
            return null;
        if (Api.Side == EnumAppSide.Server)
            TakeDie(byPlayer);
        return true;
    }

    /// <summary>A lead or copper hollow section (the game's chute section) goes on (an ingot, an angle or a
    /// pipe section, what comes off, never does).</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (Drawing.ClassOfHollow(slot.Itemstack?.Collectible?.Code?.ToString()) == 0)
            return false;
        if (Api.Side == EnumAppSide.Server)
            TryLoadHollow(slot, byPlayer);
        return true;
    }

    /// <summary>Ctrl + right-click (server side): the die, with what durability it has left, once
    /// no hollow is on the bench.</summary>
    public bool TakeDie(IPlayer byPlayer)
    {
        if (Die == null)
            return false;
        if (JobOn)
            return Error(byPlayer, "error-busy");
        if (Assembly!.Take(Drawing.DiePart) is not { } die || DieStack(die.Code, (int)Math.Round(die.Left)) is not { } stack)
            return false;
        Give(byPlayer, stack);
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>A die of <paramref name="code"/> with <paramref name="left"/> durability.</summary>
    private ItemStack? DieStack(string code, int left)
    {
        if (Api.World.GetItem(new AssetLocation(code)) is not { } item)
            return null;
        var stack = new ItemStack(item);
        if (left < item.GetMaxDurability(stack))
            stack.Attributes.SetInt("durability", Math.Max(1, left));
        return stack;
    }

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is drawn into, or null when
    /// it does not exist in this game.</summary>
    private Item? SectionItem(int k) =>
        Drawing.SectionFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a hollow section from <paramref name="slot"/> on the bench, if the bench takes it now.</summary>
    public bool TryLoadHollow(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Drawing.CanLoad(code, Ready, JobOn, DieDraws.ToList()))
        {
            case DrawLoadVerdict.Loads:
                break;
            case DrawLoadVerdict.Incomplete:
                return Error(byPlayer, Built ? "error-no-die" : "error-incomplete");
            case DrawLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case DrawLoadVerdict.DieRefuses:
                return Error(byPlayer, "error-die-refuses-" + Drawing.MetalOf(Drawing.ClassOfHollow(code)),
                    Lang.Get(DrawBenchSystem.Domain + ":drawbench-die-" + DieMetal));
            default:
                return false;
        }
        if (SectionItem(Drawing.ClassOfHollow(code)) == null)
            return Error(byPlayer, "error-no-section");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack hollow)
    {
        _hollow = hollow;
        _job = Drawing.Start(Drawing.ClassOfHollow(hollow.Collectible.Code.ToString()));
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _hollow = null;
        _job = MachineJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    /// <summary>The bench's own message (<c>seraphhorizons:drawbench-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("drawbench-" + key, Lang.GetL(sp.LanguageCode, DrawBenchSystem.Domain + ":drawbench-" + key, args));
        return false;
    }

    /// <summary>The help once built: the hollow sections the fitted die draws on an empty bench, and Ctrl to
    /// take the die out with none on.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => DrawBenchSystem.Domain + ":blockhelp-drawbench-" + action;
        if (Ready && !JobOn)
        {
            var draws = DieDraws;
            var hollows = Stacks(Api.World, [Drawing.LeadHollow, Drawing.CopperHollow], 1)
                .Where(s => draws.Contains(Drawing.MetalOf(Drawing.ClassOfHollow(s.Collectible.Code.ToString()))!)).ToArray();
            if (hollows.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadhollow"), MouseButton = EnumMouseButton.Right, Itemstacks = hollows });
        }
        bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (Die != null && !JobOn && !(creative && CreativeFills))
            help.Add(new WorldInteraction { ActionLangCode = Key("takedie"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The draw ----

    // The draw syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool Work(double radians)
    {
        Draw(radians);
        return false;
    }

    /// <summary>Draws by <paramref name="radians"/> of shaft rotation (server side; call only while
    /// running). Each time W crosses a section's hand-out (m + <see cref="Drawing.HandOut"/>, the jaws
    /// open on the drawn section), a pipe section comes off and the tank drains a section's oil; at
    /// W = 4, the dog back at the die after the fourth, the die wears and the hollow is done. Returns the
    /// sections that came off.</summary>
    public int Draw(double radians)
    {
        if (!JobOn || !Ready)
            return 0;
        var before = _job;
        (_job, int sections, bool finished) = Drawing.Advance(_job, radians, Config.TurnsPerSection(_job.Class));
        if (sections > 0 && SectionItem(_job.Class) is { } section)
        {
            DrainOil(sections);
            for (int i = 0; i < sections; i++)
                Deliver(new ItemStack(section));
        }
        if (finished)
            Finish();
        else if (sections > 0 || Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty(sections > 0);
        return sections;
    }

    /// <summary>The hollow is drawn: the die wears its fixed points (a spent die breaks with the
    /// tool-break sound and the bench stops until a new one goes in), and the bench clears.</summary>
    private void Finish()
    {
        if (Assembly!.Wearing.Wear(Drawing.DiePart, Config.DieWearPerHollow))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearJob();
        MarkDirty(true);
    }

    /// <summary>Drops <paramref name="stack"/> just beyond the output face.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var at = WorldPoint(rig.OutputDrop());
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        Api.World.SpawnItemEntity(stack, at, new Vec3d(n.X * 0.05, 0.02, n.Z * 0.05));
        Api.World.PlaySoundAt(SectionSound, at.X, at.Y, at.Z);
    }

    // A drawing bench is heard.
    protected override void OnClientTick(float dt)
    {
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Running)
        {
            _soundSeconds = 0;
            return;
        }
        if ((_soundSeconds -= dt) <= 0)
        {
            _soundSeconds = 1.8f;
            var at = WorldPoint(rig.Die);
            capi.World.PlaySoundAt(DrawSound, at.X, at.Y, at.Z, null, true, 16, 0.5f);
        }
    }

    // ---- The renderer's ----

    /// <summary>The billets (<c>billetlead</c>, <c>billetcopper</c>) as the clock shows them; every other part as
    /// its stage (and the die, its set) is fitted.</summary>
    public override bool ShowsPart(string? requires) =>
        requires is DrawBenchRequires.BilletLead or DrawBenchRequires.BilletCopper ? _clock.ShowsBillet(requires) : base.ShowsPart(requires);

    /// <summary>The shaft turns the drive whether or not the bench is complete.</summary>
    public override bool Animates => true;

    /// <summary>The die's texture follows its metal.</summary>
    public override string TextureKey => DieMetal ?? "";

    /// <summary>The block's textures with the die's metal in place of <c>die</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new DieTextureSource(base.PartTextures(capi), DieMetal is { } metal ? MachineMeshes.MetalTexture(capi, metal) : null);

    /// <summary>θ and ψ the shaft's (about the entry shaft's native x axis), W, k and p from the clock, and the
    /// oil's fill for the oiler's level; while the bench draws, metal dust at the die's mouth and, with oil in
    /// the tank, drips from the oiler's spout.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var (theta, psi) = AdvanceShaft();
        double turned = Math.Max(0, psi - _lastPsi);
        _lastPsi = psi;
        int k = JobOn ? _job.Class : 0;
        bool running = Running;
        _clock.Advance(dt, turned, k, _job.Work, running, TurnsPerSection(k));
        if (!far && running && Rig is { } rig)
        {
            if ((_dustTimer += dt) >= DustInterval)
            {
                _dustTimer = 0;
                SpawnDust(rig, k);
            }
            if (OilFill > 0 && (_dripTimer += dt) >= DripInterval)
            {
                _dripTimer = 0;
                SpawnDrip(rig);
            }
        }
        return _clock.Input(theta, psi, OilFill);
    }

    private void SpawnDust(DrawBenchRig rig, int k)
    {
        var at = WorldPoint(rig.Die);
        // fine metal from the die's mouth: lead grey, copper red
        int colour = k == 2 ? ColorUtil.ToRgba(220, 70, 120, 190) : ColorUtil.ToRgba(220, 120, 110, 105);
        Api.World.SpawnParticles(2, colour,
            at.AddCopy(-0.03, -0.03, -0.03), at.AddCopy(0.03, 0.03, 0.03),
            new Vec3f(-0.3f, -0.1f, -0.3f), new Vec3f(0.3f, 0.3f, 0.3f), 0.6f, 1f, 0.15f, EnumParticleModel.Cube, null);
        // and a wisp of the lubricant burning off
        if (OilFill > 0 && Api.World.Rand.NextDouble() < 0.25)
            Api.World.SpawnParticles(1, ColorUtil.ToRgba(60, 200, 200, 200),
                at, at.AddCopy(0.02, 0.02, 0.02),
                new Vec3f(-0.05f, 0.1f, -0.05f), new Vec3f(0.05f, 0.25f, 0.05f), 1.2f, -0.02f, 0.4f, EnumParticleModel.Quad, null);
    }

    private void SpawnDrip(DrawBenchRig rig)
    {
        var at = WorldPoint(rig.Drip);
        Api.World.SpawnParticles(1, ColorUtil.ToRgba(190, 40, 140, 190),
            at.AddCopy(-0.01, -0.02, -0.01), at.AddCopy(0.01, 0, 0.01),
            new Vec3f(0, -0.4f, 0), new Vec3f(0, -0.2f, 0), 0.6f, 1f, 0.12f, EnumParticleModel.Quad, null);
    }

    /// <summary>The block's textures with the die's metal in place of <c>die</c>.</summary>
    private sealed class DieTextureSource(ITexPositionSource inner, TextureAtlasPosition? metal) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "die" && metal != null ? metal : inner[textureCode]!;
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the die with its
    /// durability), and the hollow if no section has been drawn from it yet (once one has, the rest of
    /// it is lost with the bench).</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (JobOn && Drawing.SectionsDone(_job) == 0)
            yield return _hollow!.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (JobOn)
            tree.SetItemstack("hollow", _hollow);
        else
            tree.RemoveAttribute("hollow");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("turnsPerSectionLead", Config.TurnsPerSectionLead);
            tree.SetFloat("turnsPerSectionCopper", Config.TurnsPerSectionCopper);
            tree.SetString("dieDraws", string.Join(",", Config.MetalsFor(DieMetal)));
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _hollow = tree.GetItemstack("hollow");
        if (_hollow != null && !_hollow.ResolveBlockOrItem(worldForResolving))
            _hollow = null;
        _job = _hollow == null ? MachineJob.None : Drawing.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _hollow = null;
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverTurnsLead = tree.TryGetFloat("turnsPerSectionLead");
            _serverTurnsCopper = tree.TryGetFloat("turnsPerSectionCopper");
            _serverDieDraws = tree.GetString("dieDraws") is { } draws
                ? draws.Split(',', StringSplitOptions.RemoveEmptyEntries)
                : null;
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _hollow?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_hollow), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _hollow?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    /// <summary>The die and what it draws, and the hollow on the bench with its sections drawn.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(DrawBenchSystem.Domain + ":drawbench-" + key, args);
        if (DieMetal is { } die)
        {
            var draws = DieDraws.Select(m => L("metal-" + m)).ToList();
            dsc.AppendLine(L("info-die", L("die-" + die), draws.Count == 0 ? L("info-draws-nothing") : string.Join(", ", draws)));
        }
        if (JobOn)
            dsc.AppendLine(L("info-hollow", L("metal-" + Drawing.MetalOf(_job.Class)), Drawing.SectionsDone(_job), Drawing.SectionsPerHollow));
    }

    /// <summary>For a wrench: the draw's W exactly, and the shaft turns a section of its metal takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (JobOn)
            dsc.AppendLine(Lang.Get(DrawBenchSystem.Domain + ":drawbench-info-draw",
                _job.Work.ToString("0.000", CultureInfo.InvariantCulture), Drawing.SectionsPerHollow,
                TurnsPerSection(_job.Class).ToString("0.###", CultureInfo.InvariantCulture)));
    }
}
