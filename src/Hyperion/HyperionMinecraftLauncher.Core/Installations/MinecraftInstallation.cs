namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// Resolved paths inside a <c>.minecraft</c> directory. Lets the rest of the app
/// avoid <see cref="System.IO.Path.Combine(string, string)"/> spelling mistakes and
/// keeps platform path resolution in one place
/// (<see cref="IMinecraftInstallationLocator"/>).
/// </summary>
public sealed record MinecraftInstallation
{
    /// <summary>Absolute path to <c>.minecraft</c> (or the platform equivalent).</summary>
    public required string Root { get; init; }

    /// <summary>Absolute path to <c>versions/</c>.</summary>
    public required string VersionsDirectory { get; init; }

    /// <summary>Absolute path to <c>launcher_profiles.json</c> (may not exist yet).</summary>
    public required string LauncherProfilesPath { get; init; }

    /// <summary>Absolute path to <c>launcher_accounts.json</c> (may not exist yet; opaque to us - read-only display).</summary>
    public required string LauncherAccountsPath { get; init; }

    /// <summary>Absolute path to <c>servers.dat</c> (may not exist yet).</summary>
    public required string ServersDatPath { get; init; }
}
