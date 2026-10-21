using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// A fired ore machine (the stall roaster, the reverberatory roaster, the cupellation and liquation furnaces, the
/// Parkes kettle, the acid parting vessel): an ore machine (<see cref="BEOreMachine"/>) with a flue
/// (<see cref="FurnaceFiring"/>, on its rig's <see cref="FurnaceFlue"/>) whose brickwork is one fired brick type,
/// lit as the forge is. It keeps what they share: the cap and the row, found again each second on the server
/// (<see cref="FindRow"/>: machines of its kind side by side, built and facing the same way, unless
/// <see cref="RowInOwnTick"/>), the brick type the first brick set (the brickwork's textures, and the renderer's
/// key), lighting (<see cref="Ignite"/> says why not, else <see cref="Light"/>), the fire's particles (its fire door's
/// flames, its row's chimney smoke: <see cref="OnClientTick"/>), the cap's drop, and saving them.
/// </summary>
public abstract class BEFiredOreMachine : BEOreMachine, IFiredMachine
{
    private readonly FurnaceFiring _firing = new();
    private readonly ParticleTimer _fireParticles = new(0.3f);

    public FurnaceFiring Firing => _firing;

    /// <summary>The machine's rig, with its flue; null until it is loaded.</summary>
    protected abstract FiredRig? FiredRig { get; }

    public FurnaceFlue? Flue => FiredRig?.Flue;

    public bool Capped => _firing.Capped;
    public FurnaceRow Row => _firing.Row;

    /// <summary>The fired brick type of its brickwork (<c>red</c>), once the first is in: the first stage's first material.</summary>
    public string? BrickType =>
        Assembly?.Build.CodeIn(0, 0) is { } code && Flue is { } flue ? flue.Bricks.TypeOf(code) : null;

