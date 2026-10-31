using CmlLib.Core;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installers;
using CmlLib.Core.Version;
using CmlLib.Core.VersionLoader;
using CmlLib.Core.VersionMetadata;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace Happyland.Launcher.Core;

public sealed class GameManager
{
    private readonly CmlLibUnderlyingLauncher underlying;
    private readonly MinecraftLauncher launcher;
    private readonly CmlLibModLoaderInstaller loader;
    private readonly CmlLibForgeUnderlying forge;
    private readonly CmlLibNeoForgeUnderlying neoForge;
    private readonly AdoptiumJavaRuntimeManager java;
    private readonly HttpClient http;
    private readonly LaunchVersionLoader versionLoader;
    private readonly string gameDirectory;
    private readonly string javaDirectory;
    private readonly string markerPath;
    private readonly string uninstalledPath;

    public GameManager(string root)
    {
        gameDirectory = Path.Combine(root, "game");
        javaDirectory = Path.Combine(root, "java");
        http = new HttpClient(new MinecraftMirrorHandler()) { Timeout = TimeSpan.FromMinutes(3) };
        var parameters = MinecraftLauncherParameters.CreateDefault(new MinecraftPath(gameDirectory), http);
        versionLoader = new LaunchVersionLoader(parameters.VersionLoader!, new LocalJsonVersionLoader(parameters.MinecraftPath!));
        parameters.VersionLoader = versionLoader;
        var extractors = parameters.FileExtractors!;
        extractors.Remove(extractors.OfType<JavaFileExtractor>().Single());
        launcher = new MinecraftLauncher(parameters);
        underlying = new CmlLibUnderlyingLauncher(launcher);
        forge = new CmlLibForgeUnderlying(launcher);
        neoForge = new CmlLibNeoForgeUnderlying(launcher);
        loader = new CmlLibModLoaderInstaller(forge, neoForge, new CmlLibFabricUnderlying(http, launcher), new CmlLibQuiltUnderlying(http, launcher));
        java = new AdoptiumJavaRuntimeManager(http, javaDirectory, AdoptiumJavaRuntimeManager.DetectOs(), AdoptiumJavaRuntimeManager.DetectArch());
        markerPath = Path.Combine(root, "prepared.json");
        uninstalledPath = Path.Combine(root, "uninstalled.flag");
    }

    public bool HasGameFiles => !File.Exists(uninstalledPath) && Directory.Exists(launcher.MinecraftPath.Versions) &&
        Directory.EnumerateFiles(launcher.MinecraftPath.Versions, "*.jar", SearchOption.AllDirectories).Any();

    private async Task<JavaRequirement> JavaRequirementAsync(string minecraftVersion, CancellationToken ct)
    {
        var version = await launcher.GetVersionAsync(minecraftVersion, ct);
        return ResolveJavaRequirement(version, minecraftVersion);
    }

    private static JavaRequirement ResolveJavaRequirement(IVersion version, string minecraftVersion)
    {
        if (!int.TryParse(version.JavaVersion?.MajorVersion, out var major)) return JavaRequirementResolver.For(minecraftVersion);
        return major switch
        {
            <= 8 => JavaRequirement.Java8,
            <= 17 => JavaRequirement.Java17,
            <= 21 => JavaRequirement.Java21,
            <= 25 => JavaRequirement.Java25,
            _ => throw new NotSupportedException($"当前游戏需要暂未支持的 Java {major}。")
        };
    }

    public async Task<bool> IsPreparedAsync(InstalledPack? pack)
    {
        if (pack is null || File.Exists(uninstalledPath) || !File.Exists(markerPath)) return false;
        var text = await File.ReadAllTextAsync(markerPath);
        return text == pack.Sha256;
    }

    public async Task PrepareAsync(InstalledPack pack, string? javaPath, IProgress<LaunchProgress>? progress, CancellationToken ct = default)
    {
        if (File.Exists(markerPath)) File.Delete(markerPath);
        versionLoader.LocalOnly = false;
        await launcher.GetAllVersionsAsync(ct);
        var instance = pack.Instance;
        if (string.IsNullOrWhiteSpace(javaPath))
            javaPath = await EnsureManagedJavaAsync(await JavaRequirementAsync(instance.VersionId, ct), null, ct);
        forge.JavaPath = javaPath;
        neoForge.JavaPath = javaPath;
        progress?.Report(new LaunchProgress { Stage = "下载 Minecraft" });
        await underlying.InstallAsync(instance.VersionId, progress, ct);
        if (instance.Loader != TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.None)
        {
            progress?.Report(new LaunchProgress { Stage = "安装模组加载器" });
            var version = await loader.InstallAsync(instance.Loader, instance.VersionId, instance.LoaderVersion, null, ct);
            await underlying.InstallAsync(version, progress, ct);
        }
        File.Delete(uninstalledPath);
        await File.WriteAllTextAsync(markerPath, pack.Sha256, ct);
    }

