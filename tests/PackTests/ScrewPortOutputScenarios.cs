using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, ScrewPortOutput: a ported Archimedes screw pushes up into a container,
/// then into its port, then out of its port (only with no container above), and never throws out
/// of its top. A stack of five screws on angled gears, driven by a creative rotor at speed setting
/// 10 (the network at 0.95), is fed one flax fibre at a time into its bottom screw for 60 s of
/// virtual time. With the patch every case moves about 108 a minute, none loose. Without it, an
/// open ported top threw every item out of its top (107 loose, none in the chest on its port);
/// capped with granite, 57 reached the chest against 107 for a straight top under a chest; a
/// ported bottom screw threw 58 of 111 out of its open port.
/// </summary>
public partial class SharedWorldScenarios
{
    private const int ScrewHeight = 5;
    private const long ScrewRunMs = 60_000;

    private static bool ScrewPortPatched() =>
        Harmony.GetPatchInfo(ScrewPortOutput.MoveItem)?.Prefixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) == true;

    private readonly record struct ScrewRun(int Fed, int InChest, int Loose)
    {
        public double PerMinute => InChest * 60_000.0 / ScrewRunMs;
    }

    /// <summary>Builds the stack at a site of its own, runs it and clears it. The top screw is
    /// <paramref name="top"/>; a ported one has the chest on its port, a straight one under it.
    /// <paramref name="cap"/> puts granite over a ported top; <paramref name="bottom"/> is the
    /// bottom screw (its port, if any, open to the air).</summary>
    private async Task<ScrewRun> RunScrewStack(int site, string top, bool cap = false,
        string bottom = "game:archimedesscrew-straight")
    {
        var b = await CutterSite(860, 700 + 8 * site, 3);
        var sapi = (ICoreServerAPI)World.Api;
        sapi.WorldManager.LoadChunkColumnPriority(b.X / GlobalConstants.ChunkSize, b.Z / GlobalConstants.ChunkSize,
            new ChunkLoadOptions { KeepLoaded = true });
        await World.Ticks(5);

        World.SetBlock("game:angledgears-eu", b);
        World.SetBlock(bottom, b.UpCopy());
        for (int y = 2; y < ScrewHeight; y++)
            World.SetBlock("game:archimedesscrew-straight", b.UpCopy(y));
        var topPos = b.UpCopy(ScrewHeight);
        World.SetBlock(top, topPos);
        bool ported = top.Contains("-ported-");
        var chestPos = ported ? topPos.NorthCopy() : topPos.UpCopy();
        World.SetBlock("game:chest-south", chestPos);
        if (ported && cap)
            World.SetBlock("game:rock-granite", topPos.UpCopy());
        var rotorPos = b.EastCopy();
        World.SetBlock("game:creativerotor-east", rotorPos);
        await World.Ticks(3);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);

        var feed = (BlockEntityArchimedesScrew)W.BlockAccessor.GetBlockEntity(b.UpCopy())!;
        var mp = feed.GetBehavior<BEBehaviorMPArchimedesScrew>();
        await World.Until(() => mp?.Network != null && Math.Abs(mp.Network.Speed) > 0.01f, 15000);
        await World.Ticks(60);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;
        var item = W.GetItem(new AssetLocation("game:flaxfibers"))!;
        var area = (Start: b.AddCopy(-4, 0, -4), End: b.AddCopy(5, ScrewHeight + 8, 5));
        int fed = 0;
        float speed;
        using (VirtualClock.Start(World))
        {
            long start = W.ElapsedMilliseconds;
            while (W.ElapsedMilliseconds - start < ScrewRunMs)
            {
                if (feed.Inventory[0].Empty)
                {
                    feed.Inventory[0].Itemstack = new ItemStack(item, 1);
                    feed.Inventory[0].MarkDirty();
                    fed++;
                }
                await World.Ticks(1);
            }
            speed = mp!.Network!.Speed;
        }
        var loose = W.GetEntitiesInsideCuboid(area.Start, area.End, e => e is EntityItem).ToList();
        var run = new ScrewRun(fed, chest.Inventory.Sum(s => s.StackSize), loose.Sum(e => ((EntityItem)e).Itemstack.StackSize));
        output.WriteLine($"{bottom} .. {top}{(cap ? " capped" : "")}: network {speed:0.000}, fed {run.Fed}, "
                         + $"chest {run.InChest} ({run.PerMinute:0.0}/min), loose {run.Loose}");
        foreach (var e in loose)
            e.Die(EnumDespawnReason.Removed);
        for (int y = 0; y <= ScrewHeight + 1; y++)
            World.SetBlock("game:air", b.UpCopy(y));
        World.SetBlock("game:air", chestPos);
        World.SetBlock("game:air", rotorPos);
        await World.Ticks(2);
        return run;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Screw_port_open_top_sends_every_item_into_the_chest_on_its_port()
    {
        Assert.True(ScrewPortPatched());
        var run = await RunScrewStack(0, "game:archimedesscrew-ported-north");
        Assert.Equal(0, run.Loose);
        // About 108 a minute; without the patch none.
        Assert.True(run.InChest >= 60, $"only {run.InChest} reached the chest");
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Screw_port_capped_top_keeps_up_with_a_straight_top_into_a_chest()
    {
        Assert.True(ScrewPortPatched());
        var straight = await RunScrewStack(1, "game:archimedesscrew-straight");
        var capped = await RunScrewStack(2, "game:archimedesscrew-ported-north", cap: true);
        Assert.Equal(0, capped.Loose);
        Assert.True(straight.InChest >= 60, $"the straight control moved only {straight.InChest}");
        // Without the patch about half (57 against 107).
        Assert.True(capped.InChest >= 0.85 * straight.InChest,
            $"capped ported top {capped.InChest} against {straight.InChest} for the straight one");
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Screw_port_bottom_screw_feeds_the_stack_and_never_its_open_port()
    {
        Assert.True(ScrewPortPatched());
        var run = await RunScrewStack(3, "game:archimedesscrew-straight", bottom: "game:archimedesscrew-ported-north");
        // Without the patch about half are thrown out of the port.
        Assert.Equal(0, run.Loose);
        Assert.True(run.InChest >= 60, $"only {run.InChest} reached the chest");
    }
}
