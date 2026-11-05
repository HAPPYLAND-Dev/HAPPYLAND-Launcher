using CmlLib.Core.Files;
using CmlLib.Core.Installers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

public sealed class AdaptiveGameInstaller : GameInstallerBase
{
    private readonly AdaptiveDownloader downloader;

    public AdaptiveGameInstaller(HttpClient http) : base(http) => downloader = new AdaptiveDownloader(http);

    protected override async ValueTask Install(IEnumerable<GameFile> gameFiles, CancellationToken cancellationToken)
    {
        var files = gameFiles.Where(file => !string.IsNullOrEmpty(file.Url) && !string.IsNullOrEmpty(file.Path))
            .Distinct(GameFilePathComparer.Default).Where(file => !IsExcludedPath(file.Path!)).ToArray();
        var pending = new Dictionary<DownloadFile, GameFile>();
        var completed = 0;
        await Task.Run(async () =>
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FireFileProgress(files.Length, completed, file.Name, InstallerEventType.Queued);
                if (NeedUpdate(file))
                    pending.Add(new DownloadFile(file.Url!, file.Path!, file.Size > 0 ? file.Size : null,
                        string.IsNullOrEmpty(file.Hash) ? null : file.Hash), file);
                else
                {
                    await file.ExecuteUpdateTask(cancellationToken).ConfigureAwait(false);
                    FireFileProgress(files.Length, ++completed, file.Name, InstallerEventType.Done);
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        await downloader.DownloadAsync(pending.Keys.ToArray(),
            new SyncProgress<CmlLib.Core.ByteProgress>(FireByteProgress), async (request, ct) =>
            {
                var file = pending[request];
                await file.ExecuteUpdateTask(ct).ConfigureAwait(false);
                FireFileProgress(files.Length, Interlocked.Increment(ref completed), file.Name, InstallerEventType.Done);
            }, cancellationToken).ConfigureAwait(false);
    }
}
