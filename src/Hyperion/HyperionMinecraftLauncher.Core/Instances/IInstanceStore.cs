using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

/// <summary>Persistent storage for Hyperion launcher instances (one JSON file per instance).</summary>
public interface IInstanceStore
{
    /// <summary>Return every saved instance, sorted by LastPlayedAt desc then CreatedAt desc.</summary>
    Task<IReadOnlyList<Instance>> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>Write the instance (create or update; key is <see cref="Instance.Id"/>).</summary>
    Task SaveAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Delete the instance file for the given id. No-op when missing.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
