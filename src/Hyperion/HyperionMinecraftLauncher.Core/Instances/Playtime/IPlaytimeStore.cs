using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

/// <summary>
/// Per-instance play time, kept outside <see cref="Instance"/> so auto-imported instances
/// (never persisted as instance records) accumulate time too.
/// </summary>
public interface IPlaytimeStore
{
    /// <summary>Total credited play time for the instance; zero when it was never played.</summary>
    Task<TimeSpan> GetTotalAsync(string instanceId, CancellationToken cancellationToken);

    /// <summary>Journal a freshly started game process.</summary>
    Task BeginSessionAsync(PlaySession session, CancellationToken cancellationToken);

    /// <summary>
    /// Close the journaled session of <paramref name="processId"/> at <paramref name="endedAt"/>
    /// and credit its length to the instance. Returns the credited time (zero for an unknown pid).
    /// </summary>
    Task<TimeSpan> EndSessionAsync(int processId, DateTimeOffset endedAt, CancellationToken cancellationToken);

    /// <summary>Sessions begun but not ended yet (running games, or orphans from a closed launcher).</summary>
    Task<IReadOnlyList<PlaySession>> ListActiveAsync(CancellationToken cancellationToken);
}
