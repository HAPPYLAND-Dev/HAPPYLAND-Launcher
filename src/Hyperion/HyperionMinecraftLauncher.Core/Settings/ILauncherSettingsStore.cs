using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

/// <summary>Persist and reload <see cref="LauncherSettings"/>. JSON on disk; in-memory in tests.</summary>
public interface ILauncherSettingsStore
{
    /// <summary>Read the saved settings, or fresh defaults when the file is missing or malformed.</summary>
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Persist the given settings atomically (write-temp + rename).</summary>
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken);
}
