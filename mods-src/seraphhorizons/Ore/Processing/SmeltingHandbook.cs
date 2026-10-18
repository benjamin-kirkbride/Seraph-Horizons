using System.Reflection;
using System.Reflection.Emit;
using Cairo;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The handbook's smelting counts (README "Ore processing: the handbook's smelting counts"), on every page:
/// <list type="bullet">
/// <item>"Created by" draws each smelting input with the game's <c>smeltedRatio</c> as its count, the
/// inputs per smelted stack, which the pack's exact rates make 20,000 (for 51 bullion), and a row of such
/// numbers runs together. Every smelting slot of that section here shows the inputs for one output,
/// rounded up (<see cref="SmeltCount.PerOne"/>; a mold's row, the inputs for what the mold takes,
/// <see cref="SmeltCount.ForUnits"/>, where the game counts every input as a nugget's 5 units),
/// abbreviated from 1,000 (<see cref="SmeltCount.Abbreviated"/>), with the slots a little apart.</item>
/// <item>The bloomery's section leaves out what smelts to bullion, which the bloomery refuses
/// (<see cref="BloomeryBullion"/>), and a new "Smelting in crucible" section lists what the crucible
/// smelts into the page's item where the game lists nothing (bullion: the game lists the crucible's inputs
/// only for an ingot, under metal molding): every stack whose smelted stack is the page's item, needs a
/// container, and melts at a fuel's heat (<see cref="CrucibleInputs"/>).</item>
/// <item>"Smelts into" on an input's page drew the output with its stack size, so one rock "smelted into"
/// 51 bullion: it now draws one output and says how many inputs make it.</item>
/// </list>
/// Harmony on the game's <c>CollectibleBehaviorHandbookTextAndExtraInfo</c>, client side: a prefix on
/// <c>AddSlideShowComponent</c> that takes over the bloomery's slots (the call whose last component is
/// the bloomery's sub-heading), and postfixes on <c>addCreatedByInfo</c> (the molds' rows, the crucible
/// section) and <c>addProcessesIntoInfo</c>. Each checks the game's method first and warns when it
/// changed. Not switchable: it says what the numbers are, with or without ore processing.
/// </summary>
public static class SmeltingHandbook
{
    public const string HarmonyId = "seraphhorizons.smeltinghandbook";

    /// <summary>The crucible section's sub-heading and the "Smelts into" count's lang keys.</summary>
    public const string CrucibleKey = "seraphhorizons:handbook-createdby-cruciblesmelting",
        PerOneKey = "seraphhorizons:handbook-smeltsinto-perone";

    /// <summary>The game's lang keys the sections are found by.</summary>
    public const string BloomeryKey = "handbook-createdby-bloomerysmelting", MoldingKey = "handbook-createdby-metalmolding",
        CreatedByKey = "Created by", SmeltDescPrefix = "game:smeltdesc-";

    /// <summary>The space before every slot but a row's first, unscaled (the game's is 0, its first's 2).</summary>
    public const double SlotGap = 6;

    private static ICoreClientAPI? _capi;
    private static List<RichTextComponentBase>? _strayNewlineIn;
    private static int _strayNewlineAt = -1;
    private static readonly Dictionary<string, LoadedTexture> Labels = new();

    private static readonly FieldInfo? SlideshowClick = AccessTools.Field(typeof(SlideshowItemstackTextComponent), "onStackClicked");
    private static readonly FieldInfo? ItemstackSlot = AccessTools.Field(typeof(ItemstackTextComponent), "slot");

    private static readonly Type Handbook = typeof(CollectibleBehaviorHandbookTextAndExtraInfo);

    public static MethodInfo? CreatedByTarget() =>
        AccessTools.DeclaredMethod(Handbook, "addCreatedByInfo",
            [typeof(ICoreClientAPI), typeof(ItemStack[]), typeof(ActionConsumable<string>), typeof(ItemStack),
             typeof(List<RichTextComponentBase>), typeof(float), typeof(List<ItemStack>), typeof(List<ItemStack>),
             typeof(List<ItemStack>), typeof(List<ItemStack>), typeof(bool)]);

    public static MethodInfo? SlideShowTarget() =>
        AccessTools.DeclaredMethod(Handbook, nameof(CollectibleBehaviorHandbookTextAndExtraInfo.AddSlideShowComponent),
            [typeof(List<RichTextComponentBase>), typeof(ICoreClientAPI), typeof(List<ItemStack>), typeof(ActionConsumable<string>), typeof(bool)]);

