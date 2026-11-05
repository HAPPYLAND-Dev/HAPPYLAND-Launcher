using System.Security.Cryptography;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

public sealed record DownloadFile(
    string Url,
    string Path,
    long? Size = null,
    string? Hash = null,
    HashAlgorithmName? HashAlgorithm = null,
    IReadOnlyList<string>? Mirrors = null);
