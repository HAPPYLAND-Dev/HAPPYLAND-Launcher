using System.Security.Cryptography;
using System.Text;
using CmlLib.Core.Installers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

public sealed class DownloadCache(string directory)
{
    public async Task DownloadAsync(AdaptiveDownloader downloader, IReadOnlyList<DownloadFile> files,
        IProgress<DownloadProgress>? progress, Func<DownloadFile, CancellationToken, Task>? fileCompleted,
        CancellationToken cancellationToken)
    {
        var missing = new List<DownloadFile>();
        long restoredBytes = 0;
        var knownTotal = files.All(file => file.Size.HasValue) ? files.Sum(file => file.Size!.Value) : (long?)null;
        progress?.Report(new DownloadProgress(0, knownTotal, 0));
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var restored = await RestoreAsync(file, cancellationToken).ConfigureAwait(false);
            if (restored is null)
            {
                missing.Add(file);
                continue;
            }
            restoredBytes += restored.Value;
            progress?.Report(new DownloadProgress(restoredBytes, knownTotal, 0));
            if (fileCompleted is not null) await fileCompleted(file, cancellationToken).ConfigureAwait(false);
        }

        await downloader.DownloadAsync(missing, new SyncProgress<DownloadProgress>(value =>
            progress?.Report(value with
            {
                DownloadedBytes = value.DownloadedBytes + restoredBytes,
                TotalBytes = value.TotalBytes + restoredBytes
            })), async (file, ct) =>
            {
                await StoreAsync(file, ct).ConfigureAwait(false);
                if (fileCompleted is not null) await fileCompleted(file, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
    }

    public void Clear()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private string? CachePath(DownloadFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Hash)) return null;
        var identity = (file.HashAlgorithm ?? HashAlgorithmName.SHA1).Name + ":" + file.Hash.ToUpperInvariant();
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".blob");
    }

    private async Task<long?> RestoreAsync(DownloadFile file, CancellationToken ct)
    {
        var path = CachePath(file);
        if (path is null || !File.Exists(path)) return null;

        await using (var input = File.OpenRead(path))
        {
            var size = input.Length;
            var digest = await CryptographicOperations.HashDataAsync(file.HashAlgorithm ?? HashAlgorithmName.SHA1, input, ct)
                .ConfigureAwait(false);
            if ((file.Size is null || file.Size == size) &&
                string.Equals(Convert.ToHexString(digest), file.Hash, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file.Path))!);
                var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".restore";
                try
                {
                    input.Position = 0;
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await input.CopyToAsync(output, ct).ConfigureAwait(false);
                    File.Move(temporary, file.Path, overwrite: true);
                }
                finally
                {
                    File.Delete(temporary);
                }
                return size;
            }
        }
        File.Delete(path);
        return null;
    }

    private async Task StoreAsync(DownloadFile file, CancellationToken ct)
    {
        var path = CachePath(file);
        if (path is null) return;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var input = File.OpenRead(file.Path))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
