using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.News;

/// <summary>Fetches Minecraft news. Production uses Mojang's launcher feed; tests stub.</summary>
public interface INewsClient
{
    /// <summary>Returns the latest news entries (newest-first). Empty on network failure (the launcher must still render).</summary>
    Task<IReadOnlyList<NewsEntry>> FetchAsync(CancellationToken cancellationToken);
}
