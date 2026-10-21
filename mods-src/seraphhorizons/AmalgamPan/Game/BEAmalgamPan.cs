using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.AmalgamPan.Core;
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

namespace SeraphHorizons.Mod.AmalgamPan;

/// <summary>
/// The amalgam pan's controller (#718), on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/amalgampan.json</c>, the muller then its shoes; its axle, its block info, its renderer).
/// It keeps the batch (<see cref="AmalgamPanBatch"/>: mercury, the charge's count, water, turns), the charge's
/// quartz as its item stack, a worked charge's amalgam waiting to be taken out, and the fraction of an
/// amalgam item carried to the next charge (<see cref="UnitCarry"/>). The muller turns by hand while a player
/// holds right-click (<see cref="BEMachine.HandWorked"/>), as the quern's stone, or from an axle in the block
/// below through the shared <see cref="BEBehaviorMachineMP"/>, with no player; either way a charge with its
/// water and mercury is worked a turn at a time, and when done ore processing's recovery
/// (<see cref="OreRecovery.Amalgamate"/>) says what amalgam it makes and what the quartz carries on
/// (amalgamated, so the rocker no longer loses its free metal at × 0.7). It takes no oil. Nothing goes in or
/// out but by hand: it has no inventory a hopper or chute can reach.
/// </summary>
public class BEAmalgamPan : BEMachine
{
    /// <summary>Expanded Matter's mercury, as the still's retort names it (<c>retort.mercury</c>).</summary>
    public const string MercuryCode = "em:mercuryportion";
    private const string WaterCode = "game:waterportion";
    private static readonly AssetLocation LoadSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation GrindSound = new("game", "sounds/block/quern");
    // The server syncs the turns at least this often (a share of the charge).
    private const double SyncStep = 0.05;
    private static readonly Float3 PanFallback = new(0.5f, 0.4f, 0.5f);
    private const float FleckInterval = 0.25f;

    private AmalgamPanBatch _batch = new();
    private ItemStack? _charge;
    private ItemStack? _amalgam;
    private readonly UnitCarry _carry = new();
    private float _grindSeconds;
    private int _syncedNeed;
    private readonly AmalgamPanClock _clock = new();
    private readonly ParticleTimer _flecks = new(FleckInterval);

    public AmalgamPanBatch Batch => _batch;
    public ItemStack? Charge => _charge;
    public ItemStack? Amalgam => _amalgam;
    public AmalgamPanConfig Config => System.Config;

    private AmalgamPanSystem System => AmalgamPanSystem.Of(Api);
    private new AmalgamPanRig? Rig => base.Rig as AmalgamPanRig;

    /// <summary>An axle below turns the pan (at the old threshold, any turning at all: <see cref="ConfigMinSpeed"/>).</summary>
    public bool ShaftTurning => Powered && MachineRun.Turning(ShaftSpeed, MinSpeed);

    /// <summary>The muller is turning on a charge it can work: the pan is working.</summary>
    public bool Working => Running;

    /// <summary>The shoes' metal (<c>iron</c>, <c>steel</c>) for the renderer, or null.</summary>
    public string? ShoeMetal
    {
        get
        {
            foreach (var drop in Assembly?.Returns() ?? [])
                if (drop.Code.StartsWith("game:metalplate-", StringComparison.Ordinal))
                    return drop.Code["game:metalplate-".Length..];
            return null;
        }
    }

    // Worked by hand or by its axle: the base's running rules for a hand machine that also takes a shaft.
    protected override bool HandWorked => true;
    protected override float ConfigMinSpeed => 0.001f;
    protected override int ClientTickMs => 100;
    public override int RenderRange => 48;

    /// <summary>A charge it can work now: with its water and its amalgam's mercury.</summary>
    public override bool HasWork => CanWork() == PanRefusal.None;
    public override double? WorkProgress => _batch.Charged && !_batch.Worked ? _batch.Progress(Config) : null;

    /// <summary>Its own load, whether it is built or not: idle, or working on a charge it can work.</summary>
    protected override bool LoadsShaft => true;
    protected override double ShaftLoad => Built && HasWork ? Config.WorkingResistance : Config.IdleResistance;

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

    public PanRefusal CanWork() => _batch.CanWork(Built, MercuryNeeded(), Config);

    // ---- Interaction ----