    public static MethodInfo? ProcessesIntoTarget() =>
        AccessTools.DeclaredMethod(Handbook, "addProcessesIntoInfo",
            [typeof(ICoreClientAPI), typeof(ActionConsumable<string>), typeof(ItemStack), typeof(List<RichTextComponentBase>),
             typeof(float), typeof(float), typeof(List<ItemStack>), typeof(List<ItemStack>), typeof(bool)]);

    /// <summary>Whether the game's <c>addCreatedByInfo</c> is as this expects: the bloomery's and the molds'
    /// sub-headings, the "Created by" heading and its slots drawn by <c>AddSlideShowComponent</c>.</summary>
    public static bool CreatedByMatches() =>
        CreatedByTarget() is { } createdBy && SlideShowTarget() is { } slideShow && ItemstackSlot != null && SlideshowClick != null
        && Loads(createdBy, BloomeryKey) && Loads(createdBy, MoldingKey) && Loads(createdBy, CreatedByKey) && Calls(createdBy, slideShow);

    /// <summary>Whether the game's <c>addProcessesIntoInfo</c> is as this expects: a "Smelts into" heading.</summary>
    public static bool ProcessesIntoMatches() =>
        ProcessesIntoTarget() is { } processesInto && ItemstackSlot != null && Loads(processesInto, SmeltDescPrefix);

