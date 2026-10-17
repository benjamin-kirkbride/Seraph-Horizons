using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rocker.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Rocker;

/// <summary>
/// The rocker (#716): the charge in its hopper, the item being washed, what lies behind its riffles and
/// the units carried between items (<see cref="RockerState"/>), and who rocks it. The player tips crushed
/// or ground ore, or placer gravel, into the hopper and works it by holding right-click on it with water
/// in a bucket, as on the quern: the server rocks it while held, pours the bucket into the hopper as it
/// does, puts what each washed item gives behind the riffles and drops the tailings in the cell beyond
/// the foot (#694). The concentrate comes out by hand, an empty hand's right-click. No automated input
/// or output: it has no inventory any hopper or chute can reach. The client draws it
/// (<see cref="RockerRenderer"/>).
/// </summary>
public class BERocker : BlockEntity
{
    private static readonly AssetLocation PourSound = new("game", "sounds/effect/water-pour");
    private static readonly AssetLocation LoadSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation TakeSound = new("game", "sounds/block/sand");
    // How long after its last step a player's hold counts (the game steps every tick while held).
    private const long HoldTimeoutMs = 400;
    // A client's own player holding right-click counts as rocking for this long after its last step.
    private const long LocalHoldMs = 250;
    // The server syncs the washing at least this often (of an item).
    private const double SyncStep = 0.1;

    private readonly RockerState _state = new();
    private ItemStack? _charge;
    private TailingsOwed _tailings;
    private double _litresOwed;
    private string? _holder;
    private long _heldAt;
    private bool _held;
    private long _localHeldAt = long.MinValue / 2;
    private float _pourSeconds;
    private float? _serverRocksPerSecond;
    private Cuboidf[]? _boxes;
    private Cuboidf[]? _collision;
    private RockerRenderer? _renderer;

    public Side Side { get; private set; } = Side.South;
    public RockerState State => _state;
    /// <summary>One item of the charge in the hopper (its kind), or null.</summary>
    public ItemStack? Charge => _state.HasCharge ? _charge : null;
    /// <summary>The code of what lies behind the riffles (the most of it), for the renderer; or null.</summary>
    public string? CatchCode => _state.Riffles.OrderByDescending(r => r.Value).Select(r => r.Key).FirstOrDefault();

    /// <summary>Someone rocks it (the server's word, or this client's own player holding right-click).</summary>
    public bool Held =>
        _held || Api is { Side: EnumAppSide.Client } && Api.World.ElapsedMilliseconds - _localHeldAt <= LocalHoldMs;

    public float RocksPerSecond => _serverRocksPerSecond ?? Config.RocksPerSecond;

    public bool CanTakeMoreCharge => !_state.HasCharge || _state.ChargeCount < Config.ChargeItems;

