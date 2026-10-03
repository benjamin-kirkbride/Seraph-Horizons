using System.Reflection;
using SeraphHorizons.Mod.TidyVariants.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TidyVariants;

/// <summary>
/// The handbook side of Tidy Variants (client only, no Harmony; docs/variant-grouping/handbook.md).
/// <list type="number">
/// <item>On <see cref="TidyVariantsModSystem.Resolved"/> (client <c>AssetsFinalize</c>, before the handbook gathers
/// its stacks at the <c>LevelFinalize</c> event): writes one <c>groupBy</c> per grouped collectible and
/// <c>exclude</c> on fully hidden ones (<see cref="HandbookAttributes"/>).</item>
/// <item>Once the handbook has built its pages (and again whenever it rebuilds them): picks one listed page per
/// group, takes the other member pages and hidden variants' pages out of the list by setting the page's
/// <c>isDuplicate</c> (per page, so links still open them), gives every member page a "variants" section
/// (<see cref="VariantsPageContent"/>), adds member names to the representative's search text, and labels its
/// list entry with the group title and size.</item>
/// </list>
/// Any failure is logged and leaves that part vanilla.
/// </summary>
public sealed class TidyHandbookSystem : ModSystem
{
    // Game internals read by reflection (verified against 1.22.7; a missing one turns the list collapse off).
    static readonly FieldInfo? DialogField = typeof(ModSystemSurvivalHandbook).GetField("dialog", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? PagesField = typeof(GuiDialogHandbook).GetField("allHandbookPages", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? LoadingField = typeof(GuiDialogHandbook).GetField("loadingPagesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? DuplicateField = typeof(GuiHandbookItemStackPage).GetField("isDuplicate", BindingFlags.Instance | BindingFlags.NonPublic);

    ICoreClientAPI? capi;
    TidyBridge? bridge;
    HandbookLayout? layout;
    HashSet<CollectibleObject> excluded = [];
    long tickId;
    bool disabled;

    // The page set last applied to (pages are rebuilt on hotkey changes and .debug reloadhandbook).
    List<GuiHandbookPage>? appliedList;
    int appliedCount = -1;
    GuiHandbookPage? appliedLast;
    // Representative pages whose list label we draw: page, label, our texture.
    readonly List<(GuiHandbookItemStackPage Page, string Label, LoadedTexture? Texture)> labels = [];

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!TidyVariantsModSystem.Enabled(api)) return;
        capi = api;
        TidyVariantsModSystem.Resolved += OnResolved;
    }

    void OnResolved(TidyBridge b)
    {
        if (b.Side != EnumAppSide.Client || capi is null) return;
        try
        {
            bridge = b;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            layout = HandbookLayout.Build(b.Resolution);
            var r = HandbookAttributes.Apply(b, layout.Attributes, capi.Logger);
            sw.Stop();
            excluded = [];
            foreach (var e in layout.Attributes.Entries)
                if (e.Exclude)
                {
                    var coll = e.Kind == EntryKind.Block ? (CollectibleObject?)capi.World.GetBlock(new AssetLocation(e.Code)) : capi.World.GetItem(new AssetLocation(e.Code));
                    if (coll is not null) excluded.Add(coll);
                }
            var issues = layout.Attributes.Issues;
            capi.Logger.Notification("[seraphhorizons] Tidy Variants handbook: groupBy on {0} collectibles ({1} replaced a shipped one), exclude on {2}, {3} failed; {4} groupby-inexact, {5} groupby-conflict; {6} ms",
                r.GroupBy, r.ReplacedShipped, r.Exclude, r.Failed, issues.Count(i => i.Kind == "groupby-inexact"), issues.Count(i => i.Kind == "groupby-conflict"), sw.ElapsedMilliseconds);
            foreach (var i in issues) capi.Logger.VerboseDebug("[seraphhorizons] Tidy Variants: {0}: {1}", i.Kind, i.Message);

            if (DialogField is null || PagesField is null || LoadingField is null || DuplicateField is null)
            {
                capi.Logger.Warning("[seraphhorizons] Tidy Variants handbook: a game field this mod reads is missing; the handbook list stays vanilla (groupBy is still written)");
                return;
            }
            if (tickId == 0) tickId = capi.Event.RegisterGameTickListener(OnTick, 50);
        }
        catch (Exception ex)
        {
            capi.Logger.Error("[seraphhorizons] Tidy Variants handbook: setup failed; the handbook stays vanilla: {0}", ex);
        }
    }

    void OnTick(float dt)
    {
        if (disabled || capi is null) return;
        try
        {
            var system = capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
            if (system is null || DialogField!.GetValue(system) is not GuiDialogHandbook dialog) return;
            if ((bool)LoadingField!.GetValue(dialog)!) return;
            if (PagesField!.GetValue(dialog) is not List<GuiHandbookPage> pages || pages.Count == 0) return;

            if (!ReferenceEquals(pages, appliedList) || pages.Count != appliedCount || !ReferenceEquals(pages[^1], appliedLast))
            {
                appliedList = pages; appliedCount = pages.Count; appliedLast = pages[^1];
                Apply(pages);
            }
            if (dialog.IsOpened()) RefreshLabels();
        }
        catch (Exception ex)
        {
            disabled = true;
            capi.Logger.Error("[seraphhorizons] Tidy Variants handbook: page update failed; stopped (pages already changed stay changed): {0}", ex);
        }
    }

    /// <summary>Applies the layout to a freshly built page list; on failure undoes its own page changes.</summary>
    void Apply(List<GuiHandbookPage> pages)
    {
        var b = bridge!; var l = layout!; var api = capi!;
        ClearLabels();
        VariantsPageContent.Views = new Dictionary<string, HandbookGroupView>();

        var byCode = new Dictionary<string, GuiHandbookItemStackPage>(StringComparer.Ordinal);
        int listedBefore = 0;
        foreach (var p in pages)
        {
            if (p is not GuiHandbookItemStackPage sp) continue;
            byCode[sp.PageCode] = sp; // last wins, like the dialog's own page-code index
            if (!sp.IsDuplicate) listedBefore++;
        }

        string? PageOf(int entry)
        {
            string code = GuiHandbookItemStackPage.PageCodeForStack(b.StackOf(entry));
            return byCode.ContainsKey(code) ? code : null;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var collapse = l.Collapse(PageOf, code => CanRepresent(byCode[code]));

        // Pages of fully hidden collectibles that a mod's GetHandBookStacks listed despite exclude.
        var repPages = collapse.Groups.Select(g => g.RepresentativePage).ToHashSet(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(collapse.DuplicatePages, StringComparer.Ordinal);
        foreach (var (code, sp) in byCode)
            if (excluded.Contains(sp.Stack.Collectible) && !repPages.Contains(code)) duplicates.Add(code);

        var flipped = new List<GuiHandbookItemStackPage>();
        try
        {
            foreach (var code in duplicates)
            {
                var sp = byCode[code];
                if (sp.IsDuplicate) continue;
                DuplicateField!.SetValue(sp, true);
                flipped.Add(sp);
            }

            var views = new Dictionary<string, HandbookGroupView>(StringComparer.Ordinal);
            foreach (var g in collapse.Groups)
            {
                var rep = byCode[g.RepresentativePage];
                var members = g.MemberPages.Select(m => (byCode[m.Page].Stack, m.Page)).ToList();
                string repName = rep.Stack.GetName();
                string title = GroupTitles.Of(b, g.Group.Group, repName);
                var view = new HandbookGroupView(title, members);
                foreach (var (stack, code) in members)
                {
                    views[code] = view;
                    AttachSection(stack.Collectible);
                }

                // Search: the group's page also matches its members' names, and its title is the group title
                // (lang or derived; the representative's own name stays in TextCacheAll).
                var names = g.MemberPages.Skip(1).Select(m => byCode[m.Page].TextCacheTitle).Distinct();
                rep.TextCacheAll = rep.TextCacheAll + " " + string.Join(" ", names);
                if (title != repName)
                {
                    rep.TextCacheTitle = title.ToSearchFriendly();
                    rep.TextCacheAll = title.ToSearchFriendly() + " " + rep.TextCacheAll;
                }
                labels.Add((rep, Lang.Get("seraphhorizons:tidyvariants-handbook-list-entry", title, members.Count), null));
            }
            VariantsPageContent.Views = views;

            int listedAfter = byCode.Values.Count(p => !p.IsDuplicate);
            api.Logger.Notification("[seraphhorizons] Tidy Variants handbook: {0} groups collapsed ({1} left alone: no member page could represent them), {2} pages left the list; stack pages listed {3} -> {4}; {5} ms",
                collapse.Groups.Count, collapse.NoRepresentative, flipped.Count, listedBefore, listedAfter, sw.ElapsedMilliseconds);
        }
        catch
        {
            foreach (var sp in flipped) DuplicateField!.SetValue(sp, false);
            VariantsPageContent.Views = new Dictionary<string, HandbookGroupView>();
            ClearLabels();
            throw;
        }
    }

    /// <summary>A page can be a group's only list entry if it is listed, is a plain stack page, and its collectible
    /// will show <see cref="VariantsPageContent"/> (vanilla page text, no other custom page content).</summary>
    static bool CanRepresent(GuiHandbookItemStackPage page)
    {
        if (page.GetType() != typeof(GuiHandbookItemStackPage) || page.IsDuplicate || page.Stack?.Collectible is not { } coll) return false;
        if (coll is ICustomHandbookPageContent) return false;
        var custom = coll.GetCollectibleInterface<ICustomHandbookPageContent>();
        if (custom is not null and not VariantsPageContent) return false;
        return coll.GetBehavior<CollectibleBehaviorHandbookTextAndExtraInfo>() is not null;
    }

    static void AttachSection(CollectibleObject coll)
    {
        if (coll.CollectibleBehaviors.Any(b => b is VariantsPageContent)) return;
        coll.CollectibleBehaviors = coll.CollectibleBehaviors.Append(new VariantsPageContent(coll));
    }

    /// <summary>The list draws a page's name from a texture it makes lazily; swap in ours once it has made it.</summary>
    void RefreshLabels()
    {
        for (int i = 0; i < labels.Count; i++)
        {
            var (page, label, tex) = labels[i];
            if (page.Texture is null || ReferenceEquals(page.Texture, tex)) continue;
            page.Texture.Dispose();
            tex = new TextTextureUtil(capi!).GenTextTexture(label, CairoFont.WhiteSmallText());
            page.Texture = tex;
            labels[i] = (page, label, tex);
        }
    }

    void ClearLabels()
    {
        // Leave the textures to their pages: a page disposes whatever texture it holds.
        labels.Clear();
    }

    public override void Dispose()
    {
        TidyVariantsModSystem.Resolved -= OnResolved;
        if (capi is not null && tickId != 0) capi.Event.UnregisterGameTickListener(tickId);
        VariantsPageContent.Views = new Dictionary<string, HandbookGroupView>();
        labels.Clear();
    }
}
