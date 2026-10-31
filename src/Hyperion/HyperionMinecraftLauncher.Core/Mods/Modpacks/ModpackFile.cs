namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// One downloadable artifact referenced by a modpack manifest. The path is relative
/// to the target instance directory - e.g. <c>"mods/fabric-api-0.92.2.jar"</c> or
/// <c>"resourcepacks/themed.zip"</c>. <see cref="DownloadUrl"/> is the absolute URL
/// the importer fetches from; <see cref="Sha1"/> + <see cref="FileSizeBytes"/> are
/// optional verification metadata. <see cref="Required"/> follows Modrinth's
/// <c>env.client = "required"</c> semantics: optional files can be skipped on import.
/// </summary>
public sealed record ModpackFile
{
    /// <summary>Path relative to the instance directory (e.g. <c>mods/foo.jar</c>).</summary>
    public required string TargetPath { get; init; }

    /// <summary>Absolute URL to fetch this file from.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Further mirrors from the manifest, tried in order when <see cref="DownloadUrl"/> fails.</summary>
    public IReadOnlyList<string> MirrorUrls { get; init; } = Array.Empty<string>();

    /// <summary>Sha-1 hex digest for verifying the download. Null when the manifest omits it.</summary>
    public string? Sha1 { get; init; }

    /// <summary>Declared file size in bytes. Null when the manifest omits it.</summary>
    public long? FileSizeBytes { get; init; }

    /// <summary>True when this file is required for the pack to function (default).</summary>
    public bool Required { get; init; } = true;
}