    /// <summary>Patches the handbook (the client's; the patches do nothing until <see cref="Bind"/>). False when
    /// nothing could be patched; each part that is not as expected is left as the game has it, with a warning.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        bool any = false;
        if (!CreatedByMatches())
            logger.Warning("[seraphhorizons] Smelting handbook: CollectibleBehaviorHandbookTextAndExtraInfo.addCreatedByInfo is not as "
                           + "expected; the game changed, so \"Created by\" shows the game's smelting ratios and no crucible section");
        else
        {
            harmony.Patch(SlideShowTarget(), prefix: new HarmonyMethod(typeof(SmeltingHandbook), nameof(SlideShowPrefix)));
            harmony.Patch(CreatedByTarget(), postfix: new HarmonyMethod(typeof(SmeltingHandbook), nameof(CreatedByPostfix)));
            any = true;
        }
        if (!ProcessesIntoMatches())
            logger.Warning("[seraphhorizons] Smelting handbook: CollectibleBehaviorHandbookTextAndExtraInfo.addProcessesIntoInfo is not as "
                           + "expected; the game changed, so \"Smelts into\" draws the output's stack size");
        else
        {
            harmony.Patch(ProcessesIntoTarget(), prefix: new HarmonyMethod(typeof(SmeltingHandbook), nameof(ProcessesIntoPrefix)),
                postfix: new HarmonyMethod(typeof(SmeltingHandbook), nameof(ProcessesIntoPostfix)));
            any = true;
        }
        return any;
    }

    /// <summary>Client: the patches act from now on.</summary>
    public static void Bind(ICoreClientAPI capi) => _capi = capi;

    public static void Unbind()
    {
        _capi = null;
        _strayNewlineIn = null;
        foreach (var t in Labels.Values) t.Dispose();
        Labels.Clear();
    }

    private static bool Loads(MethodBase method, string text) =>
        PatchProcessor.GetOriginalInstructions(method).Any(i => i.opcode == OpCodes.Ldstr && i.operand as string == text);

    private static bool Calls(MethodBase method, MethodInfo callee) =>
        PatchProcessor.GetOriginalInstructions(method).Any(i => i.Calls(callee));

    // ---- What a slot counts (both sides, so the pack tests can check it on the server) ----

    /// <summary>The size of a smelting's output: its resolved stack's, else its stack's.</summary>
    public static int OutputSize(CombustibleProperties props) =>
        props.SmeltedStack?.ResolvedItemstack?.StackSize ?? props.SmeltedStack?.StackSize ?? 1;

    /// <summary>Inputs of <paramref name="stack"/> that make one of its smelted stack.</summary>
    public static int PerOne(IWorldAccessor world, ItemStack stack) =>
        stack.Collectible.GetCombustibleProperties(world, stack, null) is { } p ? SmeltCount.PerOne(p.SmeltedRatio, OutputSize(p)) : 1;

    /// <summary>Whether the bloomery takes a smelting by its heat, as the game's bloomery and handbook decide.</summary>
    public static bool BloomeryHeat(CombustibleProperties? props) =>
        props?.SmeltedStack?.ResolvedItemstack != null && props.MeltingPoint is >= BlockEntityBloomery.MinTemp and < BlockEntityBloomery.MaxTemp;

    /// <summary>
    /// The bloomery section's inputs: <paramref name="listed"/> (the game's, each drawn at its ratio) without
    /// what smelts to bullion, each a copy counting the inputs for one output.
    /// </summary>
    public static List<ItemStack> BloomeryInputs(IWorldAccessor world, IEnumerable<ItemStack?> listed)
    {
        var kept = new List<ItemStack>();
        foreach (var s in listed)
        {
            if (s?.Collectible == null || BloomeryBullion.SmeltsToBullion(world, s))
                continue;
            var c = s.Clone();
            c.StackSize = PerOne(world, s);
            kept.Add(c);
        }
        return kept;
    }

    /// <summary>
    /// The crucible section's inputs for <paramref name="page"/>: every stack of <paramref name="all"/> but the
    /// page's own that smelts (smelting type <c>smelt</c>) into it in a container, at a heat a fuel reaches
    /// (<paramref name="hottestFuel"/>, °C), and that the bloomery section does not already list; each once, a
    /// copy counting the inputs for one output. None for an ingot, whose crucible inputs the game lists under
    /// metal molding.
    /// </summary>
    public static List<ItemStack> CrucibleInputs(IWorldAccessor world, ItemStack page, IEnumerable<ItemStack> all, int hottestFuel)
    {
        var found = new List<ItemStack>();
        if (page.Collectible is ItemIngot)
            return found;
        foreach (var s in all)
        {
            if (s?.Collectible == null || s.Equals(world, page, GlobalConstants.IgnoredStackAttributes))
                continue;
            var props = s.Collectible.GetCombustibleProperties(world, s, null);
            if (props is not { RequiresContainer: true, SmeltingType: EnumSmeltType.Smelt } || props.MeltingPoint > hottestFuel
                || props.SmeltedStack?.ResolvedItemstack is not { } output
                || !output.Equals(world, page, GlobalConstants.IgnoredStackAttributes)
                || (BloomeryHeat(props) && !BloomeryBullion.SmeltsToBullion(props))
                || found.Any(f => f.Equals(world, s, GlobalConstants.IgnoredStackAttributes)))
                continue;
            var c = s.Clone();
            c.StackSize = SmeltCount.PerOne(props.SmeltedRatio, OutputSize(props));
            found.Add(c);
        }
        return found;
    }

    /// <summary>The hottest of <paramref name="fuels"/>' burn temperatures (°C), as the game's handbook reads them.</summary>
    public static int HottestFuel(IWorldAccessor world, IEnumerable<ItemStack> fuels) =>
        fuels.Select(f => f.Collectible?.GetCombustibleProperties(world, f, null)?.BurnTemperature ?? 0).DefaultIfEmpty(0).Max();

    // ---- The patches ----

    private static bool SlideShowPrefix(List<RichTextComponentBase> components, ICoreClientAPI capi, List<ItemStack> stacks,
        ActionConsumable<string> openDetailPageFor, bool showStackSize)
    {
        if (!showStackSize || _capi == null || components.Count == 0 || !IsSubHeading(components[^1], BloomeryKey))
            return true;
        try
        {
            var kept = BloomeryInputs(capi.World, stacks);
            stacks.Clear();
            if (kept.Count == 0)
            {
                // Nothing left but bullion: no section (the game adds its line break next, taken off after).
                components.RemoveAt(components.Count - 1);
                if (components.Count > 0 && components[^1] is ClearFloatTextComponent)
                    components.RemoveAt(components.Count - 1);
                _strayNewlineIn = components;
                _strayNewlineAt = components.Count;
                return false;
            }
            AddSlots(components, capi, kept, openDetailPageFor);
            return false;
        }
        catch (Exception e)
        {
            capi.Logger.Error("[seraphhorizons] Smelting handbook: could not draw the bloomery's inputs: {0}", e);
            return true;
        }
    }

    private static void CreatedByPostfix(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor,
        ItemStack stack, List<RichTextComponentBase> components, List<ItemStack> fuels, bool haveText, ref bool __result)
    {
        if (_capi == null || stack?.Collectible == null)
            return;
        try
        {
            if (ReferenceEquals(_strayNewlineIn, components) && _strayNewlineAt < components.Count
                && components[_strayNewlineAt] is RichTextComponent { DisplayText: "\n" })
                components.RemoveAt(_strayNewlineAt);
            _strayNewlineIn = null;
            CountMolds(capi, components);
            var crucible = CrucibleInputs(capi.World, stack, allStacks, HottestFuel(capi.World, fuels));
            int heading = components.FindLastIndex(c => c is RichTextComponent rt && rt.DisplayText == Lang.Get(CreatedByKey) + "\n");
            if (crucible.Count == 0)
            {
                // The bloomery's was the only section and it is gone: no heading either.
                if (heading >= 0 && heading == components.Count - 1)
                {
                    components.RemoveAt(heading);
                    // AddHeading put a space before it when there was text already.
                    if (haveText && heading > 0 && components[heading - 1] is ClearFloatTextComponent)
                        components.RemoveAt(heading - 1);
                    __result = haveText;
                }
                return;
            }
            bool first = heading < 0 || heading == components.Count - 1;
            if (heading < 0)
            {
                bool had = __result;
                CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi, CreatedByKey, ref had);
                __result = had;
            }
            components.Add(new ClearFloatTextComponent(capi, first ? 3f : 7f));
            CollectibleBehaviorHandbookTextAndExtraInfo.AddSubHeading(components, capi, openDetailPageFor, CrucibleKey, null);
            AddSlots(components, capi, crucible, openDetailPageFor);
            components.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
        }
        catch (Exception e)
        {
            capi.Logger.Error("[seraphhorizons] Smelting handbook: could not finish {0}'s \"Created by\": {1}", stack.Collectible.Code, e);
        }
    }

    // The molds' rows: inputs + mold = cast. The game counts each input at a nugget's 5 units.
    private static void CountMolds(ICoreClientAPI capi, List<RichTextComponentBase> components)
    {
        int link = components.FindIndex(c => c is LinkTextComponent lt && lt.DisplayText.Trim() == Lang.Get(MoldingKey).Trim());
        if (link < 0)
            return;
        for (int i = link + 1; i + 2 < components.Count && components[i] is not ClearFloatTextComponent; i++)
        {
            if (components[i] is not SlideshowItemstackTextComponent inputs
                || components[i + 1] is not RichTextComponent { DisplayText: var plus } || plus.Trim() != "+"
                || components[i + 2] is not SlideshowItemstackTextComponent { Itemstacks: [var mold, ..] })
                continue;
            int units = mold.ItemAttributes?["requiredUnits"].AsInt(100) ?? 100;
            bool big = false;
            foreach (var s in inputs.Itemstacks)
            {
                if (s?.Collectible.GetCombustibleProperties(capi.World, s, null) is not { } p)
                    continue;
                s.StackSize = SmeltCount.ForUnits(units, p.SmeltedRatio, OutputSize(p));
                big |= s.StackSize >= 1000;
            }
            if (big && SlideshowClick?.GetValue(inputs) is Action<ItemStack> click)
                components[i] = new CountedSlideshow(capi, inputs.Itemstacks, click) { PaddingLeft = inputs.PaddingLeft };
        }
    }

    private static void ProcessesIntoPrefix(List<RichTextComponentBase> components, out int __state) => __state = components.Count;

    private static void ProcessesIntoPostfix(ICoreClientAPI capi, ItemStack stack, List<RichTextComponentBase> components, int __state)
    {
        if (_capi == null || stack?.Collectible == null)
            return;
        try
        {
            if (stack.Collectible.GetCombustibleProperties(capi.World, stack, null) is not { SmeltedStack.ResolvedItemstack: { } output } props
                || props.SmeltedRatio <= 1 || output.StackSize >= props.SmeltedRatio)
                return;
            for (int i = __state; i < components.Count; i++)
            {
                if (components[i] is not ItemstackTextComponent shown || ItemstackSlot?.GetValue(shown) is not ItemSlot slot
                    || !ReferenceEquals(slot.Itemstack, output))
                    continue;
                var one = output.Clone();
                one.StackSize = 1;
                slot.Itemstack = one;
                components.Insert(i + 1, new RichTextComponent(capi,
                    Lang.Get(PerOneKey, SmeltCount.Abbreviated(SmeltCount.PerOne(props.SmeltedRatio, output.StackSize))), CairoFont.WhiteSmallText())
                {
                    VerticalAlign = EnumVerticalAlign.Middle,
                });
                return;
            }
        }
        catch (Exception e)
        {
            capi.Logger.Error("[seraphhorizons] Smelting handbook: could not count {0}'s \"Smelts into\": {1}", stack.Collectible.Code, e);
        }
    }

    // ---- Drawing ----

    private static bool IsSubHeading(RichTextComponentBase c, string key) =>
        c is RichTextComponent rt && rt.GetType() == typeof(RichTextComponent) && rt.DisplayText.Trim() == ("• " + Lang.Get(key)).Trim();

    /// <summary>Slots for <paramref name="stacks"/>, grouped as the game groups them (its handbook
    /// <c>groupBy</c>), a little apart.</summary>
    private static void AddSlots(List<RichTextComponentBase> components, ICoreClientAPI capi, List<ItemStack> stacks,
        ActionConsumable<string> openDetailPageFor)
    {
        double padding = 2;
        while (stacks.Count > 0)
        {
            var s = stacks[0];
            stacks.RemoveAt(0);
            components.Add(new CountedSlideshow(capi, s, stacks, cs => openDetailPageFor(PageCode(capi, cs))) { PaddingLeft = padding });
            padding = SlotGap;
        }
    }

    private static string PageCode(ICoreClientAPI capi, ItemStack stack) =>
        stack.Collectible.GetCollectibleInterface<IHandBookPageCodeProvider>()?.HandbookPageCodeForStack(capi.World, stack)
        ?? GuiHandbookItemStackPage.PageCodeForStack(stack);

    /// <summary>A count drawn as the game draws a stack size, abbreviated; one texture per text and size.</summary>
    internal static LoadedTexture Label(ICoreClientAPI capi, string text, float scale)
    {
        string key = $"{text}-{(int)(scale * 100)}-{RuntimeEnv.GUIScale}";
        if (Labels.TryGetValue(key, out var tex))
            return tex;
        var font = CairoFont.WhiteSmallText().WithFontSize((float)GuiStyle.SmallishFontSize);
        font.FontWeight = FontWeight.Bold;
        font.Color = [1, 1, 1, 1];
        font.StrokeColor = [0, 0, 0, 1];
        font.StrokeWidth = RuntimeEnv.GUIScale + 0.25;
        font.UnscaledFontsize *= scale;
        return Labels[key] = capi.Gui.TextTexture.GenTextTexture(text, font);
    }
}

