using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// A melting hole of the crucible furnace (README "Crucible furnace"). It holds a melting pot (its
/// heats, and its charge until that melts), the coke around it, its fire's temperature and whether it
/// is lit; the lid is the block's variant. The server ticks it: it finds the hole's row and chimney
/// (<see cref="FurnaceRow"/>), draws forced air from a pipe next to the row, advances the fire
/// (<see cref="FurnaceHeat"/>) by the calendar's hours since the last tick, and melts the charge once
/// the fire is at its recipe's temperature. A player works it by right-clicks (<see cref="OnInteract"/>)
/// and lights it as a forge is lit (<see cref="BlockMeltingHole"/>).
/// </summary>
public class BEMeltingHole : BlockEntity
{
    private static readonly AssetLocation LidSound = new("game", "sounds/block/hatch");
    private static readonly AssetLocation CokeSound = new("game", "sounds/block/charcoal");
    private static readonly AssetLocation PotSound = new("game", "sounds/block/ceramicplace");
    private static readonly AssetLocation ChargeSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation CrackSound = new("game", "sounds/block/ceramicbreak");
    private static readonly AssetLocation IgniteSound = new("game", "sounds/torch-ignite");

    private bool _pot;
    private int _heats;
    private List<ChargeItem> _charge = [];
    private string? _melted;
    private int _meltedUnits;
    private HoleHeat _heat = new(FurnaceHeat.Ambient, 0, false);
    private double _lastHours = double.NaN;
    private Draft _draft = Draft.None;
    private RowProblem _problem = RowProblem.NoChimney;
    private int _chimneyHeight;
    private int _rowHoles = 1;
    private long _forcedUntilMs;
    private MeshData? _potMesh;

    public bool HasPot => _pot;
    public int Heats => _heats;
    public IReadOnlyList<ChargeItem> Charge => _charge;
    /// <summary>The recipe the pot's charge melted by, or null while it is unmelted.</summary>
    public string? Melted => _melted;
    public int MeltedUnits => _meltedUnits;
    public double Temperature => _heat.Temperature;
    public double Coke => _heat.Coke;
    public bool Lit => _heat.Lit;
    public Draft Draft => _draft;
    public RowProblem Problem => _problem;
    public bool LidClosed => Block?.Variant["lid"] != "open";

