using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// Enumerates <c>versions/&lt;id&gt;/&lt;id&gt;.json</c> entries inside a <c>.minecraft</c> directory
/// and parses out the metadata needed to render them in the launcher.
/// </summary>
public interface IInstalledVersionScanner
{
    /// <summary>
    /// Scan the given <paramref name="versionsDirectory"/>. Returns an empty list when the directory
    /// is missing or empty. Malformed per-version manifests are skipped silently.
    /// </summary>
    IReadOnlyList<InstalledVersion> Scan(string versionsDirectory);
}
