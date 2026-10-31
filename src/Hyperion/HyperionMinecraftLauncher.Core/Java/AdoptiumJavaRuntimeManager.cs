using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// Downloads Adoptium Temurin JREs on demand and caches them under
/// <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/java/{requirement}/</c>. The launcher
/// service calls <see cref="EnsureRuntimeAsync"/> right before <see cref="IUnderlyingLauncher"/>
/// so the resolved <c>java(.exe)</c> path can be threaded into the launch options.
///
/// Zips are extracted with <see cref="ZipFile"/> on .NET. Linux/macOS tarballs shell out to
/// the system <c>tar</c> binary (universally available on those platforms; avoids hauling in
/// a managed tar dependency for the one cross-platform case).
/// </summary>
public sealed class AdoptiumJavaRuntimeManager : IJavaRuntimeManager
{
    private readonly HttpClient _http;
    private readonly string _rootDirectory;
    private readonly string _os;
    private readonly string _arch;
    private readonly ISystemJavaProbe? _systemProbe;

    /// <summary>Default constructor: uses the OS-default cache root and a fresh HttpClient.</summary>
    public AdoptiumJavaRuntimeManager()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, DefaultRootDirectory(), DetectOs(), DetectArch(), new SystemJavaProbe())
    {
    }

    /// <summary>Constructor for tests: caller supplies the HttpClient, cache root, and OS/arch.</summary>
    /// <remarks>
    /// Kept as a 4-arg overload so existing test sites compile unchanged. Tests that pre-date the
    /// v0.32.11 system-probe path pass a null probe implicitly here; that disables the probe and
    /// keeps the old "cache or download" two-step contract intact for those tests.
    /// </remarks>
    public AdoptiumJavaRuntimeManager(HttpClient http, string rootDirectory, string os, string arch)
        : this(http, rootDirectory, os, arch, systemProbe: null)
    {
    }

    /// <summary>
    /// Full constructor with the v0.32.11 system-probe extension. When <paramref name="systemProbe"/>
    /// is non-null, <see cref="EnsureRuntimeAsync"/> queries it BETWEEN the managed-cache check
    /// and the Adoptium download, so a host with an existing matching JDK / JRE under JAVA_HOME,
    /// PATH, or a well-known install dir is preferred over a fresh download.
    /// </summary>
    public AdoptiumJavaRuntimeManager(HttpClient http, string rootDirectory, string os, string arch, ISystemJavaProbe? systemProbe)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _rootDirectory = !string.IsNullOrWhiteSpace(rootDirectory)
            ? rootDirectory
            : throw new ArgumentException("rootDirectory is required", nameof(rootDirectory));
        _os = !string.IsNullOrWhiteSpace(os) ? os : throw new ArgumentException("os is required", nameof(os));
        _arch = !string.IsNullOrWhiteSpace(arch) ? arch : throw new ArgumentException("arch is required", nameof(arch));
        _systemProbe = systemProbe;
    }

    /// <summary>Resolve the platform default cache root: <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/java</c>.</summary>
    public static string DefaultRootDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperionMinecraftLauncher", "java");

    /// <summary>Detect the Adoptium-API OS string for the current process platform.</summary>
    public static string DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "mac";
        // Fall back to linux; Adoptium has no FreeBSD/etc and linux is a sensible default.
        return "linux";
    }

    /// <summary>Detect the Adoptium-API architecture string for the current process platform.</summary>
    public static string DetectArch() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x32",
        Architecture.Arm64 => "aarch64",
        Architecture.Arm => "arm",
        _ => "x64",
    };

    /// <inheritdoc />
    public async Task<string> EnsureRuntimeAsync(
        JavaRequirement requirement,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetDir = RuntimeDirectory(requirement);

        // 1. Managed cache. Cheapest check (one File.Exists), and a runtime we put there
        //    ourselves is known-good for this Minecraft major. Always wins.
        var existingExe = FindJavaExecutable(targetDir);
        if (existingExe is not null)
        {
            progress?.Report(1.0);
            return existingExe;
        }

        // 2. System probe (v0.32.11). Look for a matching JDK / JRE already installed via
        //    JAVA_HOME, PATH, or a well-known vendor directory. Skips the multi-hundred-MB
        //    Adoptium download whenever the host already has a usable Java of the right
        //    major. Probe failures degrade silently to step 3.
        if (_systemProbe is not null)
        {
            try
            {
                var systemExe = await _systemProbe.FindAsync(requirement, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(systemExe))
                {
                    progress?.Report(1.0);
                    return systemExe;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // The probe contract says it shouldn't throw on individual candidate failures;
                // a real exception here means the probe itself is broken. Don't let that block
                // the launch - fall through to the download path.
            }
        }

        // 3. Download from Adoptium and extract into the managed cache.
        Directory.CreateDirectory(targetDir);

        var url = AdoptiumUrlBuilder.BuildBinaryUrl(requirement, _os, _arch);
        var archiveExtension = _os == "windows" ? ".zip" : ".tar.gz";
        var tempArchive = Path.Combine(targetDir, $"download{archiveExtension}.tmp");

        try
        {
            await DownloadAsync(url, tempArchive, progress, cancellationToken).ConfigureAwait(false);
            await ExtractAsync(tempArchive, targetDir, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Always best-effort cleanup of the temp archive - successful extraction or not.
            try { if (File.Exists(tempArchive)) File.Delete(tempArchive); }
            catch { /* ignored; not critical */ }
        }

        var javaExe = FindJavaExecutable(targetDir)
            ?? throw new InvalidOperationException(
                $"Adoptium archive extracted but no 'bin/java' was found under '{targetDir}'.");

        progress?.Report(1.0);
        return javaExe;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledJavaRuntime>> ListInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = new List<InstalledJavaRuntime>();
        if (!Directory.Exists(_rootDirectory))
        {
            IReadOnlyList<InstalledJavaRuntime> empty = list;
            return Task.FromResult(empty);
        }

        foreach (JavaRequirement req in Enum.GetValues(typeof(JavaRequirement)))
        {
            var dir = RuntimeDirectory(req);
            if (!Directory.Exists(dir)) continue;
            var exe = FindJavaExecutable(dir);
            if (exe is null) continue;
            var home = Path.GetDirectoryName(Path.GetDirectoryName(exe)) ?? dir;
            var size = SafeDirectorySize(dir);
            var version = ParseVersionFromHomePath(home) ?? new Version(AdoptiumUrlBuilder.FeatureNumber(req), 0);
            list.Add(new InstalledJavaRuntime(req, exe, home, version, size));
        }

        IReadOnlyList<InstalledJavaRuntime> result = list;
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task RemoveAsync(JavaRequirement requirement, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dir = RuntimeDirectory(requirement);
        if (Directory.Exists(dir))
        {
            // Recursive: tear down the entire runtime tree. Any locked file (e.g. a running game
            // process still has java.exe open) bubbles up as IOException for the caller to surface.
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    }

    private string RuntimeDirectory(JavaRequirement requirement) =>
        Path.Combine(_rootDirectory, requirement.ToString().ToLowerInvariant());

    private async Task DownloadAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destination);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;
            if (progress is not null && total is long t && t > 0)
            {
                // Reserve the 0.95..1.0 band for the extraction step, so the bar fills smoothly
                // end-to-end rather than hitting 100% before extraction finishes.
                var frac = Math.Min(0.95, (double)downloaded / t * 0.95);
                progress.Report(frac);
            }
        }
    }

    private async Task ExtractAsync(string archivePath, string targetDir, CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase))
        {
            // Sync API; wrap so we don't block the calling thread.
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Shell out to tar. The -z flag handles gzip, -x extract, -f file. -C cd to target dir.
            var psi = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{targetDir}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start 'tar' to extract the Adoptium archive.");
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"tar exited with code {proc.ExitCode} while extracting '{archivePath}': {err}");
            }
        }
    }

    /// <summary>
    /// Locate the <c>bin/java(.exe)</c> under <paramref name="rootDir"/>. Adoptium tarballs land
    /// in a single sub-folder like <c>jdk-21.0.2+13-jre/</c>, so we search one level deep.
    /// </summary>
    private string? FindJavaExecutable(string rootDir)
    {
        if (!Directory.Exists(rootDir)) return null;

        var exeName = _os == "windows" ? "java.exe" : "java";

        // 1) bin directly under root (some installs)
        var direct = Path.Combine(rootDir, "bin", exeName);
        if (File.Exists(direct)) return direct;

        // 2) one extracted folder down: rootDir/<jdk-x.y.z+b-jre>/bin/java
        foreach (var sub in Directory.EnumerateDirectories(rootDir))
        {
            // macOS Adoptium bundles use Contents/Home/bin/java.
            var macHome = Path.Combine(sub, "Contents", "Home", "bin", exeName);
            if (File.Exists(macHome)) return macHome;

            var nested = Path.Combine(sub, "bin", exeName);
            if (File.Exists(nested)) return nested;
        }

        return null;
    }

    private static long SafeDirectorySize(string dir)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* skip unreadable files */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Pull a parseable Version out of an Adoptium folder name like <c>"jdk-21.0.2+13-jre"</c>.
    /// </summary>
    private static Version? ParseVersionFromHomePath(string home)
    {
        var name = Path.GetFileName(home);
        if (string.IsNullOrEmpty(name)) return null;

        // "jdk-21.0.2+13-jre" -> "21.0.2".  Strip everything before the first digit and after the first non-digit/non-dot.
        var start = 0;
        while (start < name.Length && !char.IsDigit(name[start])) start++;
        if (start >= name.Length) return null;
        var end = start;
        while (end < name.Length && (char.IsDigit(name[end]) || name[end] == '.')) end++;
        var numeric = name.Substring(start, end - start).TrimEnd('.');
        if (numeric.Length == 0) return null;
        // Version requires at least major.minor. Pad with .0 if necessary.
        if (!numeric.Contains('.')) numeric += ".0";
        return Version.TryParse(numeric, out var v) ? v : null;
    }
}
