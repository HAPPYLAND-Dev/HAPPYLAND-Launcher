using CmlLib.Core.Files;
using CmlLib.Core.Installers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

public sealed class AdaptiveGameInstaller : GameInstallerBase
{
    private readonly AdaptiveDownloader downloader;
    private readonly Func<string, string?>? rangeFallback;
    public IProgress<DownloadProgress>? DetailedProgress { get; set; }

    public AdaptiveGameInstaller(HttpClient http, Func<string, string?>? rangeFallback = null) : base(http)
    {
        downloader = new AdaptiveDownloader(http);
        this.rangeFallback = rangeFallback;
    }

    protected override async ValueTask Install(IEnumerable<GameFile> gameFiles, CancellationToken cancellationToken)
    {
        var files = gameFiles.Where(file => !string.IsNullOrEmpty(file.Url) && !string.IsNullOrEmpty(file.Path))
            .Distinct(GameFilePathComparer.Default).Where(file => !IsExcludedPath(file.Path!)).ToArray();
        var pending = new Dictionary<DownloadFile, GameFile>();
        var completed = 0;
        long reusedBytes = 0;
        await Task.Run(async () =>
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FireFileProgress(files.Length, completed, file.Name, InstallerEventType.Queued);
                if (NeedUpdate(file))
                    pending.Add(new DownloadFile(file.Url!, file.Path!, file.Size > 0 ? file.Size : null,
                        string.IsNullOrEmpty(file.Hash) ? null : file.Hash,
                        RangeFallbackUrl: rangeFallback?.Invoke(file.Url!)), file);
                else
                {
                    await file.ExecuteUpdateTask(cancellationToken).ConfigureAwait(false);
                    reusedBytes += new FileInfo(file.Path!).Length;
                    FireFileProgress(files.Length, ++completed, file.Name, InstallerEventType.Done);
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        await downloader.DownloadAsync(pending.Keys.ToArray(),
            new SyncProgress<DownloadProgress>(value =>
            {
                var total = value.TotalBytes + reusedBytes;
                var overall = value with { DownloadedBytes = value.DownloadedBytes + reusedBytes, TotalBytes = total };
                DetailedProgress?.Report(overall);
                FireByteProgress(new CmlLib.Core.ByteProgress(total ?? 0, overall.DownloadedBytes));
            }), async (request, ct) =>
            {
                var file = pending[request];
                await file.ExecuteUpdateTask(ct).ConfigureAwait(false);
                FireFileProgress(files.Length, Interlocked.Increment(ref completed), file.Name, InstallerEventType.Done);
            }, cancellationToken).ConfigureAwait(false);
    }
}
