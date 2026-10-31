using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.News;

/// <summary>
/// Asks <c>primary</c> first and falls back to <c>secondary</c> when it has nothing (or fails).
/// The minecraft.net article API is undocumented, so the launcher's own feed backs it up.
/// </summary>
public sealed class FallbackNewsClient : INewsClient
{
    private readonly INewsClient _primary;
    private readonly INewsClient _secondary;

    /// <summary>Wrap two sources; both are required.</summary>
    public FallbackNewsClient(INewsClient primary, INewsClient secondary)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _secondary = secondary ?? throw new ArgumentNullException(nameof(secondary));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _primary.FetchAsync(cancellationToken).ConfigureAwait(false);
            if (entries.Count > 0) return entries;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Schema drift or an unexpected failure in the primary source - use the backup.
        }
        return await _secondary.FetchAsync(cancellationToken).ConfigureAwait(false);
    }
}