    private CrucibleFurnaceSystem System => CrucibleFurnaceSystem.Of(Api);
    private CrucibleFurnaceConfig Config => System.Config;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(Tick, 1000);
        else
            RegisterGameTickListener(SmokeTick, 400);
    }

    // ---- Ticking ----

    private void Tick(float dt) => Update(dt);

    /// <summary>Runs the furnace up to now (server side): row, draft, fire, melt. Public for the tests.</summary>
    public void Update(float realSeconds = 1)
    {
        double now = Api.World.Calendar.TotalHours;
        if (double.IsNaN(_lastHours))
            _lastHours = now;
        double hours = Math.Max(0, now - _lastHours);
        _lastHours = now;

        var row = FindRow();
        _problem = row.Problem;
        _chimneyHeight = row.ChimneyHeight;
        _rowHoles = row.Holes.Count;
        var draft = Draft.None;
        if (row.Works)
        {
            draft = Draft.Chimney;
            if (_heat.Lit && LidClosed && DrawAir(row, realSeconds))
                _forcedUntilMs = Api.World.ElapsedMilliseconds + 5000;
            if (Api.World.ElapsedMilliseconds < _forcedUntilMs && _heat.Lit)
                draft = Draft.Forced;
        }
        _draft = draft;

        var before = _heat;
        _heat = FurnaceHeat.Advance(_heat, hours, draft, LidClosed, Config);
        bool changed = (int)before.Temperature != (int)_heat.Temperature || before.Lit != _heat.Lit
                       || Math.Ceiling(before.Coke) != Math.Ceiling(_heat.Coke);
        changed |= TryMelt();
        if (changed || hours > 0 && _heat.Lit)
            MarkDirty();
    }

    /// <summary>Moves the fire's clock <paramref name="hours"/> back, as if that much time had passed
    /// since the last tick (the tests' clock); the next <see cref="Update"/> runs it.</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    /// <summary>Sets the fire as it is (the tests' and the creative shortcut's).</summary>
    public void SetFire(double temperature, double coke, bool lit)
    {
        _heat = new HoleHeat(temperature, coke, lit && coke > 0);
        MarkDirty();
    }

    private bool TryMelt()
    {
        if (!_pot || _melted != null || _charge.Count == 0)
            return false;
        var match = System.Recipes.Match(_charge);
        if (match.Recipe is not { } recipe || _heat.Temperature < recipe.Temperature)
            return false;
        _melted = recipe.Code;
        _meltedUnits = PotRecipes.TotalUnits(_charge);
        _charge = [];
        return true;
    }

    public FurnaceRow FindRow()
    {
        var chimney = Config.ChimneyBlocks;
        FurnaceCell At(Int3 p)
        {
            var pos = new BlockPos(Pos.X + p.X, Pos.Y + p.Y, Pos.Z + p.Z, Pos.dimension);
            var block = Api.World.BlockAccessor.GetBlock(pos);
            if (block is BlockMeltingHole)
                return FurnaceCell.Hole;
            string? code = block?.Code?.ToString();
            return code != null && chimney.Any(c => PotRecipes.CodeMatches(c, code)) ? FurnaceCell.Chimney : FurnaceCell.Other;
        }
        return FurnaceRow.Find(new Int3(0, 0, 0), At, Config.MaxHolesInRow, Config.MinChimneyHeight);
    }

    /// <summary>Draws this hole's share of air from the first pipe beside or under any hole of its row
    /// that gives air; true if it got at least half.</summary>
    private bool DrawAir(FurnaceRow row, float realSeconds)
    {
        float want = (float)(Config.AirLitresPerSecond * realSeconds);
        if (want <= 0)
            return false;
        float got = 0;
        foreach (var hole in row.Holes)
        foreach (var face in new[] { BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST, BlockFacing.DOWN })
        {
            var pos = new BlockPos(Pos.X + hole.X + face.Normali.X, Pos.Y + hole.Y + face.Normali.Y, Pos.Z + hole.Z + face.Normali.Z, Pos.dimension);
            var be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be == null || be is BEMeltingHole)
                continue;
            got += ForcedAir.Draw(be, want - got, Api.Logger);
            if (got >= want)
                return true;
        }
        return got >= want / 2;
    }

    // ---- Interaction ----

    /// <summary>
    /// A right-click on the hole. Empty hand: with Shift, the lid opens or shuts; else a shut lid
    /// opens, and an open hole gives up its pot (tongs in the off hand when it is hot) or, with none,
    /// shuts. With the lid open: a fired melting pot goes in; coke goes on the fire (with Shift, into
    /// the pot, for ferrosilicon); anything a pot recipe takes goes into the pot, as much as the
    /// recipes allow. Done on the server; the client says whether the click is the hole's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        bool sneak = byPlayer.Entity.Controls.ShiftKey;
        bool server = Api.Side == EnumAppSide.Server;
        if (held == null)
        {
            if (!server)
                return true;
            if (sneak || LidClosed || !_pot)
                ToggleLid(byPlayer);
            else
                Pull(byPlayer, slot!);
            return true;
        }
        // a firestarter or a torch lights the coke (BlockMeltingHole, IIgnitable)
        if (held.Collectible is ItemFirestarter || held.Block?.HasBehavior<BlockBehaviorCanIgnite>() == true)
            return false;
        string code = held.Collectible.Code.ToString();
        bool pot = code == CrucibleFurnaceSystem.FiredPot;
        bool coke = code == "game:coke" && !sneak;
        if (!pot && !coke && !System.Recipes.IsIngredient(code))
        {
            if (server)
                Error(byPlayer, "notaningredient");
            return true;
        }
        if (!server)
            return true;
        if (LidClosed)
            return Error(byPlayer, "lidclosed");
        if (pot)
            PutPot(byPlayer, slot!);
        else if (coke)
            AddCoke(byPlayer, slot!);
        else
            AddCharge(byPlayer, slot!);
        return true;
    }

    private void ToggleLid(IPlayer byPlayer)
    {
        string to = LidClosed ? "open" : "closed";
        if (Api.World.GetBlock(Block.CodeWithVariant("lid", to)) is not { Id: > 0 } block)
            return;
        Api.World.BlockAccessor.ExchangeBlock(block.Id, Pos);
        Api.World.PlaySoundAt(LidSound, Pos.X + 0.5, Pos.Y + 1, Pos.Z + 0.5, byPlayer);
        MarkDirty(true);
    }

    private void PutPot(IPlayer byPlayer, ItemSlot slot)
    {
        if (_pot)
        {
            Error(byPlayer, "haspot");
            return;
        }
        var stack = slot.Itemstack!;
        _pot = true;
        _heats = PotStack.Heats(stack);
        _charge = PotStack.Charge(stack);
        _melted = null;
        _meltedUnits = 0;
        slot.TakeOut(1);
        slot.MarkDirty();
        Api.World.PlaySoundAt(PotSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer);
        MarkDirty(true);
    }

    private void AddCoke(IPlayer byPlayer, ItemSlot slot)
    {
        int room = Config.CokeCapacity - (int)Math.Ceiling(_heat.Coke - 1e-9);
        if (room <= 0)
        {
            Error(byPlayer, "cokefull", Config.CokeCapacity);
            return;
        }
        int n = Math.Min(room, slot.StackSize);
        _heat = _heat with { Coke = _heat.Coke + n };
        slot.TakeOut(n);
        slot.MarkDirty();
        Api.World.PlaySoundAt(CokeSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer);
        MarkDirty();
    }

    private void AddCharge(IPlayer byPlayer, ItemSlot slot)
    {
        if (!_pot)
        {
            Error(byPlayer, "nopot");
            return;
        }
        if (_melted != null)
        {
            Error(byPlayer, "molten");
            return;
        }
        string code = slot.Itemstack!.Collectible.Code.ToString();
        var add = System.Recipes.CanAdd(_charge, code, slot.StackSize, Config.PotCapacityUnits);
        if (add.Count <= 0)
        {
            Error(byPlayer, "refused-" + add.Refusal.ToString().ToLowerInvariant(), Config.PotCapacityUnits);
            return;
        }
        int i = _charge.FindIndex(c => c.Code == code);
        if (i >= 0)
            _charge[i] = _charge[i] with { Count = _charge[i].Count + add.Count };
        else
            _charge.Add(new ChargeItem(code, add.Count));
        slot.TakeOut(add.Count);
        slot.MarkDirty();
        Api.World.PlaySoundAt(ChargeSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer);
        MarkDirty();
    }

    /// <summary>Takes the pot out into the player's empty hand (server side).</summary>
    private void Pull(IPlayer byPlayer, ItemSlot hand)
    {
        double t = _heat.Temperature;
        bool tongs = byPlayer.Entity.LeftHandItemSlot?.Itemstack?.ItemAttributes?.IsTrue("heatResistant") == true;
        if (t > GlobalConstants.TooHotToTouchTemperature && !tongs)
        {
            Error(byPlayer, "needtongs");
            return;
        }
        var world = Api.World;
        var recipe = System.Recipes.Get(_melted);
        int heats = _heats;
        _pot = false;
        _heats = 0;
        var charge = _charge;
        _charge = [];
        _melted = null;
        int units = _meltedUnits;
        _meltedUnits = 0;
        MarkDirty(true);

        if (recipe == null)
        {
            // unmelted: the pot comes back with its charge
            var pot = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
            PotStack.SetHeats(pot, heats);
            PotStack.SetCharge(pot, charge);
            Hot(pot, t);
            hand.Itemstack = pot;
            hand.MarkDirty();
            return;
        }
        heats++;
        if (recipe.Pourable && CrucibleFurnaceSystem.Resolve(world, recipe.Output) is { } ingot)
        {
            var metal = new ItemStack(ingot);
            double meltingPoint = metal.Collectible.GetMeltingPoint(world, null, new DummySlot(metal));
            if (t < PourWindow.SolidifiesAt(meltingPoint))
            {
                // frozen in the hole: the pot is lost, the metal knocked out
                world.PlaySoundAt(CrackSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null);
                Give(byPlayer, Bits(metal, units / Stainless.UnitsPerLump));
                Error(byPlayer, "frozenhole", units / Stainless.UnitsPerLump);
                return;
            }
            var smelted = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.SmeltedPot)));
            (smelted.Collectible as BlockSmeltedContainer)?.SetContents(smelted, metal, PotRecipes.Products(recipe, units).Units);
            PotStack.SetHeats(smelted, heats);
            Hot(smelted, t);
            double hoursPerSecond = world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul / 3600.0;
            double speed = PourWindow.CooldownPerHour(t, meltingPoint, Config.PourWindowSeconds, hoursPerSecond);
            (smelted.Attributes["temperature"] as ITreeAttribute)?.SetFloat("cooldownSpeed", (float)speed);
            hand.Itemstack = smelted;
            hand.MarkDirty();
            return;
        }
        // a ferroalloy: the cake is broken out of the pot, which goes to the hand first
        var products = PotRecipes.Products(recipe, units);
        if (heats >= Config.PotHeats)
        {
            world.PlaySoundAt(CrackSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null);
            Error(byPlayer, "potcracked");
        }
        else
        {
            var empty = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
            PotStack.SetHeats(empty, heats);
            hand.Itemstack = Hot(empty, t);
            hand.MarkDirty();
        }
        if (products.Lumps > 0 && CrucibleFurnaceSystem.Resolve(world, recipe.Output) is { } lump)
            Give(byPlayer, Hot(new ItemStack(lump, products.Lumps), t));
        if (products.Byproducts > 0 && recipe.Byproduct != null && CrucibleFurnaceSystem.Resolve(world, recipe.Byproduct) is { } slag)
            Give(byPlayer, Hot(new ItemStack(slag, products.Byproducts), t));
    }

    private ItemStack Hot(ItemStack stack, double t)
    {
        if (t > FurnaceHeat.Ambient + 1)
            stack.Collectible.SetTemperature(Api.World, stack, (float)t);
        return stack;
    }

    private ItemStack? Bits(ItemStack metal, int count)
    {
        string? m = metal.Collectible.Variant["metal"];
        return count > 0 && m != null && Api.World.GetItem(new AssetLocation("game", "metalbit-" + m)) is { } bit
            ? new ItemStack(bit, count)
            : null;
    }

    private void Give(IPlayer byPlayer, ItemStack? stack)
    {
        if (stack == null)
            return;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("cruciblefurnace-" + key, Lang.GetL(sp.LanguageCode, CrucibleFurnaceSystem.Domain + ":cruciblefurnace-error-" + key, args));
        return true;
    }

    // ---- Lighting ----

    /// <summary>Whether a firestarter or torch can light the hole now; else the error key.</summary>
    public string? CannotLight()
    {
        if (_heat.Lit)
            return "lit";
        if (LidClosed)
            return "lidclosed";
        if (_heat.Coke <= 0)
            return "nocoke";
        if (_pot && _melted == null && _charge.Count > 0 && System.Recipes.Match(_charge).Recipe == null)
            return "badcharge";
        return null;
    }

    /// <summary>Lights the coke (server side); the reason it cannot, to the player, else.</summary>
    public bool TryLight(IPlayer? byPlayer)
    {
        if (CannotLight() is { } why)
        {
            Error(byPlayer, why);
            return false;
        }
        Update(0);
        _heat = _heat with { Lit = true };
        Api.World.PlaySoundAt(IgniteSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null);
        MarkDirty();
        return true;
    }

    // ---- Breaking ----

    /// <summary>What breaking the hole gives besides the hole: the pot with its charge, the unburnt
    /// coke; a melted charge as its products (stainless as bits), the pot lost.</summary>
    public List<ItemStack> ContentDrops()
    {
        var drops = new List<ItemStack>();
        var world = Api.World;
        int coke = (int)Math.Floor(_heat.Coke + 1e-9);
        if (coke > 0 && world.GetItem(new AssetLocation("game", "coke")) is { } cokeItem)
            drops.Add(new ItemStack(cokeItem, coke));
        if (!_pot)
            return drops;
        if (System.Recipes.Get(_melted) is { } recipe)
        {
            var products = PotRecipes.Products(recipe, _meltedUnits);
            if (CrucibleFurnaceSystem.Resolve(world, recipe.Output) is { } output)
            {
                var stack = recipe.Pourable ? Bits(new ItemStack(output), products.Units / Stainless.UnitsPerLump)
                    : products.Lumps > 0 ? new ItemStack(output, products.Lumps) : null;
                if (stack != null)
                    drops.Add(stack);
            }
            if (products.Byproducts > 0 && recipe.Byproduct != null && CrucibleFurnaceSystem.Resolve(world, recipe.Byproduct) is { } slag)
                drops.Add(new ItemStack(slag, products.Byproducts));
            return drops;
        }
        var pot = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
        PotStack.SetHeats(pot, _heats);
        PotStack.SetCharge(pot, _charge);
        drops.Add(pot);
        return drops;
    }

    // ---- Client ----

    private void SmokeTick(float dt)
    {
        if (!_heat.Lit || Api is not ICoreClientAPI capi || _problem != RowProblem.None)
            return;
        // smoke from the chimney's top: find it again (cheap: a row of four and a column)
        var row = FindRow();
        if (row.Chimney is not { } c)
            return;
        var top = new Vec3d(Pos.X + c.X + 0.5, Pos.Y + c.Y + row.ChimneyHeight + 0.1, Pos.Z + c.Z + 0.5);
        var smoke = new SimpleParticleProperties(1, 2, ColorUtil.ToRgba(80, 60, 60, 60), top.AddCopy(-0.2, 0, -0.2), top.AddCopy(0.2, 0.2, 0.2),
            new Vec3f(-0.1f, 0.3f, -0.1f), new Vec3f(0.1f, 0.6f, 0.1f), 3, -0.02f, 0.5f, 1.5f, EnumParticleModel.Quad)
        {
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.5f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -40),
        };
        capi.World.SpawnParticles(smoke);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (_pot && !LidClosed && Api is ICoreClientAPI capi
            && capi.World.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)) is { Id: > 0 } potBlock)
        {
            if (_potMesh == null)
            {
                tessThreadTesselator.TesselateBlock(potBlock, out var mesh);
                _potMesh = mesh.Translate(0, 2 / 16f, 0);
            }
            mesher.AddMeshData(_potMesh);
        }
        return false;
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-info-" + key, args);
        dsc.AppendLine(L(LidClosed ? "lidclosed" : "lidopen"));
        dsc.AppendLine(L("temperature", (int)Math.Round(_heat.Temperature)));
        int coke = (int)Math.Ceiling(_heat.Coke - 1e-9);
        dsc.AppendLine(coke == 0 ? L("nocoke") : L(_heat.Lit ? "cokeburning" : "coke", coke, Config.CokeCapacity));
        dsc.AppendLine(_problem switch
        {
            RowProblem.None => L(_draft == Draft.Forced ? "draft-forced" : "draft-chimney", _rowHoles, _chimneyHeight),
            RowProblem.ChimneyTooShort => L("draft-short", _chimneyHeight, Config.MinChimneyHeight),
            RowProblem.TooManyHoles => L("draft-toomany", Config.MaxHolesInRow),
            _ => L("draft-none", Config.MinChimneyHeight),
        });
        if (!_pot)
        {
            dsc.AppendLine(L("nopot"));
            return;
        }
        dsc.AppendLine(PotStack.HeatsLine(_heats, Config.PotHeats));
        var world = Api.World;
        if (System.Recipes.Get(_melted) is { } melted)
        {
            var output = CrucibleFurnaceSystem.Resolve(world, melted.Output);
            string name = output == null ? melted.Output
                : output.Variant["metal"] is { } metal ? Lang.Get("material-" + metal) : new ItemStack(output).GetName();
            dsc.AppendLine(melted.Pourable ? L("molten", PotRecipes.Products(melted, _meltedUnits).Units, name)
                : L("ready", PotRecipes.Products(melted, _meltedUnits).Lumps, name));
            return;
        }
        if (_charge.Count == 0)
        {
            dsc.AppendLine(L("empty", Config.PotCapacityUnits));
            return;
        }
        dsc.AppendLine(L("charge", PotStack.Describe(world, _charge), PotRecipes.TotalUnits(_charge), Config.PotCapacityUnits));
        var match = System.Recipes.Match(_charge);
        if (match.Recipe is { } r)
            dsc.AppendLine(L("makes", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-recipe-" + r.Code), (int)r.Temperature));
        else if (match.Closest is { } closest)
        {
            dsc.AppendLine(L(match.TooSmall ? "toosmall" : "wrongratio", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-recipe-" + closest.Code)));
            foreach (var (ingredient, share) in match.Shares)
                dsc.AppendLine(L("share", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-ingredient-" + ingredient.Name),
                    (int)Math.Round(share * 100), (int)Math.Round(ingredient.Min * 100), (int)Math.Round(ingredient.Max * 100)));
        }
    }

    // ---- Saving ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("pot", _pot);
        tree.SetInt("heats", _heats);
        var charge = new TreeAttribute();
        foreach (var item in _charge)
            charge.SetInt(item.Code, item.Count);
        tree["charge"] = charge;
        if (_melted != null)
            tree.SetString("melted", _melted);
        else
            tree.RemoveAttribute("melted");
        tree.SetInt("meltedUnits", _meltedUnits);
        tree.SetDouble("temperature", _heat.Temperature);
        tree.SetDouble("coke", _heat.Coke);
        tree.SetBool("lit", _heat.Lit);
        tree.SetDouble("lastHours", _lastHours);
        tree.SetInt("draft", (int)_draft);
        tree.SetInt("problem", (int)_problem);
        tree.SetInt("chimneyHeight", _chimneyHeight);
        tree.SetInt("rowHoles", _rowHoles);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _pot = tree.GetBool("pot");
        _heats = tree.GetInt("heats");
        _charge = tree["charge"] is ITreeAttribute charge
            ? charge.Select(e => new ChargeItem(e.Key, (e.Value as IntAttribute)?.value ?? 0)).Where(i => i.Count > 0).ToList()
            : [];
        _melted = tree.GetString("melted");
        _meltedUnits = tree.GetInt("meltedUnits");
        _heat = new HoleHeat(tree.GetDouble("temperature", FurnaceHeat.Ambient), tree.GetDouble("coke"), tree.GetBool("lit"));
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        _draft = (Draft)tree.GetInt("draft");
        _problem = (RowProblem)tree.GetInt("problem", (int)RowProblem.NoChimney);
        _chimneyHeight = tree.GetInt("chimneyHeight");
        _rowHoles = tree.GetInt("rowHoles", 1);
    }
}
