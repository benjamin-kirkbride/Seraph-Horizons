using System.Text;
using SeraphHorizons.Mod.AmalgamPan.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.AmalgamPan;

/// <summary>
/// The amalgam pan's block entity (#718). Holds the staged build (the muller, then its shoes), the
/// batch (<see cref="AmalgamPanBatch"/>: mercury, the charge's count, water, turns), the charge's
/// quartz as its item stack, a worked charge's amalgam waiting to be taken out, and the fraction of an
/// amalgam item carried to the next charge (<see cref="UnitCarry"/>). The muller turns by hand while a
/// player holds right-click, as the quern's stone, or from an axle in the block below, with no player;
/// either way a charge with its water and mercury is worked a turn at a time, and when done ore
/// processing's recovery (<see cref="OreRecovery.Amalgamate"/>) says what amalgam it makes and what the
/// quartz carries on (amalgamated, so the rocker no longer loses its free metal at × 0.7). Nothing goes
/// in or out but by hand. The client draws it (<see cref="AmalgamPanRenderer"/>).
/// </summary>
public class BEAmalgamPan : BlockEntity, IMachinePower
{
    /// <summary>Expanded Matter's mercury, as the still's retort names it (<c>retort.mercury</c>).</summary>
    public const string MercuryCode = "em:mercuryportion";
    private const string WaterCode = "game:waterportion";
    private static readonly AssetLocation LoadSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation PourSound = new("game", "sounds/effect/water-pour");
    private static readonly AssetLocation GrindSound = new("game", "sounds/block/quern");
    private const string BuildKey = "build";
    // A client's own player holding right-click counts as working for this long after its last step.
    private const long LocalHoldMs = 250;
    // The server syncs the turns at least this often (a share of the charge).
    private const double SyncStep = 0.05;

    private MachineAssembly? _assembly;
    private AmalgamPanBatch _batch = new();
    private ItemStack? _charge;
    private ItemStack? _amalgam;
    private readonly UnitCarry _carry = new();
    private readonly HandHolds _holds = new();
    private bool _held;
    private long _localHeldAt = long.MinValue / 2;
    private float _lastAngle = float.NaN;
    private float _grindSeconds;
    private int _syncedNeed;
    private AmalgamPanRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    /// <summary>The muller and its shoes, fitted in order (the shared machine build, #711). An empty
    /// build stands in when the build file is missing: such a pan is never complete.</summary>
    public MachineAssembly Assembly => _assembly ??= new MachineAssembly(Spec);
    public AmalgamPanBatch Batch => _batch;
    public bool Complete => _assembly is { Built: true } a && a.Build.Stages.Count > 0;
    public ItemStack? Charge => _charge;
    public ItemStack? Amalgam => _amalgam;
    public AmalgamPanConfig Config => System.Config;

    private AmalgamPanSystem System => AmalgamPanSystem.Of(Api);

    // the pan's build, or one with no stages (never complete) when its file is missing
    private MachineBuild Spec => System.Build ?? new MachineBuild(AmalgamPanSystem.MachineId, null, FeedMode.Batch, [], []);

    /// <summary>The axle below, as the shared machine behaviour sees it.</summary>
    public BEBehaviorMachineMP? Power => GetBehavior<BEBehaviorMachineMP>();

    /// <summary>An axle below turns the pan.</summary>
    public bool Powered => Power?.Network is { } net && Math.Abs(net.Speed * Power.GearedRatio) > 0.001f;

    /// <summary>Someone holds right-click on the pan (the server's word, or this client's own player).</summary>
    public bool HandHeld =>
        _held || Api is { Side: EnumAppSide.Client } && Api.World.ElapsedMilliseconds - _localHeldAt <= LocalHoldMs;

    /// <summary>The muller is turning on a charge it can work: the pan is working.</summary>
    public bool Working => (Powered || _held) && CanWork() == PanRefusal.None;

    // IMachinePower
    public BlockFacing PowerFace => BlockFacing.DOWN;
    public float Resistance => !Complete ? Config.IdleResistance
        : CanWork() == PanRefusal.None ? Config.WorkingResistance : Config.IdleResistance;

