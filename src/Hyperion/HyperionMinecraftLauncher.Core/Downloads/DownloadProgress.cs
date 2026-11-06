namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

public sealed record DownloadProgress(long DownloadedBytes, long? TotalBytes, long TransferredBytes);
