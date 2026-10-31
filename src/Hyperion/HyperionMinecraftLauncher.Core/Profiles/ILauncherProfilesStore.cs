using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;

/// <summary>
/// Read <c>launcher_profiles.json</c> from a given path. Writing is intentionally
/// out of scope for v0.6: when our launcher and Mojang's own launcher round-trip
/// the same file they can corrupt each other's auth bookkeeping. Treat Mojang's file
/// as read-only and keep any future Hyperion-owned profiles in a separate store.
/// </summary>
public interface ILauncherProfilesStore
{
    /// <summary>Parse <c>launcher_profiles.json</c> from <paramref name="path"/>. Returns an empty file when missing.</summary>
    Task<LauncherProfilesFile> LoadAsync(string path, CancellationToken cancellationToken);
}
