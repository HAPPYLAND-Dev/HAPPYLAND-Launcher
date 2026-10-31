using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// Lists the per-instance content people want to glance at without launching the game:
/// screenshots, world saves, and the multiplayer server list, each scoped to that
/// instance's <c>gameDir</c>. Implementations resolve the root path from
/// <see cref="Instance.GameDirectory"/> with a sensible default fallback.
/// </summary>
public interface IInstanceBrowser
{
    /// <summary>Enumerate <c>&lt;gameDir&gt;/screenshots/*.png</c>, newest first.</summary>
    Task<IReadOnlyList<ScreenshotEntry>> ListScreenshotsAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Enumerate direct child dirs of <c>&lt;gameDir&gt;/saves/</c> that contain a <c>level.dat</c>.</summary>
    Task<IReadOnlyList<WorldEntry>> ListWorldsAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Parse <c>&lt;gameDir&gt;/servers.dat</c>. Empty list when the file is missing or malformed.</summary>
    Task<IReadOnlyList<ServerListEntry>> ListServersAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Enumerate <c>&lt;gameDir&gt;/resourcepacks/*.zip</c> and <c>*.zip.disabled</c>.</summary>
    Task<IReadOnlyList<ResourcePackEntry>> ListResourcePacksAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Enumerate <c>&lt;gameDir&gt;/shaderpacks/*.zip</c> and <c>*.zip.disabled</c>.</summary>
    Task<IReadOnlyList<ShaderPackEntry>> ListShaderPacksAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Enumerate <c>&lt;gameDir&gt;/saves/&lt;world&gt;/datapacks/*.zip</c> across every world, grouped by world.</summary>
    Task<IReadOnlyList<DataPackEntry>> ListDataPacksAsync(Instance instance, CancellationToken cancellationToken);
}
