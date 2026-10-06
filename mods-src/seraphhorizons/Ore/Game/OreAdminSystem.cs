using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Registers the ore admin tools (#458, <see cref="OreAdminCommands"/>) after <see cref="OreSystem"/>
/// has bound the cell rule and the deposit service; switch <see cref="SeraphHorizonsConfig.AdminTools"/>.
/// Server only.
/// </summary>
public class OreAdminSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override double ExecuteOrder() => 0.7;

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).AdminTools) return;
        var ore = api.ModLoader.GetModSystem<OreSystem>();
        if (ore == null) return;
        new OreAdminCommands(api, ore).Register();
    }
}
