using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;

/// <summary>
/// Persistent ring buffer of the most-recently-uploaded skins. Bounded to a small cap
/// (typically 10) so the user can quickly re-apply a skin they used recently without
/// re-locating the PNG on disk.
/// </summary>
public interface ISkinHistoryStore
{
    /// <summary>
    /// Append a freshly-uploaded skin to the history. If the store is at capacity the
    /// oldest entry (both file and index slot) is removed before the new one is written.
    /// </summary>
    Task AppendAsync(byte[] pngBytes, SkinVariant variant, CancellationToken cancellationToken);

    /// <summary>Most-recent-first list of history entries currently on disk.</summary>
    Task<IReadOnlyList<SkinHistoryEntry>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Drop every entry and delete every PNG on disk.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}
