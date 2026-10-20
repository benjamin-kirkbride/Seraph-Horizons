using System.Text;
using Vintagestory.API.Client;
using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// A spring (README "Springs"): a solid block of its host rock, <c>waterspring-{kind}-{rock}</c>,
/// that the rivulet generator places where it placed a single water source. It pours like that
/// source while no pipe couples to it (<see cref="BlockEntityWaterSpring"/>), feeds the pipes that
/// do, fills any liquid container with well water and lets a player drink it with an empty hand
/// (<see cref="SpringWater"/>), and breaks into what its host rock does.
/// </summary>
public class BlockWaterSpring : Block
{
    public SpringKind Kind { get; private set; }
    public string Rock { get; private set; } = HostRock.Fallback;

    /// <summary>The game's source and falling blocks of the liquid this spring pours.</summary>
    public Block? Source { get; private set; }
    public Block? Falling { get; private set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        SpringWater.EnsureBound(api);
        if (SpringCodes.Parse(Code?.Path) is { } parsed)
        {
            Kind = parsed.Kind;
            Rock = parsed.Rock;
        }
        Source = api.World.GetBlock(new AssetLocation("game", SpringCodes.SourcePath(Kind)));
        Falling = api.World.GetBlock(new AssetLocation("game", SpringCodes.FallingPath(Kind)));
        SpringWater.SetPortion(this);
    }

    /// <summary>Whether a fluid block is the water this spring pours.</summary>
    public bool Pours(Block? fluid) => fluid?.Code is { Domain: "game" } code && SpringCodes.IsLiquidOf(Kind, code.Path);

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
            return;
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityWaterSpring spring)
            spring.Look();
        else if (EntityClass != null)
            // Placed without its block entity (a schematic, a world edit): it gets one now.
            world.BlockAccessor.SpawnBlockEntity(EntityClass, pos);
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        // What it poured has no source now: the flowing water round it looks again and drains.
        if (world.Side == EnumAppSide.Server)
            SpringPouring.Notify(world, pos, this);
    }

    /// <summary>What mining its host rock gives (the rock's own drops), nothing of its own.</summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var rock = world.GetBlock(new AssetLocation(SpringCodes.HostRockCode(Rock)));
        if (rock == null || rock.Id == 0 || rock is BlockWaterSpring)
            return [];
        return rock.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(this);

    /// <summary>"Granite spring", "Granite rapids spring": the rock's own name in the kind's.</summary>
    public override string GetHeldItemName(ItemStack itemStack)
    {
        string key = $"{SpringCodes.Domain}:waterspring-name-{SpringCodes.NameOf(Kind)}";
        string rock = Lang.GetMatching("game:rock-" + Rock);
        return Lang.HasTranslation(key) ? Lang.Get(key, rock) : base.GetHeldItemName(itemStack);
    }

    // ---- drinking: an empty hand held on it, as Hydrate or Diedrate's drinking (a second) ----

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!SpringsSystem.On || !SpringWater.CanDrink || byPlayer?.Entity == null || !byPlayer.Entity.RightHandItemSlot.Empty)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!SpringWater.MayDrink(byPlayer))
            return false;
        return true;
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Client && secondsUsed > 0.2f && world.Rand.NextDouble() < 0.3)
            world.PlaySoundAt(new AssetLocation("sounds/player/drink1"), byPlayer.Entity, null, randomizePitch: true, 8f, 0.4f);
        return secondsUsed < SpringWater.DrinkSeconds;
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side != EnumAppSide.Server || secondsUsed < SpringWater.DrinkSeconds - 0.05f || !SpringsSystem.On)
            return;
        if (SpringWater.MayDrink(byPlayer) && SpringWater.Drink(world, byPlayer))
        {
            var at = blockSel.FullPosition ?? blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5);
            world.PlaySoundAt(new AssetLocation("sounds/effect/water-pour"), at.X, at.Y, at.Z, null, true, 16f, 0.6f);
        }
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (SpringsSystem.On && SpringWater.CanDrink)
            help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:springs-help-drink", MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
        help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:springs-help-fill", MouseButton = EnumMouseButton.Right });
        return [.. help, .. base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? []];
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var text = new StringBuilder(base.GetPlacedBlockInfo(world, pos, forPlayer) ?? "");
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityWaterSpring spring)
        {
            if (text.Length > 0 && text[^1] != '\n')
                text.AppendLine();
            float rate = SpringsSystem.Settings.LitresPerSecond(Kind);
            text.Append(spring.Tapped
                ? Lang.Get("seraphhorizons:springs-info-tapped", SpringsConfig.Format(rate))
                : Lang.Get("seraphhorizons:springs-info-pouring"));
            float extra = SpringPumps.Extra(rate, SpringsSystem.Settings.PumpBoostPerTurn, spring.PumpTurns);
            if (spring.Tapped && extra > 0)
                text.AppendLine().Append(Lang.Get("seraphhorizons:springs-info-pumped",
                    spring.PumpTurns.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), SpringsConfig.Format(extra)));
        }
        return text.ToString();
    }
}
