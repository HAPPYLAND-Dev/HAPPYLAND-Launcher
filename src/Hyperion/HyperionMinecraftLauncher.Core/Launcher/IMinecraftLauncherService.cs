using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Abstract Minecraft launcher contract. The Core library ships one implementation
/// (<see cref="CmlLibMinecraftLauncherService"/>) wrapping the CmlLib.Core library;
/// tests substitute fakes through this interface.
/// </summary>
public interface IMinecraftLauncherService
{
    /// <summary>
    /// Fetch the list of installable Minecraft versions (release + snapshot, sorted newest-first).
    /// </summary>
    Task<IReadOnlyList<VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enumerate versions already present on disk under the resolved <c>.minecraft</c> directory.
    /// Returns an empty list when the directory is missing - useful so the UI can render a "no installs yet" state.
    /// </summary>
    Task<IReadOnlyList<InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Read <c>launcher_profiles.json</c> from the resolved <c>.minecraft</c> directory.
    /// Returns an empty list when the file is absent. Read-only - we don't write back into Mojang's own file.
    /// </summary>
    Task<IReadOnlyList<LauncherProfile>> ListProfilesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Read the multiplayer server list (<c>servers.dat</c>) from the resolved <c>.minecraft</c> directory.
    /// Returns an empty list when the file is absent or malformed.
    /// </summary>
    Task<IReadOnlyList<ServerListEntry>> ListServersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ping every server in <paramref name="entries"/> in parallel using the Minecraft
    /// Server List Ping protocol. Each ping has its own 3-second timeout; unreachable
    /// servers map to <c>null</c> in the returned dictionary (keyed by <see cref="ServerListEntry.Ip"/>).
    /// </summary>
    Task<IReadOnlyDictionary<string, ServerStatus?>> PingServersAsync(
        IEnumerable<ServerListEntry> entries,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetch Minecraft news from Mojang's official launcher feed.
    /// Returns an empty list on network failure (the launcher must still render).
    /// </summary>
    Task<IReadOnlyList<NewsEntry>> ListNewsAsync(CancellationToken cancellationToken);

    /// <summary>List every saved Hyperion instance (most-recently-played first).</summary>
    Task<IReadOnlyList<Instance>> ListInstancesAsync(CancellationToken cancellationToken);

    /// <summary>Create or update an instance.</summary>
    Task SaveInstanceAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Delete an instance by id.</summary>
    Task DeleteInstanceAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Resolve a session from the supplied <paramref name="request"/>.
    /// Offline / Microsoft / Mojang paths are dispatched on <see cref="AuthRequest.Mode"/>.
    /// </summary>
    Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Install (if missing) and launch the requested Minecraft version.
    /// The returned <see cref="LaunchResult"/> reports the spawned process id and exit semantics.
    /// </summary>
    Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken);
}
