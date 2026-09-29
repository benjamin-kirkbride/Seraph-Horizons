using System.Collections;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// Which collectibles have a GUI renderer registered with RegisterItemstackRenderer. The engine
/// keeps them in an internal field (ClientEventAPI.itemStackRenderersByTarget[class][target]),
/// so this is reflection and may be unavailable; the export then records nothing about them.
/// </summary>
internal sealed class CustomRenderers
{
    private readonly IDictionary?[] _guiByClass = new IDictionary?[2];

    public CustomRenderers(ICoreClientAPI api)
    {
        try
        {
            object events = api.Event;
            if (events.GetType().GetField("itemStackRenderersByTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(events) is not Array byClass)
            {
                Problem = "itemStackRenderersByTarget not found";
                return;
            }
            for (int cls = 0; cls < Math.Min(2, byClass.Length); cls++)
            {
                if (byClass.GetValue(cls) is Array byTarget && byTarget.Length > (int)EnumItemRenderTarget.Gui)
                {
                    _guiByClass[cls] = byTarget.GetValue((int)EnumItemRenderTarget.Gui) as IDictionary;
                }
            }
            Available = true;
        }
        catch (Exception e)
        {
            Problem = e.GetType().Name + ": " + e.Message;
        }
    }

    public bool Available { get; }
    public string? Problem { get; }

    public int Count => _guiByClass.Sum(d => d?.Count ?? 0);

    public bool Has(CollectibleObject c)
    {
        IDictionary? d = _guiByClass[(int)c.ItemClass];
        return d != null && d.Contains(c.Id);
    }
}