    private RockerSystem System => RockerSystem.Of(Api);
    private RockerConfig Config => System.Config;
    public RockerRig? Rig => System.Rig;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.South;
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnTick, 50);
        else
        {
            RegisterGameTickListener(OnClientTick, 100);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockRocker)
                _renderer = new RockerRenderer(capi, this, rig);
        }
    }

    /// <summary>A native-frame point as a world position.</summary>
    public Vec3d WorldPoint(Float3 native) => MachinePoints.WorldPoint(Pos, native, Side);

    public Cuboidf[]? SelectionBoxes => _boxes;
    public Cuboidf[]? CollisionBoxes => _collision;

    private void BuildBoxes()
    {
        if (Rig is not { } rig)
            return;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, Side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        _boxes = rig.Cell.Boxes.Count == 0 ? [Cuboidf.Default()] : rig.Cell.Boxes.Select(World).ToArray();
        _collision = rig.Cell.LidBox is { } lid ? [.. _boxes, World(lid)] : _boxes;
    }

    /// <summary>The world position of the cell the tailings fall into, beyond the open foot.</summary>
    public BlockPos TailingsPos()
    {
        var local = Rig?.TailingsCell ?? new Int3(-1, 0, 0);
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the rocker. With crushed or ground ore or placer gravel in hand, tips as much of it
    /// as the hopper holds in (one kind at a time). With a liquid container in hand, starts rocking:
    /// held, it rocks while water is poured (<see cref="OnWorkStep"/>). With an empty hand, takes
    /// everything from behind the riffles; Ctrl and an empty hand takes the charge back while untouched.
    /// Shift lets a held block be placed against it. Decided and done on the server; the client says
    /// whether the click is the rocker's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        var controls = byPlayer.Entity.Controls;
        bool server = Api.Side == EnumAppSide.Server;
        if (controls.ShiftKey)
            return false;
        if (held == null)
        {
            if (!server)
                return _state.HasCatch || _state.HasCharge;
            if (controls.CtrlKey)
                return TakeCharge(byPlayer);
            if (_state.HasCatch)
                return TakeCatch(byPlayer);
            return _state.HasCharge ? Error(byPlayer, "error-need-water") : Error(byPlayer, "error-empty");
        }
        if (RockerFeed.Parse(held.Collectible.Code.ToString()) != null)
            return !server || TryLoad(slot!, byPlayer);
        if (RockerWater.IsContainer(held))
        {
            if (!server)
                return true;
            if (_state.Stop(RockerWater.Litres(held), Config) is var stop and not RockerStop.None)
                return Error(byPlayer, stop switch
                {
                    RockerStop.NoCharge => "error-no-charge",
                    RockerStop.RifflesFull => "error-riffles-full",
                    _ => "error-no-water",
                });
            Hold(byPlayer);
            return true;
        }
        return false;
    }

    /// <summary>Right-click still held (both sides, every tick): whether the player goes on rocking.</summary>
    public bool OnWorkStep(IPlayer byPlayer, float secondsUsed)
    {
        var held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        if (!RockerWater.IsContainer(held))
            return false;
        if (Api.Side == EnumAppSide.Client)
        {
            bool go = _state.HasCharge && RockerWater.Litres(held) > 0 || secondsUsed < 0.5f;
            if (go)
                _localHeldAt = Api.World.ElapsedMilliseconds;
            return go;
        }
        if (_state.Stop(RockerWater.Litres(held) - _litresOwed, Config) != RockerStop.None)
        {
            Release(byPlayer);
            return false;
        }
        Hold(byPlayer);
        return true;
    }

    /// <summary>The player let go.</summary>
    public void Release(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = long.MinValue / 2;
            return;
        }
        if (_holder == byPlayer.PlayerUID)
            _holder = null;
        UpdateHeld();
    }

    private void Hold(IPlayer byPlayer)
    {
        _holder = byPlayer.PlayerUID;
        _heldAt = Api.World.ElapsedMilliseconds;
        UpdateHeld();
    }

    private IPlayer? Holder()
    {
        if (_holder == null || Api.World.ElapsedMilliseconds - _heldAt > HoldTimeoutMs)
            return null;
        return Api.World.PlayerByUid(_holder);
    }

    private void UpdateHeld()
    {
        bool held = Holder() != null && _state.HasCharge;
        if (held == _held)
            return;
        _held = held;
        if (!held)
            _holder = null;
        MarkDirty(false);
    }

    /// <summary>The key a charge's kind is held by: its code and the ore's state on the stack (riddled,
    /// ground, amalgamated, its units), so items that would not stack do not share a charge.</summary>
    public static string KindKey(ItemStack stack) =>
        stack.Collectible.Code + "|" + (stack.Attributes is TreeAttribute tree && tree.Count > 0 ? tree.ToJsonToken() : "");

    /// <summary>Tips as much of the held feed as the hopper takes (server side).</summary>
    public bool TryLoad(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        if (stack == null)
            return false;
        switch (_state.CanLoad(KindKey(stack), RockerFeed.Parse(stack.Collectible.Code.ToString()) != null, Config, out int room))
        {
            case RockerLoadVerdict.Loads:
                break;
            case RockerLoadVerdict.OtherFeed:
                return Error(byPlayer, "error-other-charge");
            case RockerLoadVerdict.Full:
                return Error(byPlayer, "error-hopper-full");
            default:
                return false;
        }
        int n = _state.Load(KindKey(stack), Math.Min(room, stack.StackSize), Config);
        if (n <= 0)
            return false;
        if (_charge == null || _state.ChargeCount == n)
        {
            _charge = stack.Clone();
            _charge.StackSize = 1;
        }
        slot.TakeOut(n);
        slot.MarkDirty();
        var at = WorldPoint(Rig?.Pour.Pos ?? new Float3(0.5f, 0.6f, 0.5f));
        Api.World.PlaySoundAt(LoadSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl and an empty hand (server side): the charge comes back while untouched.</summary>
    public bool TakeCharge(IPlayer byPlayer)
    {
        if (!_state.HasCharge)
            return Error(byPlayer, "error-empty");
        if (!_state.ChargeUntouched)
            return Error(byPlayer, "error-busy");
        var stack = _charge!.Clone();
        stack.StackSize = _state.Unload();
        _charge = null;
        Give(byPlayer, stack);
        MarkDirty(true);
        return true;
    }

    /// <summary>An empty hand (server side): everything behind the riffles comes out.</summary>
    public bool TakeCatch(IPlayer byPlayer)
    {
        var caught = _state.TakeCatch();
        foreach (var stack in Stacks(caught))
            Give(byPlayer, stack);
        var at = WorldPoint(Rig?.Concentrate.Pos ?? new Float3(0.4f, 0.3f, 0.5f));
        Api.World.PlaySoundAt(TakeSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
        return true;
    }

    private IEnumerable<ItemStack> Stacks(IReadOnlyDictionary<string, int> counts)
    {
        foreach (var (code, count) in counts)
        {
            var loc = new AssetLocation(code);
            CollectibleObject? c = Api.World.GetItem(loc) ?? (CollectibleObject?)Api.World.GetBlock(loc);
            if (c == null || c.IsMissing || c.Id == 0)
            {
                Api.Logger.Warning("[seraphhorizons] Rocker: no {0} in this world; {1} of it lost", code, count);
                continue;
            }
            int max = Math.Max(1, c.MaxStackSize);
            for (int left = count; left > 0; left -= max)
                yield return new ItemStack(c, Math.Min(max, left));
        }
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("rocker-" + key, Lang.GetL(sp.LanguageCode, RockerSystem.Domain + ":rocker-" + key, args));
        return false;
    }

    // ---- Rocking ----

    private void OnTick(float dt)
    {
        UpdateHeld();
        if (_held && Holder() is { } player)
            Rock(player, Config.RocksPerSecond * dt);
    }

    /// <summary>
    /// Rocks it by up to <paramref name="rocks"/> with the water in <paramref name="player"/>'s held
    /// container (server side): the water poured is taken out of it, each item washed through puts what
    /// it gives behind the riffles, and its tailings fall beyond the foot. Returns the step.
    /// </summary>
    public RockStep Rock(IPlayer player, double rocks)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        double available = RockerWater.Litres(slot?.Itemstack) - _litresOwed;
        var template = _charge;
        if (slot == null || template == null)
            return RockStep.None;
        double before = _state.ItemWork;
        int countBefore = _state.ChargeCount;
        var step = _state.Rock(rocks, available, Config, () => System.YieldOf(template));
        if (step.Litres > 0)
        {
            double perLitre = RockerWater.PortionsPerLitre(slot.Itemstack);
            _litresOwed += step.Litres;
            int portions = (int)Math.Floor(_litresOwed * perLitre + 1e-9);
            if (portions > 0)
                _litresOwed -= RockerWater.Take(player, slot, portions) / perLitre;
        }
        if (step.Washed > 0)
            Discard(template, step.Washed);
        if (!_state.HasCharge)
            _charge = null;
        if (step.Washed > 0 || step.Caught.Count > 0 || !_state.HasCharge)
            MarkDirty(true);
        else if (countBefore != _state.ChargeCount || Math.Floor(before / SyncStep) != Math.Floor(_state.ItemWork / SyncStep))
            MarkDirty(false);
        UpdateHeld();
        return step;
    }

    // The tailings of the items washed: the rocker's rule (#694), in the feed's rock, into the cell beyond the foot.
    private void Discard(ItemStack template, int washed)
    {
        if (OreMachinesSystem.Of(Api).Config.TailingsFor("rocker") is not { } rule)
            return;
        (int whole, _tailings) = _tailings.Add(washed, rule);
        if (whole > 0)
            Tailings.Discard(Api.World, TailingsPos(), rule.Form, Tailings.RockOf(template), whole);
    }

    // The client's own: a worked rocker splashes and pours; one removed under it goes.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockRocker)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Held || !_state.HasCharge)
        {
            _pourSeconds = 0;
            return;
        }
        if ((_pourSeconds -= dt) <= 0)
        {
            _pourSeconds = 1.8f;
            var at = WorldPoint(rig.Pour.Pos);
            capi.World.PlaySoundAt(PourSound, at.X, at.Y, at.Z, null, true, 12, 0.4f);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking it gives besides the rocker: the charge while untouched, and everything
    /// behind the riffles (an item half washed is lost).</summary>
    public IEnumerable<ItemStack> ContentDrops()
    {
        if (_state.HasCharge && _charge != null)
        {
            int n = _state.ChargeCount - (_state.ChargeUntouched ? 0 : 1);
            if (n > 0)
            {
                var stack = _charge.Clone();
                stack.StackSize = n;
                yield return stack;
            }
        }
        foreach (var stack in Stacks(_state.Riffles))
            yield return stack;
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
        if (_state.HasCharge && _charge != null)
        {
            tree.SetItemstack("charge", _charge);
            tree.SetString("chargeKey", _state.ChargeKey);
            tree.SetInt("chargeCount", _state.ChargeCount);
            tree.SetDouble("itemWork", _state.ItemWork);
        }
        else
        {
            tree.RemoveAttribute("charge");
            tree.RemoveAttribute("chargeKey");
            tree.SetInt("chargeCount", 0);
            tree.SetDouble("itemWork", 0);
        }
        var riffles = new TreeAttribute();
        foreach (var (code, n) in _state.Riffles)
            riffles.SetInt(code, n);
        tree["riffles"] = riffles;
        var carry = new TreeAttribute();
        foreach (var (code, units) in _state.Carry.Held)
            carry.SetDouble(code, units);
        tree["carry"] = carry;
        tree.SetDouble("tailings", _tailings.Layers);
        tree.SetDouble("litresOwed", _litresOwed);
        tree.SetBool("held", _held);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetFloat("rocksPerSecond", Config.RocksPerSecond);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _charge = tree.GetItemstack("charge");
        if (_charge != null && !_charge.ResolveBlockOrItem(worldForResolving))
            _charge = null;
        var riffles = (tree["riffles"] as ITreeAttribute)?.Select(kv => new KeyValuePair<string, int>(kv.Key, (kv.Value as IntAttribute)?.value ?? 0))
                      ?? [];
        var carry = (tree["carry"] as ITreeAttribute)?.Select(kv => new KeyValuePair<string, double>(kv.Key, (kv.Value as DoubleAttribute)?.value ?? 0))
                    ?? [];
        _state.Restore(_charge == null ? null : tree.GetString("chargeKey") ?? KindKey(_charge), tree.GetInt("chargeCount"),
            tree.GetDouble("itemWork"), riffles.ToList(), carry.ToList());
        if (!_state.HasCharge)
            _charge = null;
        _tailings = TailingsOwed.Restore(tree.GetDouble("tailings"));
        _litresOwed = Math.Clamp(tree.GetDouble("litresOwed"), 0, 1);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _held = tree.GetBool("held");
            _serverRocksPerSecond = tree.TryGetFloat("rocksPerSecond");
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _charge?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_charge), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _charge?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(RockerSystem.Domain + ":rocker-" + key, args);
        if (Charge is { } charge)
        {
            int percent = (int)Math.Floor(_state.ItemWork * 100 + 1e-9);
            dsc.AppendLine(L(Held ? "info-rocking" : "info-charge", _state.ChargeCount, charge.GetName(), percent));
        }
        else
            dsc.AppendLine(L("info-empty"));
        if (_state.HasCatch)
        {
            var names = _state.Riffles.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r =>
            {
                var loc = new AssetLocation(r.Key);
                CollectibleObject? c = Api.World.GetItem(loc) ?? (CollectibleObject?)Api.World.GetBlock(loc);
                return $"{r.Value} {(c == null ? r.Key : new ItemStack(c).GetName())}";
            });
            dsc.AppendLine(L("info-riffles", string.Join(", ", names)));
            if (_state.RiffleCount >= Config.RiffleItems)
                dsc.AppendLine(L("info-riffles-full"));
        }
    }
}
