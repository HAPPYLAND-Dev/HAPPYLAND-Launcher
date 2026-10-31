using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// CRUD-only contract for headless server registrations. No process spawn / Java side here -
/// when the v0.29 server-jar download lands, a separate service will run the actual server.
/// </summary>
public interface IHeadlessServerStore
{
    /// <summary>Create a new headless server entry on disk and return the persisted record.</summary>
    Task<HeadlessServer> CreateAsync(HeadlessServerCreateRequest request, CancellationToken cancellationToken);

    /// <summary>List every saved headless server (newest CreatedAt first).</summary>
    Task<IReadOnlyList<HeadlessServer>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Read one server by id, or <c>null</c> when no such id exists.</summary>
    Task<HeadlessServer?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Delete the server folder. No-op when the id is unknown.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