/// <summary>
/// A handbook slideshow slot that draws its stack's size abbreviated (<see cref="SmeltCount.Abbreviated"/>)
/// where the game draws the whole number, at the game's place and in its font.
/// </summary>
internal sealed class CountedSlideshow : SlideshowItemstackTextComponent
{
    private const double Size = 40;

    public CountedSlideshow(ICoreClientAPI capi, ItemStack group, List<ItemStack> all, Action<ItemStack> onClick)
        : base(capi, group, all, Size, EnumFloat.Inline, onClick) => ShowStackSize = false;

    public CountedSlideshow(ICoreClientAPI capi, ItemStack[] stacks, Action<ItemStack> onClick)
        : base(capi, stacks, Size, EnumFloat.Inline, onClick) => ShowStackSize = false;

    public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
    {
        base.RenderInteractiveElements(deltaTime, renderX, renderY, renderZ);
        if (slot.Itemstack is not { StackSize: not 1 } stack)
            return;
        var line = BoundsPerLine[0];
        float size = (float)line.Width * renderSize;
        double x = renderX + line.X + line.Width * 0.5 + renderOffset.X + offX;
        double y = renderY + line.Y + line.Height * 0.5 + renderOffset.Y + offY;
        float scale = size / (float)GuiElement.scaled(25.6);
        var label = SmeltingHandbook.Label(capi, SmeltCount.Abbreviated(stack.StackSize), scale);
        capi.Render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
        capi.Render.Render2DLoadedTexture(label, (int)(x + size + 1 - label.Width),
            (int)(y + scale * GuiElement.scaled(3) - GuiElement.scaled(4)), 200 + renderOffset.Z);
        capi.Render.GlToggleBlend(true);
    }
}