    /// <summary>Whether the machine finds its row in a tick of its own (<see cref="RefreshRow"/>), not the shared
    /// one a second.</summary>
    protected virtual bool RowInOwnTick => false;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server && !RowInOwnTick)
            RegisterGameTickListener(_ =>
            {
                if (Built && RefreshRow())
                    MarkDirty(true);
            }, 1000);
    }

    // ---- The row and its chimney ----

    /// <summary>The chimney standing on the outlet, counted as the crucible furnace counts its own.</summary>
    public ChimneyColumn ChimneyOnOutlet() => Flue is { } flue ? FurnaceFiring.ChimneyOn(this, flue) : default;

    /// <summary>Its row: machines of its kind side by side along their width (built, facing the same way), one
    /// chimney on any outlet, the others capped. The reverberatory roaster's is it and the furnace back to back with it.</summary>
    public FurnaceRow FindRow() =>
        Flue is { } flue && Built ? _firing.Find(this, flue, RowMemberAt, flue.RowMax, flue.MinChimney) : FurnaceRow.Alone;

    /// <summary>The machine of its row at offset <paramref name="k"/> (never 0), or null: one of its kind <paramref name="k"/>
    /// widths along, built and facing the same way. The reverberatory roaster's is the furnace back to back with it.</summary>
    protected virtual BEFiredOreMachine? RowMemberAt(int k) =>
        Flue is { } flue && FurnaceFiring.Beside<BEFiredOreMachine>(this, flue, k) is { Built: true } m && m.GetType() == GetType() ? m : null;

    /// <summary>Finds the row again (server side); whether it changed.</summary>
    protected bool RefreshRow() => _firing.SetRow(FindRow());

    /// <summary>Whether a click is on the outlet's top, where the cap goes.</summary>
    protected bool IsOutletTop(BlockSelection sel) => Flue is { } flue && FurnaceFiring.IsOutletTop(this, flue, sel);

    /// <summary>Caps the outlet with the held stack; false when it holds too few.</summary>
    protected bool Cap(IPlayer byPlayer, ItemSlot slot) => Flue is { } flue && _firing.TryCap(this, flue, byPlayer, slot);

    /// <summary>Takes the cap off to the player.</summary>
    protected void Uncap(IPlayer byPlayer)
    {
        if (Flue is { } flue)
            _firing.Uncap(this, flue, byPlayer, stack => Give(byPlayer, stack));
    }

    /// <summary>Gives <paramref name="stack"/> to the player, or drops it over the controller when they have no room.</summary>
    protected virtual void Give(IPlayer byPlayer, ItemStack? stack)
    {
        if (stack == null || stack.StackSize <= 0)
            return;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    /// <summary>The row's line in the block info, the machine's <c>{machine}-info-row…</c> lines.</summary>
    protected string RowLine() =>
        Flue is { } flue ? _firing.RowLine(flue, (key, args) => Lang.Get($"seraphhorizons:{Machine}-info-{key}", args)) : "";

    // ---- Fire ----

    public override bool Ignitable => Built;

    public override bool Burning => Lit;

    /// <summary>Whether its fire burns.</summary>
    public abstract bool Lit { get; }

    /// <summary>Lights it (server side), or tells the player why not (<see cref="BEMachine.CannotIgnite"/>); lit, the
    /// row is found at once.</summary>
    public override void Ignite(IPlayer? byPlayer)
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        if (CannotIgnite() is { } why)
        {
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError(Machine, why);
            return;
        }
        Light(byPlayer);
        _firing.SetRow(FindRow());
        MarkDirty(true);
    }

    /// <summary>The fire lit (server side, it can be): the machine's own state, its sound and the audit line.</summary>
    protected abstract void Light(IPlayer? byPlayer);

    // ---- The fire's particles ----

    /// <summary>Where its fire shows, flaming while lit (<see cref="FireDoorEffect"/>): the rig's <c>fireDoor</c>; null
    /// (none) when it has none. A machine whose fire shows elsewhere (the stall roaster's heap) names that point.</summary>
    protected virtual Float3? FireDoorPoint => Rig?.Point("fireDoor");

    /// <summary>What its fire door shows each particle tick while lit: flames and embers (<see cref="MachineParticles.FireDoor"/>).
    /// Override to change the look; the crucible furnace's own lid-gated flames at its mouth are its own.</summary>
    protected virtual void FireDoorEffect(ICoreClientAPI capi, Vec3d at) =>
        MachineParticles.FireDoor(capi.World, at, MachineParticles.Around(MachineParticles.FireDoorSpread, Side));

    /// <summary>Whether it is the one of its row that smokes at the chimney: the member the chimney stands on (so one
    /// emitter a row), while the row draws and any member burns.</summary>
    private bool SmokesForRow()
    {
        var row = Row;
        if (row.ChimneyAt != 0 || !row.Works)
            return false;
        return Lit || row.Members.Any(k => k != 0 && RowMemberAt(k) is { Lit: true });
    }

    /// <summary>While lit, its fire door flames; the row's chimney owner smokes off the chimney top while the row burns,
    /// as the crucible furnace's chimneys do.</summary>
    protected override void OnClientTick(float dt)
    {
        if (Api is not ICoreClientAPI capi || !Built || !_fireParticles.Due(dt))
            return;
        if (Lit && FireDoorPoint is { } door)
            FireDoorEffect(capi, WorldPoint(door));
        if (Flue is { } flue && SmokesForRow())
            FurnaceFiring.ChimneySmoke(capi.World, FurnaceFiring.ChimneyTop(this, flue, Row.ChimneyHeight));
    }

    // ---- Drawing and breaking ----

    public override string TextureKey => BrickType ?? "";

    /// <summary>The block's textures, the brickwork's brick code in the machine's brick type.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        Flue is { } flue ? FurnaceFiring.BrickTextures(capi, Block, flue.Bricks, BrickType) : base.PartTextures(capi);

    /// <summary>The frame and every stage's items, and the cap.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (Flue is { } flue && _firing.Drop(Api.World, flue) is { } cap)
            yield return cap;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _firing.Write(tree);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _firing.Read(tree);
    }
}
