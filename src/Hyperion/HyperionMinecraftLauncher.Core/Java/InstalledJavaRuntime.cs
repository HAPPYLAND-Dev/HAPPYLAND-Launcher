using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// A Java runtime present on disk under the launcher's managed runtimes directory.
/// </summary>
/// <param name="Requirement">Which Minecraft Java family this runtime satisfies.</param>
/// <param name="JavaExecutablePath">Absolute path to <c>java</c> / <c>java.exe</c> under <c>bin/</c>.</param>
/// <param name="JavaHome">Absolute path to the JRE root (parent of <c>bin/</c>).</param>
/// <param name="Version">Parsed runtime version (best-effort from the archive layout).</param>
/// <param name="SizeBytes">Total on-disk size of the extracted runtime in bytes.</param>
public sealed record InstalledJavaRuntime(
    JavaRequirement Requirement,
    string JavaExecutablePath,
    string JavaHome,
    Version Version,
    long SizeBytes);
