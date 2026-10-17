using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class BigPileRulesTests
{
    // Piles as the pack loads them (game 1.22.7, More Piles 3.0.1): capacity, items per model,
    // cuboids per model, collision scale, ratio, a click, a Ctrl click.
    private static readonly BigPileRules.Pile Firewood = new(32, 2, 1, 0.125f, 0.5f, 2, 8);
    private static readonly BigPileRules.Pile Ingot = new(64, 1, 1, 0f, 1f, 1, 4);
    private static readonly BigPileRules.Pile Stone = new(64, 2, 1, 0.06f, 0.5f, 2, 16);
    private static readonly BigPileRules.Pile RawBrick = new(24, 1, 1, 0.1666666666666667f, 1f, 1, 4);
    private static readonly BigPileRules.Pile MorePilesCube = new(64, 1, 1, 0.125f, 1f, 1, 4);
    private static readonly BigPileRules.Pile MorePilesLog = new(16, 1, 3, 0.25f, 1f, 1, 4);
    private static readonly BigPileRules.Pile Snowball = new(11, 1, 1, 0.2f, 1f, 1, 6);

    [Theory]
    [InlineData(32, 32, 2048)]  // firewood
    [InlineData(64, 16, 1024)]  // an ingot, a rock block cut to 16
    [InlineData(24, 64, 4096)]  // raw bricks
    [InlineData(64, 1, 64)]     // a stack of one already holds 64 stacks
    [InlineData(128, 1, 128)]   // never smaller than it was
    [InlineData(4, 0, 64)]      // no stack size counts as one
    public void Capacity_is_64_stacks_never_less_than_before(int capacity, int maxStack, int expected) =>
        Assert.Equal(expected, BigPileRules.Capacity(capacity, maxStack));

    [Fact]
    public void Firewood_holds_2048_and_draws_as_before()
    {
        var big = BigPileRules.Big(Firewood, 32);
        Assert.Equal(2048, big.StackingCapacity);
        Assert.Equal(128, big.ItemsPerModel);
        Assert.Equal(1, big.CuboidsPerModel);
        Assert.Equal(2, big.TransferQuantity);
        Assert.Equal(32, big.BulkTransferQuantity);
        Assert.Equal(0.5f / 64, big.ModelItemsToStackSizeRatio);
    }

    public static TheoryData<string, int> Piles() => new()
    {
        { nameof(Firewood), 32 }, { nameof(Ingot), 16 }, { nameof(Stone), 64 }, { nameof(RawBrick), 64 },
        { nameof(MorePilesCube), 16 }, { nameof(MorePilesCube), 32 }, { nameof(MorePilesCube), 64 },
        { nameof(MorePilesLog), 32 }, { nameof(Snowball), 64 },
    };

    private static BigPileRules.Pile Named(string name) => name switch
    {
        nameof(Firewood) => Firewood,
        nameof(Ingot) => Ingot,
        nameof(Stone) => Stone,
        nameof(RawBrick) => RawBrick,
        nameof(MorePilesCube) => MorePilesCube,
        nameof(MorePilesLog) => MorePilesLog,
        _ => Snowball,
    };

    /// <summary>The full big pile draws the elements, and stands as tall, as the full pile did.</summary>
    [Theory]
    [MemberData(nameof(Piles))]
    public void A_full_big_pile_looks_like_a_full_pile(string name, int maxStack)
    {
        var old = Named(name);
        var big = BigPileRules.Big(old, maxStack);
        Assert.Equal(old.CuboidsPerModel * BigPileRules.Models(old.StackingCapacity, old.ItemsPerModel),
            big.CuboidsPerModel * BigPileRules.Models(big.StackingCapacity, big.ItemsPerModel));
        Assert.Equal(BigPileRules.Layers(old.CbScaleYByLayer, old.StackingCapacity),
            BigPileRules.Layers(big.CbScaleYByLayer, big.StackingCapacity));
        // A burning pile's flames: one block up when full.
        Assert.Equal(1, BigPileRules.Models(big.StackingCapacity, big.ItemsPerModel) * big.ItemsPerModel / big.StackingCapacity);
    }

    /// <summary>A pile at a share of its capacity draws that share of the full pile, give or take a
    /// model, as the old pile did at the same share.</summary>
    [Theory]
    [MemberData(nameof(Piles))]
    public void A_part_filled_big_pile_shows_its_share(string name, int maxStack)
    {
        var old = Named(name);
        var big = BigPileRules.Big(old, maxStack);
        int fullModels = BigPileRules.Models(big.StackingCapacity, big.ItemsPerModel);
        foreach (double share in new[] { 0.01, 0.25, 0.5, 0.75, 0.99 })
        {
            int items = Math.Max(1, (int)(big.StackingCapacity * share));
            int models = BigPileRules.Models(items, big.ItemsPerModel);
            Assert.InRange(models, Math.Floor(fullModels * share), Math.Ceiling(fullModels * share) + 1);
            if (big.CbScaleYByLayer != 0)
            {
                int fullLayers = BigPileRules.Layers(big.CbScaleYByLayer, big.StackingCapacity);
                Assert.InRange(BigPileRules.Layers(big.CbScaleYByLayer, items), Math.Floor(fullLayers * share), Math.Ceiling(fullLayers * share) + 1);
            }
        }
        // One item still draws one model.
        Assert.Equal(1, BigPileRules.Models(1, big.ItemsPerModel));
    }

    [Fact]
    public void Ctrl_click_moves_a_stack_never_less_than_before()
    {
        Assert.Equal(16, BigPileRules.Big(Ingot, 16).BulkTransferQuantity);
        Assert.Equal(64, BigPileRules.Big(MorePilesCube, 64).BulkTransferQuantity);
        // A stack of one keeps the four it moved; its capacity is 64 already, so nothing changes.
        Assert.Equal(Ingot, BigPileRules.Big(Ingot, 1));
    }

    [Fact]
    public void A_collision_scale_of_zero_stays_zero() =>
        Assert.Equal(0f, BigPileRules.Big(Ingot, 16).CbScaleYByLayer);

    [Fact]
    public void Items_per_model_keeps_the_model_count_where_it_can_and_never_draws_more()
    {
        // 11 models over 4096 items: 373 a model draws 11.
        Assert.Equal(373, BigPileRules.ItemsPerModel(11, 1, 4096));
        Assert.Equal(11, BigPileRules.Models(4096, 373));
        // 6 models over 10 items: no whole number gives 6; 2 a model draws 5.
        Assert.Equal(2, BigPileRules.ItemsPerModel(6, 1, 10));
        Assert.Equal(5, BigPileRules.Models(10, 2));
    }
}
