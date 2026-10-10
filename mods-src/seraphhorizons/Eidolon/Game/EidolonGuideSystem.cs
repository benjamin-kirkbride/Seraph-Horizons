using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's handbook guide (#681; README "Eidolon", the guide): one page,
/// "Building and commanding an eidolon" (<c>config/handbook/eidolon.json</c>, text in
/// <c>lang/en.json</c>), linked from the gantry's and the command tool's own sections. With the
/// <c>Eidolon</c> switch off the client drops the page from the handbook and the server lists it as
/// hidden, so the recipe export leaves it out too, as the crucible furnace's guide is.
/// </summary>
public class EidolonGuideSystem : ModSystem
{
    public const string GuidePageCode = "seraphhorizons-eidolon";
    public const string GuideTitleKey = "seraphhorizons:eidolon-guide-title";

    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _hidePage;

    private static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).Eidolon;

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Applies(api))
            return;
        // The woodworking guide sets the list in Start, before this; the others add to it.
        var hidden = api.ObjectCache.TryGetValue(WoodworkingGuide.HiddenGuidesKey, out var listed)
                     && listed is IEnumerable<(string, string)> pages
            ? pages.ToList()
            : [];
        hidden.Add((GuidePageCode, GuideTitleKey));
        api.ObjectCache[WoodworkingGuide.HiddenGuidesKey] = hidden;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (Applies(api) || api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is not { } handbook)
            return;
        _handbook = handbook;
        _hidePage = pages => pages.RemoveAll(p => p.PageCode == GuidePageCode);
        handbook.OnInitCustomPages += _hidePage;
    }

    public override void Dispose()
    {
        if (_handbook != null && _hidePage != null)
            _handbook.OnInitCustomPages -= _hidePage;
        _handbook = null;
        _hidePage = null;
    }
}