    /// <summary>The pan's own right-click, before its build's: Shift lets a held block be placed against it;
    /// Ctrl with an empty hand (but for the creative shortcut) takes back an untouched charge's quartz.</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (controls is { CtrlKey: true, ShiftKey: false } && slot.Itemstack == null && _batch.Charged && _batch.Untouched
            && !CreativeShortcut(byPlayer))
            return Api.Side != EnumAppSide.Server || TakeCharge(byPlayer);
        return controls.ShiftKey ? false : null;
    }

    /// <summary>
    /// The pan's own right-click, after its build's. A container of mercury pours it in, of water pours the
    /// charge's water, an empty one takes the mercury back out (no charge in). Crushed or ground gold or
    /// silver quartz is loaded. With a worked charge, an empty hand takes out the amalgam and the quartz.
    /// Otherwise, on a built pan with a charge it can work, right-click held turns the muller
    /// (<see cref="BEMachine.OnInteractStep"/>).
    /// </summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var held = slot.Itemstack;
        string? code = held?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        if (held?.Collectible is ILiquidSource or BlockLiquidContainerBase)
            return !server || Liquid(slot, byPlayer);
        if (held != null && Quartz(held).Stream != null)
            return !server || TryLoad(slot, byPlayer);
        if (_batch.Worked)
            return !server || (held == null ? TakeOut(byPlayer) : Error(byPlayer, "worked"));
        if (!Built)
            return code == null && (!server || Error(byPlayer, "incomplete", NextName()));
        if (!_batch.Charged)
            return held == null && (!server || Error(byPlayer, "nocharge"));
        if (!server)
            return true;
        var why = CanWork();
        return why != PanRefusal.None ? Refuse(byPlayer, why) : StartHold(byPlayer);
    }

    /// <summary>The next stage's name, as the errors show it.</summary>
    public string NextName() =>
        Assembly is { } a && a.Build.Next is { } next ? MachineText.StageName(Machine, a.Build.Stages[next]) : "";

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
            var why = _batch.CanPourMercury(Built, Config);
            return why != PanRefusal.None ? Refuse(byPlayer, why) : Pour(slot, byPlayer, _batch.MercuryRoom(Config), taken => _batch.AddMercury(taken));
        }
        if (liquid == WaterCode)
        {
            var why = _batch.CanPourWater(Built, Config);
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
        var why = _batch.CanLoad(Built, free, amalgamated, same, Config);
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

    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The pan's own message (<c>seraphhorizons:amalgampan-error-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("amalgampan-" + key, Lang.GetL(sp.LanguageCode, AmalgamPanSystem.Domain + ":amalgampan-error-" + key, args));
        return false;
    }

    /// <summary>The help once built: a worked charge comes out; else mercury, quartz and its water go in, the
    /// muller is turned, and Ctrl takes an untouched charge back.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => AmalgamPanSystem.Domain + ":blockhelp-amalgampan-" + action;
        if (_batch.Worked)
        {
            help.Add(new WorldInteraction { ActionLangCode = Key("takeout"), MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
            return;
        }
        if (LiquidHelp(Api.World, Recovery?.Retort.Mercury ?? MercuryCode) is { Length: > 0 } mercury)
            help.Add(new WorldInteraction { ActionLangCode = Key("mercury"), MouseButton = EnumMouseButton.Right, Itemstacks = mercury });
        if (_batch.Items < Config.ChargeItems && _batch.Untouched)
            help.Add(new WorldInteraction { ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right });
        if (_batch.Items > 0 && _batch.WaterRoom(Config) > 0 && LiquidHelp(Api.World, WaterCode) is { Length: > 0 } water)
            help.Add(new WorldInteraction { ActionLangCode = Key("water"), MouseButton = EnumMouseButton.Right, Itemstacks = water });
        if (_batch.Items > 0)
            help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
        if (_batch.Items > 0 && _batch.Untouched)
            help.Add(new WorldInteraction { ActionLangCode = Key("takecharge"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    /// <summary>A bucket of the liquid, for the interaction help.</summary>
    private static ItemStack[] LiquidHelp(IWorldAccessor world, string liquid)
    {
        if (world.GetBlock(new AssetLocation("game:woodbucket")) is not BlockLiquidContainerBase bucket
            || world.GetItem(new AssetLocation(liquid)) is not { } item)
            return [];
        var stack = new ItemStack(bucket);
        bucket.SetContent(stack, new ItemStack(item, 1000));
        return [stack];
    }

    // ---- Working ----

    /// <summary>The axle's turn: the radians it advanced, in turns of the muller.</summary>
    protected override bool Work(double radians)
    {
        Turn(radians / (2 * Math.PI));
        return false;
    }

    /// <summary>By hand, with no axle turning it: the hand's pace.</summary>
    protected override bool WorkUnpowered(float dt)
    {
        Turn(Config.HandTurnsPerSecond * dt);
        return false;
    }

    /// <summary>Works the charge by <paramref name="turns"/> of the muller (server side): at the
    /// charge's turns it is done, the amalgam made and the quartz amalgamated. Returns the amalgam
    /// items made when it finished, else 0.</summary>
    public int Turn(double turns)
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
        var at = WorldPoint(Rig?.Pan ?? PanFallback);
        Api.World.PlaySoundAt(LoadSound, at.X, at.Y, at.Z);
        MarkDirty(true);
        RefreshState();
        return items;
    }

    // A turning pan grinds.
    protected override void OnClientTick(float dt)
    {
        bool turning = (ShaftTurning || Held) && _batch.Charged && Built;
        if (!turning)
        {
            _grindSeconds = 0;
            return;
        }
        if ((_grindSeconds -= dt) <= 0)
        {
            _grindSeconds = 1.5f;
            var at = WorldPoint(Rig?.Pan ?? PanFallback);
            Api.World.PlaySoundAt(GrindSound, at.X, at.Y, at.Z, null, true, 12, 0.4f);
        }
    }

    // ---- The renderer's ----

    /// <summary>The muller and the shoes as their stages are fitted; one of the contents as the batch stands,
    /// the water film over the charge, on a built pan.</summary>
    public override bool ShowsPart(string? requires) =>
        AmalgamPanRequires.IsContents(requires) ? Built && _batch.Shows(requires) : base.ShowsPart(requires);

    /// <summary>The shoes' texture follows their plate's metal.</summary>
    public override string TextureKey => ShoeMetal ?? "";

    /// <summary>The block's textures with the shoes' metal in place of <c>shoe</c>: the game's plate texture,
    /// inserted in the block atlas if it is not there yet (the shoes are cut from a plate).</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new ShoeTextureSource(base.PartTextures(capi), PlateTexture(capi, ShoeMetal));

    private static TextureAtlasPosition? PlateTexture(ICoreClientAPI capi, string? metal)
    {
        if (metal == null)
            return null;
        var loc = new AssetLocation("game", "block/metal/plate/" + metal);
        var pos = capi.BlockTextureAtlas[loc];
        if (pos == null && capi.Assets.Exists(loc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
            capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out pos);
        return pos;
    }

    /// <summary>θ: the axle's angle while one turns the pan (the quern's top stone takes it as it is), else the
    /// hand's clock while held on a built pan (<see cref="AmalgamPanClock"/>). While it works a charge, flecks of
    /// the slurry (the quartz's colour) thrown up round the muller.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        _clock.Advance(dt, ShaftTurning ? ShaftAngle : null, Held && Built, Config.HandTurnsPerSecond);
        if (!far && Running && _flecks.Due(dt))
        {
            int colour = MachineParticles.MaterialColour(_charge, 200, MachineParticles.Slurry, Api as ICoreClientAPI);
            (MachineParticles.SlurryDrops(colour) with
            {
                Count = 1, MaxCount = 2, MinVelocity = new Vec3f(-0.3f, 0.3f, -0.3f), MaxVelocity = new Vec3f(0.3f, 0.7f, 0.3f), Life = 0.6f, Size = 0.12f,
            }).Spawn(Api.World, WorldPoint(Rig?.Pan ?? PanFallback), MachineParticles.Around(0.25, 0.02, 0.25));
        }
        return _clock.Input();
    }

    /// <summary>The block's textures with the shoes' metal in place of <c>shoe</c>.</summary>
    private sealed class ShoeTextureSource(ITexPositionSource inner, TextureAtlasPosition? shoe) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "shoe" && shoe != null ? shoe : inner[textureCode]!;
    }

    // ---- Breaking ----

    /// <summary>What breaking the pan gives besides the frame: the muller and shoes, the quartz and any
    /// amalgam. The mercury and the water run out and are lost.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (_charge != null)
            yield return _charge.Clone();
        if (_amalgam != null)
            yield return _amalgam.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
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
            _syncedNeed = tree.GetInt("need");
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

    /// <summary>The mercury, then a worked charge's amalgam, or the charge, its water and the mercury its
    /// amalgam takes, and while it cannot be worked how far it is (the state says it is not working).</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built)
            return;
        string L(string key, params object[] args) => Lang.Get(AmalgamPanSystem.Domain + ":amalgampan-" + key, args);
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
        if (CanWork() != PanRefusal.None)
            dsc.AppendLine(L("info-waiting", (int)Math.Floor(_batch.Progress(Config) * 100 + 1e-9)));
    }

    /// <summary>For a wrench: the muller's turns on the charge, of the turns it takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (_batch.Charged && !_batch.Worked)
            dsc.AppendLine(Lang.Get(AmalgamPanSystem.Domain + ":amalgampan-info-turns",
                _batch.Turns.ToString("0.##", CultureInfo.InvariantCulture), Config.TurnsFor(_batch.Items).ToString("0.##", CultureInfo.InvariantCulture)));
    }
}
