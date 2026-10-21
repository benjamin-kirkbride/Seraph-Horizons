using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Riddle;

/// <summary>
/// The riddle's controller (#714), on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/riddle.json</c>, the riddle on its bearers, its block info, its renderer). A hand machine
/// (<see cref="BEMachine.HandWorked"/>): the player works it by holding right-click on it, as on the quern,
/// and the server advances W while anyone holds; at W = 1 the charge's fines are in the box, classified
/// (<see cref="RiddleOre"/>), and its oversize on the mesh as raw ore, back to spalling. It keeps the charge
/// on the mesh as its item stack and the riddling (<see cref="MachineJob"/>: W, 0..1), the fines in the box
/// and the oversize left on the mesh. Both stay until the player takes them out by hand: no automated input
/// or output (it has no inventory a hopper or chute can reach). Crushed ore in hand takes them out and puts
/// the next charge on, by a click or while right-click is held, so a held click riddles charge after charge
/// (<see cref="TryCycle"/>). The client poses the rig from its own clock
/// (<see cref="RiddleClock"/>). The rules are Riddle/Core's.
/// </summary>
public class BERiddle : BEMachine
{
    private static readonly AssetLocation LoadSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation ShakeSound = new("game", "sounds/player/panning");
    private static readonly AssetLocation FinesSound = new("game", "sounds/block/sand");
    private static readonly AssetLocation OversizeSound = new("game", "sounds/block/rock-hit-pickaxe");
    // The server syncs W at least this often (charges); the renderer follows the clock between.
    private const double SyncStep = 0.02;
    // The panning sound is heard every few shakes, not each.
    private const int ShakesPerSound = 3;
    private const float DustInterval = 0.15f;
    private static readonly Float3 ChargeFallback = new(0.5f, 0.45f, 0.5f);

    private ItemStack? _charge;
    private MachineJob _job = MachineJob.None;
    private ItemStack? _fines;
    private ItemStack? _oversize;
    private float? _serverShakes;
    private readonly RiddleClock _clock = new();
    private float _dustTimer;

    /// <summary>A charge is on the mesh being riddled.</summary>
    public bool Riddling => _job.On && _charge != null;
    public ItemStack? Charge => _charge;
    public MachineJob Job => _job;
    /// <summary>The fines in the box, classified, or null.</summary>
    public ItemStack? Fines => _fines;
    /// <summary>The oversize left on the mesh, raw ore, or null.</summary>
    public ItemStack? Oversize => _oversize;

    /// <summary>A charge is on the riddle: being riddled, or riddled with its oversize still on the mesh.</summary>
    public bool ChargeOn => Riddling || _oversize != null;
    /// <summary>The server's W: the charge's riddling, synced in steps.</summary>
    public double RiddleWork => Riddling ? _job.Work : 1;
    /// <summary>The box holds fines (from a charge's loading until the player takes them out).</summary>
    public bool BoxHoldsFines => Riddling || _fines != null;
    public double ShakesPerCharge => _serverShakes ?? Config.ShakesPerCharge;

    /// <summary>The fines' texture (<c>ore</c>): the crushed ore's own (its ore's nugget), the charge's, else the box's.</summary>
    public AssetLocation? OreTexture => ((_charge ?? _fines)?.Collectible as Item)?.FirstTexture?.Base;

    /// <summary>The oversize's texture (<c>rock</c>): its host rock's, the oversize's, else the rock the station
    /// would give it; null for the block's default (granite).</summary>
    public AssetLocation? RockTexture =>
        (_oversize?.Collectible.Variant["rock"] ?? RiddleOre.RockUnder(Api?.World, Pos)) is { } rock
            ? new AssetLocation("game", $"block/stone/rock/{rock}1")
            : null;

    private RiddleSystem System => RiddleSystem.Of(Api);
    private RiddleConfig Config => System.Config;
    private new RiddleRig? Rig => base.Rig as RiddleRig;

    protected override bool HandWorked => true;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 100;
    public override int RenderRange => 48;

