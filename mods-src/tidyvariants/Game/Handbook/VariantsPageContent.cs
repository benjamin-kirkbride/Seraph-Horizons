using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.TidyVariants;

/// <summary>One collapsed group as its pages show it.</summary>
/// <param name="Title">Display title (the group's lang title, else the representative's name).</param>
/// <param name="Members">The group's member pages: the stack to draw and the page code to open.</param>
internal sealed record HandbookGroupView(string Title, IReadOnlyList<(ItemStack Stack, string PageCode)> Members);

/// <summary>
/// Appends a "variants" section (one clickable icon per member page) to the handbook page of every member of a
/// collapsed group, the representative's included: that page is the group's only list entry, and the section is
/// how the other members stay reachable. Added to member collectibles on the client only; vanilla's
/// <see cref="CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo"/> calls it last through
/// <see cref="ICustomHandbookPageContent"/>. Pages are matched by page code, so one collectible with stacks in
/// several groups (clutter) shows the right group on each page.
/// </summary>
internal sealed class VariantsPageContent(CollectibleObject collObj) : CollectibleBehavior(collObj), ICustomHandbookPageContent
{
    /// <summary>Page code to group, replaced as a whole whenever the handbook reloads its pages (main thread only).</summary>
    internal static IReadOnlyDictionary<string, HandbookGroupView> Views = new Dictionary<string, HandbookGroupView>();

    public void OnHandbookPageComposed(List<RichTextComponentBase> components, ItemSlot inSlot, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
    {
        try
        {
            var stack = inSlot?.Itemstack;
            if (stack is null || !Views.TryGetValue(GuiHandbookItemStackPage.PageCodeForStack(stack), out var view)) return;

            components.Add(new ClearFloatTextComponent(capi, 14f));
            string heading = Lang.Get("tidyvariants:handbook-variants-heading", view.Title, view.Members.Count);
            components.AddRange(VtmlUtil.Richtextify(capi, "<strong>" + Escape(heading) + "</strong>\n", CairoFont.WhiteSmallText()));
            components.Add(new ClearFloatTextComponent(capi, 2f));
            foreach (var (memberStack, pageCode) in view.Members)
            {
                string code = pageCode;
                components.Add(new ItemstackTextComponent(capi, memberStack, 40.0, 4.0, EnumFloat.Inline, _ => openDetailPageFor(code)));
            }
            components.Add(new ClearFloatTextComponent(capi, 0f));
        }
        catch (Exception ex)
        {
            capi?.Logger.Error("[tidyvariants] handbook: could not add the variants section: {0}", ex);
        }
    }

    static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