    public async Task UninstallAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.Delete(markerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(uninstalledPath)!);
        await File.WriteAllTextAsync(uninstalledPath, "", ct);
        await Task.Run(() =>
        {
            foreach (var directory in new[]
            {
                Path.Combine(gameDirectory, "versions"),
                Path.Combine(gameDirectory, "assets"),
                Path.Combine(gameDirectory, "libraries"),
                Path.Combine(gameDirectory, "runtime"),
                javaDirectory
            })
            {
                RemoveManagedDirectory(directory, ct);
            }
        }, ct);
    }

    public async Task ReinstallJavaAsync(InstalledPack pack, IProgress<LaunchProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var wasPrepared = await IsPreparedAsync(pack);
        File.Delete(markerPath);
        var localVersions = await new LocalJsonVersionLoader(launcher.MinecraftPath).GetVersionMetadatasAsync(ct);
        var version = await localVersions.GetVersionAsync(pack.Instance.VersionId, ct);
        var requirement = ResolveJavaRequirement(version, pack.Instance.VersionId);
        progress?.Report(new LaunchProgress { Stage = "Preparing Java" });
        await Task.Run(() => RemoveManagedDirectory(Path.Combine(javaDirectory, requirement.ToString().ToLowerInvariant()), ct), ct);
        var javaProgress = progress is null ? null : new SyncProgress<double>(fraction =>
            progress.Report(new LaunchProgress { Stage = "Downloading Java", Fraction = fraction }));
        await EnsureManagedJavaAsync(requirement, javaProgress, ct);
        if (wasPrepared) await File.WriteAllTextAsync(markerPath, pack.Sha256, ct);
    }

    private async Task<string> EnsureManagedJavaAsync(JavaRequirement requirement, IProgress<double>? progress, CancellationToken ct)
    {
        var installed = await Task.Run(() => java.ListInstalledAsync(ct), ct);
        var existing = installed.FirstOrDefault(runtime => runtime.Requirement == requirement);
        if (existing is not null) return existing.JavaExecutablePath;

        var runtimeName = requirement.ToString().ToLowerInvariant();
        var target = Path.Combine(javaDirectory, runtimeName);
        await Task.Run(() => RemoveManagedDirectory(target, ct), ct);
        var stagingRoot = Path.Combine(javaDirectory, ".install-" + Guid.NewGuid().ToString("N"));
        var staging = new AdoptiumJavaRuntimeManager(http, stagingRoot, AdoptiumJavaRuntimeManager.DetectOs(), AdoptiumJavaRuntimeManager.DetectArch());
        try
        {
            var executable = await staging.EnsureRuntimeAsync(requirement, progress, ct);
            ct.ThrowIfCancellationRequested();
            var stagedRuntime = Path.Combine(stagingRoot, runtimeName);
            var relativeExecutable = Path.GetRelativePath(stagedRuntime, executable);
            await Task.Run(() => Directory.Move(stagedRuntime, target), ct);
            return Path.Combine(target, relativeExecutable);
        }
        finally
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
            });
        }
    }

    private static void RemoveManagedDirectory(string directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory)) return;
        var isolated = Path.Combine(Path.GetDirectoryName(directory)!, "." + Path.GetFileName(directory) + ".remove-" + Guid.NewGuid().ToString("N"));
        Directory.Move(directory, isolated);
        ct.ThrowIfCancellationRequested();
        Directory.Delete(isolated, recursive: true);
    }

    public async Task<GameLaunchResult> LaunchAsync(InstalledPack pack, AuthResult account, string host, int port, string? javaPath, IReadOnlyList<string> extraJvmArgs, IProgress<LaunchProgress>? progress, CancellationToken ct = default, GamePreferences? preferences = null)
    {
        if (!await IsPreparedAsync(pack)) throw new InvalidOperationException("游戏尚未安装完成。");
        versionLoader.LocalOnly = true;
        await launcher.GetAllVersionsAsync(ct);
        var instance = pack.Instance;
        javaPath = string.IsNullOrWhiteSpace(javaPath) ? await EnsureManagedJavaAsync(await JavaRequirementAsync(instance.VersionId, ct), null, ct) : javaPath;
        forge.JavaPath = javaPath;
        neoForge.JavaPath = javaPath;
        var version = instance.Loader == TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.ModLoader.None ? instance.VersionId :
            await loader.InstallAsync(instance.Loader, instance.VersionId, instance.LoaderVersion, null, ct);
        if (preferences is not null) await preferences.ApplyAsync(instance.GameDirectory!, ct);
        var startup = new GameStartupSignal();
        var launched = await underlying.StartProcessAsync(new LaunchRequest { VersionName = version, Session = account,
            GameDirectory = instance.GameDirectory, JavaPath = javaPath, QuickPlay = new QuickPlay.Multiplayer(host, port), ExtraJvmArguments = extraJvmArgs,
            GameLogObserver = startup,
            MaximumRamMb = preferences?.MaximumMemoryMb, MinimumRamMb = preferences is null ? null : 512,
            ScreenWidth = preferences?.Width, ScreenHeight = preferences?.Height, FullScreen = preferences?.FullScreen ?? false }, true, ct);
        return new GameLaunchResult(launched.ProcessId, startup);
    }

    private sealed class LaunchVersionLoader(IVersionLoader online, IVersionLoader local) : IVersionLoader
    {
        public bool LocalOnly { get; set; }
        public ValueTask<VersionMetadataCollection> GetVersionMetadatasAsync(CancellationToken ct = default) =>
            (LocalOnly ? local : online).GetVersionMetadatasAsync(ct);
    }
}