    /// <summary>The shoes' metal (<c>iron</c>, <c>steel</c>) for the renderer, or null.</summary>
    public string? ShoeMetal
    {
        get
        {
            foreach (var drop in Assembly.Returns())
                if (drop.Code.StartsWith("game:metalplate-", StringComparison.Ordinal))
                    return drop.Code["game:metalplate-".Length..];
            return null;
        }
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        _ = Assembly;
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnTick, 100);
        else
        {
            RegisterGameTickListener(OnClientTick, 100);
            if (api is ICoreClientAPI capi && System.Rig is { } rig)
                _renderer = new AmalgamPanRenderer(capi, this, rig);
        }
    }

    // ---- Recovery ----

    private OreRecovery? Recovery => OreProcessingSystem.Of(Api).Recovery;

    private double AmalgamMercury => Recovery?.Retort.AmalgamMercury ?? 10;

    /// <summary>The quartz in a stack as ore processing reads it, and whether its ore has free metal.</summary>
    private (OreStream? Stream, bool Free) Quartz(ItemStack? stack)
    {
        if (stack == null || OreStreamAttributes.Read(stack) is not { } stream)
            return (null, false);
        return (stream, Recovery?.Ore(stream.Ore).Free == true);
    }

    /// <summary>What the charge in the pan would make: the amalgam's units (all the items') and the
    /// worked quartz's stream (an item's).</summary>
    private (double Units, OreStream After, string Ore)? Pass()
    {
        if (_charge == null || Recovery is not { } recovery || Quartz(_charge).Stream is not { } stream)
            return null;
        var pass = recovery.Amalgamate(HandStation.AmalgamPan, stream);
        return (pass.Amalgam * _charge.StackSize, pass.Rest, stream.Ore);
    }

    /// <summary>Amalgam items the charge will make, with what is carried.</summary>
    public int AmalgamItemsDue() =>
        Pass() is { } p
            ? AmalgamPanBatch.AmalgamItems(p.Units, _carry.HeldFor(OreProducts.AmalgamCode(p.Ore)), Recovery!.ConcentrateUnits)
            : 0;

    /// <summary>The mercury the charge in the pan needs, in portions (a client has the server's figure:
    /// only the server reads ore processing's recovery).</summary>
    public int MercuryNeeded() =>
        Api?.Side == EnumAppSide.Client ? _syncedNeed : AmalgamPanBatch.MercuryNeeded(AmalgamItemsDue(), AmalgamMercury);

    public PanRefusal CanWork() => _batch.CanWork(Complete, MercuryNeeded(), Config);

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the pan. Ctrl in creative mode on an unbuilt pan fits its next stage; Ctrl with
    /// an untouched charge takes the quartz back. A stage's part is fitted. A container of mercury pours
    /// it in, of water pours the charge's water, an empty one takes the mercury back out (no charge in).
    /// Crushed or ground gold or silver quartz is loaded. With a worked charge, an empty hand takes out
    /// the amalgam and the quartz. Otherwise, on a built pan with a charge, right-click held turns the
    /// muller (<see cref="OnWorkStep"/>). Shift lets a held block be placed against it. Decided on the
    /// server; the client says whether the click is the pan's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        var controls = byPlayer.Entity.Controls;
        bool server = Api.Side == EnumAppSide.Server;
        bool ctrl = controls.CtrlKey && !controls.ShiftKey;
        if (ctrl && CreativeShortcutApplies(byPlayer))
            return !server || FitNext();
        if (ctrl && held == null && _batch.Charged && _batch.Untouched)
            return !server || TakeCharge(byPlayer);
        if (controls.ShiftKey)
            return false;
        string? code = held?.Collectible?.Code?.ToString();
        if (code != null && !Complete && Assembly.Check(code, held!.StackSize, held.Collectible.MaxStackSize).Verdict != AssemblyVerdict.NotAPart)
            return !server || TryFit(slot!, byPlayer);
        if (held?.Collectible is ILiquidSource or BlockLiquidContainerBase)
            return !server || Liquid(slot!, byPlayer);
        if (held != null && Quartz(held).Stream != null)
            return !server || TryLoad(slot!, byPlayer);
        if (_batch.Worked)
            return !server || (held == null ? TakeOut(byPlayer) : Error(byPlayer, "worked"));
        if (!Complete)
            return code == null && (!server || Error(byPlayer, "incomplete", NextName()));
        if (!_batch.Charged)
            return held == null && (!server || Error(byPlayer, "nocharge"));
        if (!server)
            return true;
        var why = CanWork();
        if (why != PanRefusal.None)
            return Refuse(byPlayer, why);
        _holds.Hold(byPlayer.PlayerUID, Api.World.ElapsedMilliseconds);
        UpdateHeld();
        return true;
    }

    /// <summary>Right-click still held (both sides, every tick, as the quern's grinding).</summary>
    public bool OnWorkStep(IPlayer byPlayer, float secondsUsed)
    {
        long now = Api.World.ElapsedMilliseconds;
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = now;
            return Complete && (_batch.Charged || secondsUsed < 0.5f);
        }
        if (CanWork() != PanRefusal.None)
        {
            Release(byPlayer);
            return false;
        }
        _holds.Hold(byPlayer.PlayerUID, now);
        UpdateHeld();
        return true;
    }

    public void Release(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = long.MinValue / 2;
            return;
        }
        _holds.Release(byPlayer.PlayerUID);
        UpdateHeld();
    }

    private void UpdateHeld()
    {
        bool held = _holds.Any(Api.World.ElapsedMilliseconds) && CanWork() == PanRefusal.None;
        if (held == _held)
            return;
        _held = held;
        MarkDirty(false);
    }

    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !Complete && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fills the next stage, nothing taken.</summary>
    public bool FitNext()
    {
        if (!Assembly.FillNext())
            return false;
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25);
        MarkDirty(true);
        return true;
    }

    /// <summary>Fits what <paramref name="slot"/> holds into the next stage (server side); false, with
    /// an error to the player, when the rules say no.</summary>
    public bool TryFit(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        if (stack == null)
            return false;
        var fit = Assembly.Fit(stack.Collectible.Code.ToString(), stack.StackSize, stack.Collectible.MaxStackSize);
        switch (fit.Verdict)
        {
            case AssemblyVerdict.Fitted:
                break;
            case AssemblyVerdict.OutOfOrder:
                return Error(byPlayer, "order", NextName());
            case AssemblyVerdict.AlreadyFitted:
                return Error(byPlayer, "part-fitted");
            case AssemblyVerdict.TooFew:
                return Error(byPlayer, "toofew", NextName());
            default:
                return Error(byPlayer, "not-a-part");
        }
        if (byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(fit.Take);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>The next stage's name, as the info and the errors show it.</summary>
    public string NextName() =>
        Assembly.Build.Next is { } next ? Lang.Get(AmalgamPanSystem.Domain + ":amalgampan-info-stage-" + Assembly.Build.Stages[next].Id) : "";

    private bool Liquid(ItemSlot slot, IPlayer byPlayer)
    {
        var held = slot.Itemstack!;
        var content = (held.Collectible as ILiquidSource)?.GetContent(held);
        string? liquid = content?.Collectible?.Code?.ToString();
        if (content == null || liquid == null)
            return TakeMercury(slot, byPlayer);
        string mercury = Recovery?.Retort.Mercury ?? MercuryCode;
        if (liquid == mercury)
        {
            var why = _batch.CanPourMercury(Complete, Config);
            return why != PanRefusal.None ? Refuse(byPlayer, why) : Pour(slot, byPlayer, _batch.MercuryRoom(Config), taken => _batch.AddMercury(taken));
        }
        if (liquid == WaterCode)
        {
            var why = _batch.CanPourWater(Complete, Config);
            if (why == PanRefusal.None && !_batch.Charged)
                why = PanRefusal.NoCharge;
            if (why != PanRefusal.None)
                return Refuse(byPlayer, why);
            float perLitre = BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 100;
            int fit = (int)Math.Ceiling(_batch.WaterRoom(Config) * perLitre - 1e-6);
            return Pour(slot, byPlayer, fit, taken => _batch.AddWater(taken / perLitre));
        }
        return Error(byPlayer, "otherliquid");
    }

    private bool Pour(ItemSlot slot, IPlayer byPlayer, int fit, Action<int> into)
    {
        var held = slot.Itemstack!;
        var source = (ILiquidSource)held.Collectible;
        var content = source.GetContent(held)!;
        fit = Math.Min(fit, content.StackSize);
        if (fit <= 0)
            return false;
        int taken = held.StackSize > 1 && source is BlockLiquidContainerBase container
            ? container.SplitStackAndPerformAction(byPlayer.Entity, slot, one => container.TryTakeContent(one, fit)?.StackSize ?? 0)
            : source.TryTakeContent(held, fit)?.StackSize ?? 0;
        slot.MarkDirty();
        if (taken <= 0)
            return false;
        into(taken);
        (source as BlockLiquidContainerBase)?.DoLiquidMovedEffects(byPlayer, content, taken, BlockLiquidContainerBase.EnumLiquidDirection.Pour);
        MarkDirty(true);
        return true;
    }

    /// <summary>An empty container takes the mercury back out, with no charge in the pan.</summary>
    private bool TakeMercury(ItemSlot slot, IPlayer byPlayer)
    {
        if (slot.Itemstack?.Collectible is not BlockLiquidContainerBase container)
            return false;
        if (_batch.Items > 0)
            return Error(byPlayer, "mercury-charged");
        if (_batch.Mercury <= 0 || Api.World.GetItem(new AssetLocation(Recovery?.Retort.Mercury ?? MercuryCode)) is not { } item)
            return Error(byPlayer, "nomercury-left");
        var held = slot.Itemstack;
        var mercury = new ItemStack(item, _batch.Mercury);
        // half a portion over, so the float litres never round a portion away
        float litres = (_batch.Mercury + 0.5f) / (BlockLiquidContainerBase.GetContainableProps(mercury)?.ItemsPerLitre ?? 100f);
        int moved = held.StackSize > 1
            ? container.SplitStackAndPerformAction(byPlayer.Entity, slot, one => container.TryPutLiquid(one, mercury, litres))
            : container.TryPutLiquid(held, mercury, litres);
        slot.MarkDirty();
        if (moved <= 0)
            return false;
        _batch.TakeMercury(moved);
        container.DoLiquidMovedEffects(byPlayer, mercury, moved, BlockLiquidContainerBase.EnumLiquidDirection.Fill);
        MarkDirty(true);
        return true;
    }

    /// <summary>Loads crushed or ground quartz from <paramref name="slot"/> (server side).</summary>
    public bool TryLoad(ItemSlot slot, IPlayer? byPlayer)
    {
        var held = slot.Itemstack!;
        var (stream, free) = Quartz(held);
        bool amalgamated = stream != null && stream.State.HasFlag(OreState.Amalgamated);
        bool same = _charge == null || _charge.Equals(Api.World, held, GlobalConstants.IgnoredStackAttributes);
        var why = _batch.CanLoad(Complete, free, amalgamated, same, Config);
        if (why != PanRefusal.None)
            return Refuse(byPlayer, why);
        int n = _batch.LoadCount(held.StackSize, Config);
        var taken = slot.TakeOut(n);
        slot.MarkDirty();
        if (_charge == null)
            _charge = taken;
        else
            _charge.StackSize += taken.StackSize;
        _batch.Load(taken.StackSize);
        Api.World.PlaySoundAt(LoadSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl + right-click: an untouched charge comes back out; its water stays in the pan.</summary>
    public bool TakeCharge(IPlayer byPlayer)
    {
        if (_charge == null || _batch.Unload() == 0)
            return false;
        Give(byPlayer, _charge);
        _charge = null;
        Api.World.PlaySoundAt(LoadSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>An empty hand on a worked charge: the amalgam and the worked quartz come out.</summary>
    public bool TakeOut(IPlayer byPlayer)
    {
        if (!_batch.Worked)
            return false;
        if (_amalgam != null)
            Give(byPlayer, _amalgam);
        if (_charge != null)
            Give(byPlayer, _charge);
        _amalgam = null;
        _charge = null;
        _batch.Empty();
        Api.World.PlaySoundAt(LoadSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.8, 0.5));
    }

    private bool Refuse(IPlayer? byPlayer, PanRefusal why) => why switch
    {
        PanRefusal.Incomplete => Error(byPlayer, "incomplete", NextName()),
        PanRefusal.NoMercury => Error(byPlayer, "nomercury", MercuryNeeded(), _batch.Mercury),
        PanRefusal.NoWater => Error(byPlayer, "nowater", Num(Config.WaterLitresPerCharge), Num(_batch.Water)),
        PanRefusal.ChargeFull => Error(byPlayer, "chargefull", Config.ChargeItems),
        _ => Error(byPlayer, why.ToString().ToLowerInvariant()),
    };

    private static string Num(double v) => v.ToString("0.#", global::System.Globalization.CultureInfo.InvariantCulture);

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("amalgampan-" + key, Lang.GetL(sp.LanguageCode, AmalgamPanSystem.Domain + ":amalgampan-error-" + key, args));
        return false;
    }

    // ---- Working ----

    private void OnTick(float dt)
    {
        UpdateHeld();
        double turns = 0;
        if (Power is { } mp && Powered)
        {
            float angle = mp.ShaftAngle;
            if (!float.IsNaN(_lastAngle))
                turns = ShaftClock.AngleAdvance(_lastAngle, angle, mp.Network!.Speed * mp.GearedRatio, dt) / (2 * Math.PI);
            _lastAngle = angle;
        }
        else
        {
            _lastAngle = float.NaN;
            if (_held)
                turns = Config.HandTurnsPerSecond * dt;
        }
        if (turns > 0 && CanWork() == PanRefusal.None)
            Work(turns);
    }

    /// <summary>Works the charge by <paramref name="turns"/> of the muller (server side): at the
    /// charge's turns it is done, the amalgam made and the quartz amalgamated. Returns the amalgam
    /// items made when it finished, else 0.</summary>
    public int Work(double turns)
    {
        if (CanWork() != PanRefusal.None)
            return 0;
        double before = _batch.Progress(Config);
        if (!_batch.Advance(turns, Config))
        {
            if (Math.Floor(before / SyncStep) != Math.Floor(_batch.Progress(Config) / SyncStep))
                MarkDirty(false);
            return 0;
        }
        return Finish();
    }

    private int Finish()
    {
        if (Pass() is not { } pass || Recovery is not { } recovery || _charge == null)
            return 0;
        string code = OreProducts.AmalgamCode(pass.Ore);
        int items = _carry.Add(code, pass.Units, recovery.ConcentrateUnits);
        _batch.Finish(items, AmalgamMercury);
        OreStreamAttributes.Write(_charge, pass.After);
        _amalgam = items > 0 && Api.World.GetItem(new AssetLocation(code)) is { } amalgam ? new ItemStack(amalgam, items) : null;
        _holds.Clear();
        _held = false;
        var at = MachinePoints.WorldPoint(Pos, System.Rig?.Pan ?? new Float3(0.5f, 0.4f, 0.5f), Side);
        Api.World.PlaySoundAt(LoadSound, at.X, at.Y, at.Z);
        MarkDirty(true);
        return items;
    }

    // The client's own: a working pan grinds.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockAmalgamPan)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        bool working = (Powered || HandHeld) && _batch.Charged && Complete;
        if (!working)
        {
            _grindSeconds = 0;
            return;
        }
        if ((_grindSeconds -= dt) <= 0)
        {
            _grindSeconds = 1.5f;
            var at = MachinePoints.WorldPoint(Pos, System.Rig?.Pan ?? new Float3(0.5f, 0.4f, 0.5f), Side);
            Api.World.PlaySoundAt(GrindSound, at.X, at.Y, at.Z, null, true, 12, 0.4f);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the pan gives besides the frame: the muller and shoes, the quartz and any
    /// amalgam. The mercury and the water run out and are lost.</summary>
    public IEnumerable<ItemStack> BreakDrops()
    {
        foreach (var drop in Assembly.Returns())
            if (StackOf(Api.World, drop.Code) is { } stack)
            {
                stack.StackSize = drop.Count;
                yield return stack;
            }
        if (_charge != null)
            yield return _charge.Clone();
        if (_amalgam != null)
            yield return _amalgam.Clone();
    }

    /// <summary>One of an item or block by its code; null when the game has neither.</summary>
    public static ItemStack? StackOf(IWorldAccessor world, string code)
    {
        var loc = new AssetLocation(code);
        if (world.GetItem(loc) is { } item)
            return new ItemStack(item);
        return world.GetBlock(loc) is { Id: > 0 } block ? new ItemStack(block) : null;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        DisposeRenderer();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        DisposeRenderer();
    }

    private void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var build = new TreeAttribute();
        foreach (var (key, count) in Assembly.Build.Snapshot())
            build.SetInt(key, count);
        tree[BuildKey] = build;
        tree.SetInt("mercury", _batch.Mercury);
        tree.SetInt("items", _batch.Items);
        tree.SetDouble("water", _batch.Water);
        tree.SetDouble("turns", _batch.Turns);
        tree.SetBool("worked", _batch.Worked);
        SetStack(tree, "charge", _charge);
        SetStack(tree, "amalgam", _amalgam);
        var carry = new TreeAttribute();
        foreach (var (code, units) in _carry.Held)
            carry.SetDouble(code, units);
        tree["carry"] = carry;
        tree.SetBool("held", _held);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetInt("need", MercuryNeeded());
    }

    private static void SetStack(ITreeAttribute tree, string key, ItemStack? stack)
    {
        if (stack != null)
            tree.SetItemstack(key, stack);
        else
            tree.RemoveAttribute(key);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var snapshot = new Dictionary<string, int>();
        if (tree[BuildKey] is ITreeAttribute build)
            foreach (var (key, value) in build)
                if (value is IntAttribute n)
                    snapshot[key] = n.value;
        var spec = AmalgamPanSystem.Of(worldForResolving.Api).Build
                   ?? new MachineBuild(AmalgamPanSystem.MachineId, null, FeedMode.Batch, [], []);
        _assembly = MachineAssembly.Restore(spec, snapshot, new Dictionary<string, (string Code, double Left)>(), out _);
        _charge = Resolve(tree.GetItemstack("charge"), worldForResolving);
        _amalgam = Resolve(tree.GetItemstack("amalgam"), worldForResolving);
        _batch = AmalgamPanBatch.Restore(tree.GetInt("mercury"), _charge?.StackSize ?? 0, tree.GetDouble("water"),
            tree.GetDouble("turns"), tree.GetBool("worked"));
        var carry = new Dictionary<string, double>();
        if (tree["carry"] is ITreeAttribute held)
            foreach (var (code, value) in held)
                if (value is DoubleAttribute d)
                    carry[code] = d.value;
        _carry.Restore(carry);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _held = tree.GetBool("held");
            _syncedNeed = tree.GetInt("need");
        }
    }

    private static ItemStack? Resolve(ItemStack? stack, IWorldAccessor world) =>
        stack != null && stack.ResolveBlockOrItem(world) ? stack : null;

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        foreach (var stack in new[] { _charge, _amalgam })
            stack?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(stack), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _charge?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
        _amalgam?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(AmalgamPanSystem.Domain + ":amalgampan-" + key, args);
        if (!Complete)
        {
            dsc.AppendLine(L("info-next", NextName()));
            return;
        }
        dsc.AppendLine(L("info-mercury", _batch.Mercury, Config.MercuryCapacityPortions));
        if (_batch.Worked)
        {
            dsc.AppendLine(L("info-worked", _amalgam?.StackSize ?? 0));
            return;
        }
        if (!_batch.Charged)
        {
            dsc.AppendLine(L("info-empty", Config.ChargeItems));
            return;
        }
        dsc.AppendLine(L("info-charge", _batch.Items, _charge?.GetName() ?? ""));
        dsc.AppendLine(L("info-water", Num(_batch.Water), Num(Config.WaterLitresPerCharge)));
        int need = MercuryNeeded();
        if (need > 0)
            dsc.AppendLine(L("info-needs", need));
        int percent = (int)Math.Floor(_batch.Progress(Config) * 100 + 1e-9);
        var why = CanWork();
        dsc.AppendLine(why != PanRefusal.None ? L("info-waiting", percent)
            : Working ? L("info-working", percent)
            : L("info-ready", percent));
    }
}
