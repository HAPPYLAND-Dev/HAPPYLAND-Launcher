namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// Finds the on-disk <c>.minecraft</c> directory. Production code uses
/// <see cref="DefaultMinecraftInstallationLocator"/>; tests inject a stub pointing at
/// a temp directory.
/// </summary>
public interface IMinecraftInstallationLocator
{
    /// <summary>Compute the platform-default Minecraft installation paths.</summary>
    MinecraftInstallation Locate();
}
