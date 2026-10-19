using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>A machine with a flue (<see cref="FurnaceFiring"/>): its row's members find each other by it.</summary>
public interface IFiredMachine
{
    FurnaceFiring Firing { get; }
}

/// <summary>
/// One fired machine's flue, a piece any machine on the shared base composes, as it composes its oil
/// (<see cref="MachineOiling"/>): the crucible furnace and the fired ore machines (<see cref="BEFiredOreMachine"/>).
/// It keeps the outlet's cap and the row the machine last found (<see cref="FurnaceRow"/>: machines side by side
/// on one chimney, every other outlet capped), and gives what they share around them: the chimney standing on the
/// outlet, the neighbours along the row, capping and uncapping, the cap's drop, saving, the brickwork's textures
/// in the machine's brick type, the help's lighting stacks and the row's line in the block info.
/// </summary>
public sealed class FurnaceFiring
{
    private static readonly AssetLocation AnvilSound = new("game", "sounds/block/anvil1");

    private readonly AssetLocation _capSound;
    private bool _capped;
    private FurnaceRow _row = FurnaceRow.Alone;

    /// <param name="capSound">What capping and uncapping sound like: an anvil's ring, by default.</param>
    public FurnaceFiring(AssetLocation? capSound = null) => _capSound = capSound ?? AnvilSound;

    /// <summary>The outlet is capped.</summary>
    public bool Capped => _capped;

    /// <summary>The row the machine last found.</summary>
    public FurnaceRow Row => _row;

    /// <summary>Takes <paramref name="row"/> as the machine's row; whether it changed.</summary>
    public bool SetRow(FurnaceRow row)
    {
        if (row == _row)
            return false;
        _row = row;
        return true;
    }

    // ---- The row and its chimney ----

    /// <summary>The chimney standing on <paramref name="owner"/>'s outlet, counted as the crucible furnace counts its own.</summary>
    public static ChimneyColumn ChimneyOn(BEMachine owner, FurnaceFlue flue)
    {
        var ba = owner.Api.World.BlockAccessor;
        var pos = owner.CellPos(flue.ChimneyBase);
        return ChimneyColumn.Measure(n => ba.GetBlock(pos.UpCopy(n))?.Code?.ToString(), CrucibleFurnaceSystem.Of(owner.Api).Config);
    }

    /// <summary>The machine of type <typeparamref name="T"/> <paramref name="k"/> widths along <paramref name="owner"/>'s
    /// row (native +x), facing the same way, or null.</summary>
    public static T? Beside<T>(BEMachine owner, FurnaceFlue flue, int k) where T : BEMachine
    {
        var pos = owner.CellPos(new Int3(k * flue.Width, 0, 0));
        var ba = owner.Api.World.BlockAccessor;
        return ba.GetBlockEntity(pos) is T other && other.Side == owner.Side && ba.GetBlock(pos) is BlockMachine ? other : null;
    }

    /// <summary><paramref name="owner"/>'s row: <paramref name="member"/> gives the machine at an offset along it that
    /// is one of the row (never asked for 0), or null; the rules are <paramref name="rowMax"/> machines and
    /// <paramref name="minChimney"/> chimney blocks.</summary>
    public FurnaceRow Find(BEMachine owner, FurnaceFlue flue, System.Func<int, BEMachine?> member, int rowMax, int minChimney)
    {
        RowMember? At(int k) => k == 0
            ? new RowMember(_capped, ChimneyOn(owner, flue))
            : member(k) is { } other and IFiredMachine fired ? new RowMember(fired.Firing.Capped, ChimneyOn(other, flue)) : null;
        return FurnaceRow.Find(At, rowMax, minChimney);
    }

    // ---- The cap ----

    /// <summary>Whether a click is on the outlet's top, where the cap goes.</summary>
    public static bool IsOutletTop(BEMachine owner, FurnaceFlue flue, BlockSelection sel) =>
        sel.Face == BlockFacing.UP && sel.Position.Equals(owner.CellPos(flue.OutletCell));

    /// <summary>Caps the outlet with the held stack (on the server; out of it unless the player is in creative mode).
    /// False, the click not the machine's, when it holds too few.</summary>
    public bool TryCap(BEMachine owner, FurnaceFlue flue, IPlayer byPlayer, ItemSlot slot)
    {
        if (slot.StackSize < flue.CapCount)
            return false;
        if (owner.Api.Side != EnumAppSide.Server)
            return true;
        if (byPlayer.WorldData?.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(flue.CapCount);
            slot.MarkDirty();
        }
        _capped = true;
        Sound(owner, flue, byPlayer);
        owner.MarkDirty(true);
        return true;
    }

