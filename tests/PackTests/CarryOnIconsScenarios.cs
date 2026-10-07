using System.Reflection;
using Atlas.XUnit;
using SeraphHorizons.Mod;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, CarryOnIconsPerWorld (#401): the tweak clears Carry On's icon stack
/// fields when the client leaves a world. A headless server neither renders nor leaves a world, so
/// this holds what the tweak relies on instead: the pinned Carry On has exactly the static item stack
/// fields the tweak knows (a bump that adds or renames one fails here), Carry On's interaction help
/// fills them only while they are null, and after <see cref="CarryOnIcons.Clear"/> it builds them
/// again from the world's items.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Carry_On_icon_stacks_are_cleared_and_built_again()
    {
        var assembly = CarryOnIcons.CarryOnAssembly(World.Api)
            ?? throw new Xunit.Sdk.XunitException("Carry On's mod system is not loaded");
        var holding = assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => HoldsItems(f.FieldType))
            .Select(f => $"{f.DeclaringType!.FullName}.{f.Name}")
            .Order().ToList();
        Assert.Equal(CarryOnIcons.Fields.Select(f => $"{f.Type}.{f.Field}").Order(), holding);

        var fields = CarryOnIcons.Find(World.Api)!;
        Assert.Equal(CarryOnIcons.Fields.Length, fields.Count);

        var pos = World.Spawn.AddCopy(-47, 14, -83);
        World.SetBlock("game:chest-east", pos);
        var chest = W.BlockAccessor.GetBlock(pos);
        var carryable = chest.BlockBehaviors.Single(b => b.GetType().FullName == "CarryOn.Common.Behaviors.BlockBehaviorCarryable");
        var handsfree = fields.Single(f => f.DeclaringType!.Name == "CarryableInteractionHelpBuilder" && f.Name == "handsfreeStacks");
        // No player (Carry On's builder takes null, and builds both stacks either way): the shared
        // world's server takes 16 players at most, and its other scenarios use them all.
        WorldInteraction[] Help()
        {
            var handling = EnumHandling.PassThrough;
            return carryable.GetPlacedBlockInteractionHelp(W, new BlockSelection { Position = pos, Block = chest }, null!, ref handling);
        }

        // The help builder is the client's: Carry On initialises it in StartClientSide only, so it is
        // initialised here as the client does, and put back afterwards.
        var builder = handsfree.DeclaringType!;
        var state = builder.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.Name is "carryManager" or "configProvider").ToList();
        Assert.Equal(2, state.Count);
        var before = state.Select(f => f.GetValue(null)).ToList();
        var carrySystem = World.Api.ModLoader.GetModSystem(CarryOnIcons.CarrySystemName);
        builder.GetMethod("Init")!.Invoke(null, [carrySystem.GetType().GetProperty("CarryManager")!.GetValue(carrySystem)]);
        try
        {
            Help();
            var built = (ItemStack[])handsfree.GetValue(null)!;
            Assert.Equal("carryon:icon-handsfree", built[0].Collectible.Code.ToString());
            Help();
            Assert.Same(built, handsfree.GetValue(null));

            // Every field is filled, as after a world in which all of Carry On's help was shown.
            foreach (var f in fields)
                f.SetValue(null, f.GetValue(null) ?? new[] { new ItemStack(W.GetItem(new AssetLocation("carryon:icon-take"))) });
            Assert.Equal(fields.Count, CarryOnIcons.Clear(fields));
            Assert.All(fields, f => Assert.Null(f.GetValue(null)));

            Assert.NotEmpty(Help());
            var rebuilt = (ItemStack[])handsfree.GetValue(null)!;
            Assert.NotSame(built, rebuilt);
            Assert.Same(W.GetItem(new AssetLocation("carryon:icon-handsfree")), rebuilt[0].Item);
        }
        finally
        {
            CarryOnIcons.Clear(fields);
            for (int i = 0; i < state.Count; i++)
                state[i].SetValue(null, before[i]);
        }
    }

    /// <summary>Whether a field of this type can hold an item stack or a collectible. Delegates (the
    /// compiler's cached lambdas, <c>Func&lt;Block, bool&gt;</c>) take them as arguments, not hold them.</summary>
    private static bool HoldsItems(Type t) =>
        !typeof(Delegate).IsAssignableFrom(t)
        && (typeof(ItemStack).IsAssignableFrom(t) || typeof(CollectibleObject).IsAssignableFrom(t)
            || (t.HasElementType && HoldsItems(t.GetElementType()!))
            || (t.IsGenericType && t.GetGenericArguments().Any(HoldsItems)));
}