    public override bool HasWork => Riddling && !_job.Done;
    public override double? WorkProgress => Riddling ? _job.Progress : null;

    // ---- Interaction ----

    /// <summary>The riddle's own right-click, before its build's. Shift lets a held block be placed against it.
    /// Ctrl (but for the creative shortcut) takes back a charge nothing has been done to; one being riddled stays.
    /// The riddle never comes out: only breaking the station returns it.</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (controls.ShiftKey)
            return false;
        if (controls.CtrlKey && Riddling && !CreativeShortcut(byPlayer))
        {
            if (Api.Side == EnumAppSide.Server)
                TakeCharge(byPlayer);
            return true;
        }
        return null;
    }

    /// <summary>
    /// The riddle's own right-click, after its build's. Crushed ore in hand, with the riddle empty and the box
    /// too, goes on as a charge (a whole charge, of one kind), else is refused saying why; with the last
    /// charge riddled, it takes out its oversize and fines first (<see cref="TryCycle"/>). An empty hand takes
    /// out the oversize first, then the fines; with a charge on, the click starts riddling, held as on the
    /// quern (<see cref="OnInteractStep"/>).
    /// </summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var held = slot.Itemstack;
        string? code = held?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        // crushed ore in hand goes on as a charge (or is refused, saying why), unless a charge is being riddled
        if (Core.Riddling.IsCrushedOre(code) && !Riddling)
            return !server || TryCycle(slot, byPlayer);
        // what it holds comes out to an empty hand
        if (held == null && _oversize != null)
            return !server || TakeOversize(byPlayer);
        if (held == null && !Riddling && _fines != null)
            return !server || TakeFines(byPlayer);
        if (!Built)
            return false;
        if (!server)
            return true;
        if (!Riddling)
            return Error(byPlayer, _oversize != null || _fines != null ? "error-empty-hand" : "error-no-charge");
        return StartHold(byPlayer);
    }

    /// <summary>Whether crushed ore in <paramref name="slot"/> goes round again now: the last charge riddled, its
    /// oversize or fines still to come out, and the slot a whole new charge (<see cref="Core.Riddling.CanReload"/>).
    /// Both sides.</summary>
    public bool CanReload(ItemSlot? slot)
    {
        var stack = slot?.Itemstack;
        return Core.Riddling.CanReload(stack?.Collectible?.Code?.ToString(), stack != null && RiddleOre.IsClassified(stack),
            stack?.StackSize ?? 0, Config.LumpsPerCharge, Built, Riddling, _oversize != null || _fines != null);
    }

    /// <summary>Crushed ore in <paramref name="slot"/> on the riddle (server side): when it goes round again
    /// (<see cref="CanReload"/>) the oversize and the fines go to the player first, as an empty hand takes
    /// them; then the charge goes on (<see cref="TryLoad"/>, refused saying why).</summary>
    public bool TryCycle(ItemSlot slot, IPlayer byPlayer)
    {
        if (CanReload(slot))
        {
            TakeOversize(byPlayer);
            TakeFines(byPlayer);
        }
        return TryLoad(slot, byPlayer);
    }

    /// <summary>
    /// Right-click still held (both sides). With crushed ore in hand, the charge riddled and its oversize or
    /// fines waiting, the server takes them out to the player and puts the next charge on from the hand
    /// (<see cref="TryCycle"/>), and the riddling goes on without a fresh click; the client keeps holding
    /// meanwhile, its own charge synced after. Else as every hand machine.
    /// </summary>
    public override bool OnInteractStep(IPlayer byPlayer, float secondsUsed)
    {
        var slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (slot == null || !CanReload(slot))
            return base.OnInteractStep(byPlayer, secondsUsed);
        if (Api.Side == EnumAppSide.Client)
        {
            base.OnInteractStep(byPlayer, secondsUsed);
            return true;
        }
        return TryCycle(slot, byPlayer) && base.OnInteractStep(byPlayer, secondsUsed);
    }

    /// <summary>Puts a charge from <paramref name="slot"/> on the riddle (server side), if it takes one now:
    /// <see cref="RiddleConfig.LumpsPerCharge"/> crushed ore of the held kind.</summary>
    public bool TryLoad(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        int lumps = Config.LumpsPerCharge;
        switch (Core.Riddling.CanLoad(code, stack != null && RiddleOre.IsClassified(stack), slot.StackSize, lumps,
                                      Built, ChargeOn, _fines != null))
        {
            case RiddleLoadVerdict.Loads:
                break;
            case RiddleLoadVerdict.AlreadyClassified:
                return Error(byPlayer, "error-classified");
            case RiddleLoadVerdict.Incomplete:
                return Error(byPlayer, "error-incomplete");
            case RiddleLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case RiddleLoadVerdict.BoxFull:
                return Error(byPlayer, "error-box-full");
            case RiddleLoadVerdict.TooFew:
                return Error(byPlayer, "error-too-few", lumps);
            default:
                return false;
        }
        Load(slot.TakeOut(lumps));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack charge)
    {
        _charge = charge;
        _job = Core.Riddling.Start();
        var at = WorldPoint(Rig?.Charge ?? ChargeFallback);
        Api.World.PlaySoundAt(LoadSound, at.X, at.Y, at.Z);
        MarkDirty(true);
    }

    /// <summary>Ctrl + right-click (server side): a charge nothing has been done to comes back; one being
    /// riddled stays.</summary>
    public bool TakeCharge(IPlayer byPlayer)
    {
        if (!Riddling)
            return false;
        if (!_job.Untouched)
            return Error(byPlayer, "error-busy");
        var charge = _charge!;
        _charge = null;
        _job = MachineJob.None;
        Give(byPlayer, charge);
        Api.World.PlaySoundAt(LoadSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Takes the oversize off the mesh (server side), to the player.</summary>
    public bool TakeOversize(IPlayer byPlayer)
    {
        if (_oversize is not { } oversize)
            return false;
        _oversize = null;
        Give(byPlayer, oversize);
        var at = WorldPoint(Rig?.Charge ?? ChargeFallback);
        Api.World.PlaySoundAt(OversizeSound, at.X, at.Y, at.Z, null, true, 16, 0.5f);
        MarkDirty(true);
        return true;
    }

    /// <summary>Takes the fines out of the box (server side), to the player.</summary>
    public bool TakeFines(IPlayer byPlayer)
    {
        if (_fines is not { } fines || Riddling)
            return false;
        _fines = null;
        Give(byPlayer, fines);
        var at = WorldPoint(Rig?.Fines ?? new Float3(0.5f, 0.15f, 0.5f));
        Api.World.PlaySoundAt(FinesSound, at.X, at.Y, at.Z);
        MarkDirty(true);
        return true;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.8, 0.5));
    }

    /// <summary>The riddle's own message (<c>seraphhorizons:riddle-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("riddle-" + key, Lang.GetL(sp.LanguageCode, RiddleSystem.Domain + ":riddle-" + key, args));
        return false;
    }

    /// <summary>The help once built: the oversize, then a charge to riddle (and take back while untouched),
    /// then the fines come out; with nothing on, a charge goes on.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => RiddleSystem.Domain + ":blockhelp-riddle-" + action;
        if (!Riddling && (_oversize != null || _fines != null)
            && Stacks(Api.World, ["seraphhorizons:crushedore-*-medium"], Config.LumpsPerCharge) is { Length: > 0 } next)
            help.Add(new WorldInteraction { ActionLangCode = Key("next"), MouseButton = EnumMouseButton.Right, Itemstacks = next });
        if (_oversize != null)
            help.Add(new WorldInteraction { ActionLangCode = Key("takeoversize"), MouseButton = EnumMouseButton.Right });
        else if (Riddling)
        {
            help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
            if (_job.Untouched)
                help.Add(new WorldInteraction { ActionLangCode = Key("takecharge"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
        }
        else if (_fines != null)
            help.Add(new WorldInteraction { ActionLangCode = Key("takefines"), MouseButton = EnumMouseButton.Right });
        else if (Stacks(Api.World, ["seraphhorizons:crushedore-*-medium"], Config.LumpsPerCharge) is { Length: > 0 } ore)
            help.Add(new WorldInteraction { ActionLangCode = Key("load"), MouseButton = EnumMouseButton.Right, Itemstacks = ore });
    }

    // ---- The riddling ----

    // The riddling syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool WorkUnpowered(float dt)
    {
        Shake(Core.Riddling.ShakeRadiansPerSecond * dt);
        return false;
    }

    /// <summary>
    /// Riddles by <paramref name="radians"/> of the clock (server side): W advances by their shakes over the
    /// charge's, and at W = 1 the charge is split (<see cref="Core.Riddling.Split"/>): its fines into the box,
    /// classified, its oversize onto the mesh as raw ore. Returns whether this step finished the charge.
    /// </summary>
    public bool Shake(double radians)
    {
        if (!Riddling || !Built)
            return false;
        var before = _job;
        double shakes = Config.ShakesPerCharge;
        (_job, bool finished) = Core.Riddling.Advance(_job, radians, shakes);
        if (Core.Riddling.ShakesCrossed(before.Work, _job.Work, shakes) > 0
            && (int)Math.Floor(_job.Work * shakes + 1e-9) % ShakesPerSound == 1)
        {
            var at = WorldPoint(Rig?.Charge ?? ChargeFallback);
            Api.World.PlaySoundAt(ShakeSound, at.X, at.Y, at.Z, null, true, 16, 0.7f);
        }
        if (finished)
        {
            Deliver();
            MarkDirty(true);
            RefreshState();
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty(false);
        return false;
    }

    // The charge riddled: its fines into the box (classified), its oversize onto the mesh (raw ore).
    private void Deliver()
    {
        var charge = _charge!;
        var (fines, oversize) = Core.Riddling.Split(charge.StackSize, Config.OversizePerCharge);
        _charge = null;
        _job = MachineJob.None;
        var rock = RiddleOre.RockUnder(Api.World, Pos);
        var oversizeStack = oversize > 0 ? RiddleOre.Oversize(Api.World, charge, oversize, rock) : null;
        if (oversize > 0 && oversizeStack == null)
        {
            // no raw ore of it in this world: the oversize stays the crushed ore, unclassified, rather than lost
            oversizeStack = charge.Clone();
            oversizeStack.StackSize = oversize;
        }
        if (fines > 0)
        {
            var through = charge.Clone();
            through.StackSize = fines;
            RiddleOre.MarkClassified(through);
            _fines = through;
        }
        _oversize = oversizeStack;
        Api.World.Logger.Audit("Riddle at {0} riddled {1}x{2}: {3} fines, {4} oversize ({5}).", Pos, charge.StackSize,
            charge.Collectible.Code, fines, oversize, oversizeStack?.Collectible.Code);
    }

    // ---- The renderer's ----

    /// <summary>The frame always, the riddle when fitted, the charge while one is on, the fines while the box
    /// holds them (<see cref="RiddleClock.Shows"/>).</summary>
    public override bool ShowsPart(string? requires) => RiddleClock.Shows(requires, Built, ChargeOn, BoxHoldsFines);

    /// <summary>The fines' and the oversize's textures follow what is in it.</summary>
    public override string TextureKey => $"{OreTexture}|{RockTexture}";

    /// <summary>The block's textures with the crushed ore's in place of <c>ore</c> and the host rock's in place of <c>rock</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new ContentsTextureSource(base.PartTextures(capi), MachineMeshes.AtlasTexture(capi, OreTexture), MachineMeshes.AtlasTexture(capi, RockTexture));

    /// <summary>The riddle's clock θ, turning while it is riddled, W, k and p (<see cref="RiddleClock"/>). Nothing
    /// is drawn falling through the mesh (the model's first weak spot): while riddling, dust of the ore falls
    /// under the riddle.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        bool on = ChargeOn;
        bool held = Held && on;
        _clock.Advance(dt, on, RiddleWork, held, ShakesPerCharge);
        if (!far && held && _clock.Work < 1 && Rig is { } rig && (_dustTimer += dt) >= DustInterval)
        {
            _dustTimer = 0;
            SpawnDust(rig);
        }
        return _clock.Input();
    }

    // Fines falling through the mesh into the box.
    private void SpawnDust(RiddleRig rig)
    {
        var at = WorldPoint(rig.Charge);
        int colour = ColorUtil.ToRgba(200, 105, 95, 90);
        Api.World.SpawnParticles(3, colour,
            at.AddCopy(-0.25, -0.12, -0.25), at.AddCopy(0.25, -0.1, 0.25),
            new Vec3f(-0.02f, -0.6f, -0.02f), new Vec3f(0.02f, -0.3f, 0.02f), 0.4f, 1f, 0.15f, EnumParticleModel.Cube, null);
    }

    /// <summary>The block's textures with the crushed ore's in place of <c>ore</c> and the host rock's in place of <c>rock</c>.</summary>
    private sealed class ContentsTextureSource(ITexPositionSource inner, TextureAtlasPosition? ore, TextureAtlasPosition? rock) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode switch
        {
            "ore" when ore != null => ore,
            "rock" when rock != null => rock,
            _ => inner[textureCode]!,
        };
    }

    // ---- Breaking ----

    /// <summary>What breaking the station gives besides the frame: the riddle's nails and strips, the
    /// charge (whole: nothing is lost by breaking), the oversize and the fines.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (_charge != null)
            yield return _charge.Clone();
        if (_oversize != null)
            yield return _oversize.Clone();
        if (_fines != null)
            yield return _fines.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        SetStack(tree, "charge", Riddling ? _charge : null);
        SetStack(tree, "fines", _fines);
        SetStack(tree, "oversize", _oversize);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetFloat("shakes", Config.ShakesPerCharge);
    }

    private static void SetStack(ITreeAttribute tree, string key, ItemStack? stack)
    {
        if (stack != null)
            tree.SetItemstack(key, stack);
        else
            tree.RemoveAttribute(key);
    }

    private static ItemStack? GetStack(ITreeAttribute tree, string key, IWorldAccessor world)
    {
        var stack = tree.GetItemstack(key);
        return stack != null && stack.ResolveBlockOrItem(world) ? stack : null;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _charge = GetStack(tree, "charge", worldForResolving);
        _job = Core.Riddling.Restore(_charge != null, tree.GetDouble("work"));
        _fines = GetStack(tree, "fines", worldForResolving);
        _oversize = GetStack(tree, "oversize", worldForResolving);
        if (worldForResolving.Side == EnumAppSide.Client)
            _serverShakes = tree.TryGetFloat("shakes");
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        foreach (var stack in new[] { _charge, _fines, _oversize })
            stack?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(stack), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        foreach (var stack in new[] { _charge, _fines, _oversize })
            stack?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    /// <summary>The charge on the riddle and how far it is riddled, the oversize on the mesh and the fines in
    /// the box; on an empty riddle, what a charge is.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built)
            return;
        string L(string key, params object[] args) => Lang.Get(RiddleSystem.Domain + ":riddle-" + key, args);
        if (Riddling)
            dsc.AppendLine(L("info-charge", _charge!.GetName(), (int)Math.Floor(_job.Work * 100 + 1e-9)));
        if (_oversize != null)
            dsc.AppendLine(L("info-oversize", _oversize.StackSize, _oversize.GetName()));
        if (_fines != null)
            dsc.AppendLine(L("info-fines", _fines.StackSize, _fines.GetName()));
        if (!ChargeOn && _fines == null)
            dsc.AppendLine(L("info-empty", Config.LumpsPerCharge));
    }

    /// <summary>For a wrench: the riddling's W exactly, and the shakes a charge takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (Riddling)
            dsc.AppendLine(Lang.Get(RiddleSystem.Domain + ":riddle-info-work",
                _job.Work.ToString("0.000", CultureInfo.InvariantCulture), ShakesPerCharge.ToString("0.##", CultureInfo.InvariantCulture)));
    }
}
