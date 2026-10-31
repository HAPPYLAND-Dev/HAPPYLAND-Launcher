namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One save folder under <c>&lt;gameDir&gt;/saves/</c>. Fields are read by parsing the world's
/// <c>level.dat</c> NBT (gzip-compressed compound at <c>Data.*</c>).
/// </summary>
public sealed record WorldEntry
{
    /// <summary>Absolute path to the world folder.</summary>
    public required string FullPath { get; init; }

    /// <summary>Folder name on disk (e.g. <c>"New World"</c>). Drives "open folder" actions.</summary>
    public required string FolderName { get; init; }

    /// <summary>Player-set world name (NBT <c>Data.LevelName</c>). Falls back to <see cref="FolderName"/> when absent.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// ISO 8601 (round-trip) representation of NBT <c>Data.LastPlayed</c> (ms since epoch UTC).
    /// <c>null</c> when the field is missing - keeps "Never" rendering trivial for the View.
    /// </summary>
    public string? LastPlayedIso { get; init; }

    /// <summary>Sum of every file under <see cref="FullPath"/>. Best-effort: a Directory.EnumerateFiles + Length.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>NBT <c>Data.GameType</c> (0 = Survival, 1 = Creative, 2 = Adventure, 3 = Spectator). Null when absent.</summary>
    public int? GameMode { get; init; }

    /// <summary>NBT <c>Data.Version.Name</c> (e.g. <c>"1.21.5"</c>). Null on pre-1.9 worlds without the field.</summary>
    public string? Version { get; init; }
}
