using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

/// <summary>Read the multiplayer server list (<c>servers.dat</c>).</summary>
public interface IServersStore
{
    /// <summary>Returns the server list at <paramref name="path"/>, or an empty list when the file is missing / malformed.</summary>
    Task<IReadOnlyList<ServerListEntry>> LoadAsync(string path, CancellationToken cancellationToken);
}