    /// <summary>Takes the cap off to <paramref name="give"/> (on the server).</summary>
    public void Uncap(BEMachine owner, FurnaceFlue flue, IPlayer byPlayer, Action<ItemStack> give)
    {
        if (owner.Api.Side != EnumAppSide.Server || CapStack(owner.Api.World, flue) is not { } cap)
            return;
        _capped = false;
        give(cap);
        Sound(owner, flue, byPlayer);
        owner.MarkDirty(true);
    }

    private void Sound(BEMachine owner, FurnaceFlue flue, IPlayer byPlayer)
    {
        var at = owner.WorldPoint(new Float3(flue.OutletCell.X + 0.5f, flue.OutletCell.Y + 1, flue.OutletCell.Z + 0.5f));
        owner.Api.World.PlaySoundAt(_capSound, at.X, at.Y, at.Z, byPlayer);
    }

    /// <summary>The cap as an item stack, or null when its item is not in the game.</summary>
    public static ItemStack? CapStack(IWorldAccessor world, FurnaceFlue flue) =>
        CrucibleFurnaceSystem.Resolve(world, flue.CapCode) is { } cap ? new ItemStack(cap, flue.CapCount) : null;

    /// <summary>What breaking the machine gives of its flue: the cap, while it is on.</summary>
    public ItemStack? Drop(IWorldAccessor world, FurnaceFlue flue) => _capped ? CapStack(world, flue) : null;

    // ---- Saving and syncing ----

    public void Write(ITreeAttribute tree)
    {
        tree.SetBool("capped", _capped);
        tree.SetInt("rowFirst", _row.First);
        tree.SetInt("rowCount", _row.Count);
        tree.SetInt("rowChimneyAt", _row.ChimneyAt ?? int.MinValue);
        tree.SetInt("rowChimneyHeight", _row.ChimneyHeight);
        tree.SetInt("rowOpenOutlet", _row.OpenOutlet ?? int.MinValue);
        tree.SetInt("rowProblem", (int)_row.Problem);
    }

    public void Read(ITreeAttribute tree)
    {
        _capped = tree.GetBool("capped");
        int? Opt(string key) => tree.GetInt(key, int.MinValue) is var v && v != int.MinValue ? v : null;
        _row = new FurnaceRow(tree.GetInt("rowFirst"), Math.Max(1, tree.GetInt("rowCount", 1)), Opt("rowChimneyAt"),
            tree.GetInt("rowChimneyHeight"), Opt("rowOpenOutlet"), (RowProblem)tree.GetInt("rowProblem", (int)RowProblem.NoChimney));
    }

    // ---- Drawing, help and info ----

    /// <summary>The block's textures with the brickwork's code (<see cref="BrickTypes.TextureCode"/>) in brick type
    /// <paramref name="type"/> (the default before the first brick): its base with its overlays, composed into the
    /// block atlas.</summary>
    public static ITexPositionSource BrickTextures(ICoreClientAPI capi, Block block, BrickTypes bricks, string? type)
    {
        var inner = capi.Tesselator.GetTextureSource(block);
        if ((bricks.Get(type) ?? bricks.Get(bricks.Default)) is not { } brick)
            return inner;
        var composite = new CompositeTexture(new AssetLocation(brick.Base))
        {
            BlendedOverlays = brick.Overlays.Select(o => new BlendedOverlayTexture { Base = new AssetLocation(o) }).ToArray(),
        };
        return capi.BlockTextureAtlas.GetOrInsertTexture(composite, out _, out var pos) && pos != null
            ? new CodeTextureSource(inner, bricks.TextureCode, pos)
            : inner;
    }

    private sealed class CodeTextureSource(ITexPositionSource inner, string code, TextureAtlasPosition pos) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode == code ? pos : inner[textureCode]!;
    }

    /// <summary>What lights a fire (a firestarter, a torch), for the help's lighting line.</summary>
    public static ItemStack[] IgniteStacks(ICoreAPI api) =>
        ObjectCacheUtil.GetOrCreate(api, "seraphhorizons-furnace-ignitestacks", () => BlockBehaviorCanIgnite.CanIgniteStacks(api, true).ToArray());

    /// <summary>The row's line in a fired ore machine's block info, <paramref name="line"/> its
    /// <c>{machine}-info-row…</c> lines.</summary>
    public string RowLine(FurnaceFlue flue, System.Func<string, object[], string> line) => _row.Problem switch
    {
        RowProblem.None => line("row", [_row.Count, _row.ChimneyHeight]),
        RowProblem.TooManyFurnaces => line("row-toomany", [flue.RowMax]),
        RowProblem.OutletOpen => line("row-open", []),
        RowProblem.ChimneyTooShort => line("row-short", [_row.ChimneyHeight, flue.MinChimney]),
        RowProblem.NoChimneyTop => line("row-notop", []),
        RowProblem.ChimneyTopBuried => line("row-buried", []),
        _ => line(_capped && _row.Count == 1 ? "row-capped" : "row-none", [flue.MinChimney]),
    };
}
