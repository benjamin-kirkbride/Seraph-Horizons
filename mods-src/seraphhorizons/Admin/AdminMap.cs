using System.Collections.Concurrent;
using System.Text;
using ProtoBuf;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Admin;

/// <summary>Builds an overlay around an admin, from the argument it was switched on with.</summary>
public delegate MapOverlay MapOverlayProvider(IServerPlayer player, string argument);

/// <summary>One overlay for the admin map layer, server to client; empty <see cref="Json"/> removes it.</summary>
[ProtoContract]
public class AdminMapPacket
{
    [ProtoMember(1)] public string Key { get; set; } = "";

    [ProtoMember(2)] public string Json { get; set; } = "";
}

/// <summary>
/// The admin map layer's server half: which admins have which overlay on (<c>/sh ore map on</c>,
/// <c>/sh trade map on [item]</c>), building it around them (<see cref="Providers"/>) and sending it,
/// again every <see cref="RefreshSeconds"/> while they move. Only players with
/// <c>controlserver</c> are ever sent one: what deposits lie where is the game's secret.
/// </summary>
public sealed class AdminMapServer
{
    public const int RefreshSeconds = 30;

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _channel;
    // (player uid, overlay key) -> the argument it was switched on with
    private readonly ConcurrentDictionary<(string Uid, string Key), string> _on = new();
    private readonly long _listener;

    public AdminMapServer(ICoreServerAPI api, IServerNetworkChannel channel, Dictionary<string, MapOverlayProvider> providers)
    {
        _api = api;
        _channel = channel;
        Providers = providers;
        _listener = api.Event.RegisterGameTickListener(_ => Refresh(), RefreshSeconds * 1000);
    }

    /// <summary>Overlay builders by key: (admin, argument) → overlay around them.</summary>
    public Dictionary<string, MapOverlayProvider> Providers { get; }

    public bool IsOn(IServerPlayer player, string key) => _on.ContainsKey((player.PlayerUID, key));

    /// <summary>Switches an overlay for an admin; on sends it at once. The count of things drawn, or
    /// -1 when the player may not have it.</summary>
    public int Set(IServerPlayer player, string key, bool on, string argument = "")
    {
        if (!on)
        {
            _on.TryRemove((player.PlayerUID, key), out _);
            _channel.SendPacket(new AdminMapPacket { Key = key }, player);
            return 0;
        }
        if (!player.HasPrivilege(Privilege.controlserver)) return -1;
        _on[(player.PlayerUID, key)] = argument;
        return Send(player, key, argument);
    }

    private int Send(IServerPlayer player, string key, string argument)
    {
        if (!Providers.TryGetValue(key, out var build)) return 0;
        var overlay = build(player, argument);
        overlay.Key = key;
        _channel.SendPacket(new AdminMapPacket { Key = key, Json = overlay.ToJson() }, player);
        return overlay.Count;
    }

    private void Refresh()
    {
        foreach (var ((uid, key), argument) in _on.ToArray())
        {
            if (_api.World.PlayerByUid(uid) is not IServerPlayer { ConnectionState: EnumClientState.Playing } player) continue;
            if (!player.HasPrivilege(Privilege.controlserver))
            {
                Set(player, key, false);
                continue;
            }
            try
            {
                Send(player, key, argument);
            }
            catch (Exception e)
            {
                _api.Logger.Warning("[seraphhorizons] Admin map: building the {0} overlay failed: {1}", key, e.Message);
            }
        }
    }

    public void Dispose() => _api.Event.UnregisterGameTickListener(_listener);
}

/// <summary>
/// The admin map layer (client): draws the overlays the server sends (<see cref="AdminMapServer"/>)
/// on the world map, as outlines, rings, lines and small filled squares, with a label under the
/// mouse. A minimal layer: no textures, only the GUI shader's rectangle outline
/// (<c>RenderRectangle</c>), stacked to fill the markers. Its tab shows for every player, but only
/// admins are ever sent anything to draw.
/// </summary>
public class AdminMapLayer : MapLayer
{
    public const string Code = "seraphhorizons-admin";

    /// <summary>The overlays this client holds, by key; the network handler fills it.</summary>
    public static readonly ConcurrentDictionary<string, MapOverlay> Overlays = new(StringComparer.Ordinal);

    private Vec2f _view = new();

    public AdminMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
    {
    }

    public override string Title => "Admin overlays";

    public override string LayerGroupCode => Code;

    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

    public override bool RequireChunkLoaded => false;

    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!Active || Overlays.IsEmpty) return;
        var render = mapElem.Api.Render;
        float bx = (float)mapElem.Bounds.renderX, by = (float)mapElem.Bounds.renderY;
        const float z = 60;
        foreach (var overlay in Overlays.Values)
        {
            foreach (var r in overlay.Rects)
            {
                var (x1, y1) = View(mapElem, r.X1, r.Z1);
                var (x2, y2) = View(mapElem, r.X2, r.Z2);
                render.RenderRectangle(bx + x1, by + y1, z, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1), r.Color);
            }
            foreach (var ring in overlay.Rings)
            {
                const int steps = 48;
                for (int i = 0; i < steps; i++)
                {
                    double a = i * Math.PI * 2 / steps;
                    var (x, y) = View(mapElem, ring.X + ring.Radius * Math.Cos(a), ring.Z + ring.Radius * Math.Sin(a));
                    render.RenderRectangle(bx + x - 1, by + y - 1, z, 2, 2, ring.Color);
                }
            }
            foreach (var line in overlay.Lines)
            {
                var (x1, y1) = View(mapElem, line.X1, line.Z1);
                var (x2, y2) = View(mapElem, line.X2, line.Z2);
                int steps = Math.Clamp((int)(Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)) / 4), 1, 400);
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    render.RenderRectangle(bx + x1 + (x2 - x1) * t - 1, by + y1 + (y2 - y1) * t - 1, z, 2, 2, line.Color);
                }
            }
            foreach (var m in overlay.Marks)
            {
                var (x, y) = View(mapElem, m.X, m.Z);
                for (int s = m.Size; s > 0; s -= 2)
                    render.RenderRectangle(bx + x - s / 2f, by + y - s / 2f, z, s, s, m.Color);
            }
        }
    }

    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (!Active || Overlays.IsEmpty) return;
        float mx = (float)(args.X - mapElem.Bounds.renderX), my = (float)(args.Y - mapElem.Bounds.renderY);
        foreach (var overlay in Overlays.Values)
            foreach (var m in overlay.Marks)
            {
                if (m.Label == null) continue;
                var (x, y) = View(mapElem, m.X, m.Z);
                if (Math.Abs(x - mx) <= m.Size && Math.Abs(y - my) <= m.Size) hoverText.AppendLine(m.Label);
            }
    }

    private (float X, float Y) View(GuiElementMap map, double x, double z)
    {
        map.TranslateWorldPosToViewPos(new Vec3d(x, 0, z), ref _view);
        return (_view.X, _view.Y);
    }
}
