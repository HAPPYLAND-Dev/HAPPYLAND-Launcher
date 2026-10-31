using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// Default <see cref="ISystemJavaProbe"/>: walks JAVA_HOME, PATH, and a per-OS list of
/// well-known install directories, runs <c>java -version</c> on every candidate it finds,
/// and returns the first one whose reported major version matches the requested
/// <see cref="JavaRequirement"/>.
/// </summary>
/// <remarks>
/// <para>
/// We deliberately avoid the Windows registry. Every JRE vendor (Oracle, Eclipse Adoptium,
/// Microsoft OpenJDK, Azul Zulu, Amazon Corretto, Red Hat OpenJDK, GraalVM, ...) has its own
/// registry layout and the keys move between releases. Reading them all is a maintenance
/// pit; well-known directories cover the same ground without registry churn because every
/// vendor's installer drops bits under <c>C:\Program Files\&lt;vendor&gt;\</c> by default.
/// </para>
/// <para>
/// Results are memoised per-instance for the lifetime of the probe so launching three
/// instances in a row doesn't re-run <c>java -version</c> nine times. The cache is keyed on
/// <see cref="JavaRequirement"/>; a null cached value means "looked, found nothing".
/// </para>
/// <para>
/// Output parsing follows the JEP 223 (Java 9+) and legacy (1.x) string formats:
/// <c>"openjdk version \"21.0.2\" ..."</c> and <c>"java version \"1.8.0_311\""</c> respectively.
/// Anything that doesn't match is silently skipped.
/// </para>
/// </remarks>
public sealed class SystemJavaProbe : ISystemJavaProbe
{
    /// <summary>
    /// Per-candidate <c>java -version</c> timeout. A healthy java -version on a warm JVM
    /// returns in under 200 ms; the 3-second cap is generous and exists only to keep a
    /// hung / malfunctioning candidate from blocking the launch path.
    /// </summary>
    public static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(3);

    private static readonly Regex VersionLineRegex = new(
        @"version\s+""(\d+)(?:\.(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ConcurrentDictionary<JavaRequirement, string?> _resultCache = new();
    private readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private readonly bool _isMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <inheritdoc />
    public async Task<string?> FindAsync(JavaRequirement requirement, CancellationToken cancellationToken)
    {
        if (_resultCache.TryGetValue(requirement, out var cached))
            return cached;

        var targetMajor = AdoptiumUrlBuilder.FeatureNumber(requirement);
        string? match = null;

        // Use a HashSet to skip duplicates - the same java.exe can surface from JAVA_HOME,
        // PATH, *and* the well-known directory sweep (e.g. C:\Program Files\Eclipse Adoptium\jdk-21).
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(candidate)) continue;

            var major = await TryGetMajorVersionAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (major == targetMajor)
            {
                match = candidate;
                break;
            }
        }

        _resultCache[requirement] = match;
        return match;
    }

    /// <summary>
    /// Parse a <c>java -version</c> output blob into a major version number. Returns null
    /// when no recognisable "version "X..."" line is present. Public so tests can pin the
    /// behaviour against real-world strings without invoking a process.
    /// </summary>
    /// <remarks>
    /// Recognised shapes:
    /// <list type="bullet">
    /// <item><description><c>openjdk version "21.0.2" 2024-01-16</c> -> 21</description></item>
    /// <item><description><c>java version "17.0.10" 2024-01-16</c> -> 17</description></item>
    /// <item><description><c>java version "1.8.0_311"</c> -> 8 (legacy 1.x naming)</description></item>
    /// <item><description><c>openjdk version "11" 2018-09-25</c> -> 11 (no minor segment)</description></item>
    /// </list>
    /// </remarks>
    public static int? ParseMajor(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = VersionLineRegex.Match(text);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out var major)) return null;

        if (major == 1)
        {
            // Legacy "1.x" naming: the minor segment IS the user-visible major (Java 8 = 1.8).
            if (!match.Groups[2].Success) return null;
            return int.TryParse(match.Groups[2].Value, out var minor) ? minor : null;
        }
        return major;
    }

    /// <summary>
    /// Enumerate every <c>java(.exe)</c> path worth probing on this host, in priority order:
    /// JAVA_HOME -> PATH -> well-known install directories. Order matters because the first
    /// matching candidate wins, and a user-set JAVA_HOME is the strongest signal.
    /// </summary>
    private IEnumerable<string> EnumerateCandidates()
    {
        // 1. JAVA_HOME (the canonical "use this Java" signal on every OS).
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var exe = ResolveJavaExe(javaHome);
            if (exe is not null) yield return exe;
        }

        // 2. First java(.exe) on PATH. Useful for users who installed via brew / apt / winget.
        var onPath = FindOnPath();
        if (onPath is not null) yield return onPath;

        // 3. Well-known install directories per platform.
        foreach (var root in EnumerateWellKnownRoots())
        {
            foreach (var sub in SafeEnumDirectories(root))
            {
                var exe = ResolveJavaExe(sub);
                if (exe is not null) yield return exe;
            }
        }
    }

    /// <summary>
    /// Given a Java home directory (the parent of <c>bin/</c>), return the absolute path to
    /// <c>java(.exe)</c> if present. Handles both the straight <c>home/bin/java</c> layout
    /// (Windows, Linux, vendor archives) and macOS's <c>home/Contents/Home/bin/java</c>.
    /// </summary>
    private string? ResolveJavaExe(string home)
    {
        if (string.IsNullOrWhiteSpace(home)) return null;
        var exeName = _isWindows ? "java.exe" : "java";

        var direct = Path.Combine(home, "bin", exeName);
        if (File.Exists(direct)) return direct;

        // macOS bundles: foo.jdk/Contents/Home/bin/java
        if (_isMacOs)
        {
            var macHome = Path.Combine(home, "Contents", "Home", "bin", exeName);
            if (File.Exists(macHome)) return macHome;
        }
        return null;
    }

    /// <summary>
    /// Walk PATH looking for the first directory that contains <c>java(.exe)</c>.
    /// </summary>
    private string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        var exeName = _isWindows ? "java.exe" : "java";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed;
            try { trimmed = dir.Trim().Trim('"'); }
            catch { continue; }
            if (string.IsNullOrEmpty(trimmed)) continue;

            string candidate;
            try { candidate = Path.Combine(trimmed, exeName); }
            catch { continue; }
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Per-platform list of directories where Java vendors traditionally drop their bits.
    /// Every entry is a parent dir we'll scan one level deep, looking for vendor sub-folders
    /// like <c>jdk-21</c> or <c>temurin-17.jdk</c>.
    /// </summary>
    private IEnumerable<string> EnumerateWellKnownRoots()
    {
        if (_isWindows)
        {
            string? pf = SafeFolder(Environment.SpecialFolder.ProgramFiles);
            string? pfx86 = SafeFolder(Environment.SpecialFolder.ProgramFilesX86);
            foreach (var root in new[] { pf, pfx86 })
            {
                if (root is null) continue;
                yield return Path.Combine(root, "Java");
                yield return Path.Combine(root, "Eclipse Adoptium");
                yield return Path.Combine(root, "Eclipse Foundation");
                yield return Path.Combine(root, "Microsoft");
                yield return Path.Combine(root, "AdoptOpenJDK");
                yield return Path.Combine(root, "Zulu");
                yield return Path.Combine(root, "Amazon Corretto");
                yield return Path.Combine(root, "BellSoft");
                yield return Path.Combine(root, "Semeru");
                yield return Path.Combine(root, "GraalVM");
            }
        }
        else if (_isMacOs)
        {
            yield return "/Library/Java/JavaVirtualMachines";
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                yield return Path.Combine(home, "Library", "Java", "JavaVirtualMachines");
            yield return "/opt/homebrew/opt";
            yield return "/usr/local/opt";
        }
        else
        {
            // Linux + everything else.
            yield return "/usr/lib/jvm";
            yield return "/usr/java";
            yield return "/opt/java";
            yield return "/opt/jdk";
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                yield return Path.Combine(home, ".sdkman", "candidates", "java");
        }
    }

    private static string? SafeFolder(Environment.SpecialFolder f)
    {
        try { return Environment.GetFolderPath(f); }
        catch { return null; }
    }

    private static IEnumerable<string> SafeEnumDirectories(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            // Permission denied / IO error / race with rename. Nothing useful here.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Run <c>java -version</c> on <paramref name="javaExe"/> and extract the major version.
    /// Times out at <see cref="VersionProbeTimeout"/> so a hung candidate can't block the
    /// launch path. Returns null on any failure (process didn't start, exit code non-zero,
    /// timeout, unrecognised output) - the caller treats null as "this candidate didn't match".
    /// </summary>
    private static async Task<int?> TryGetMajorVersionAsync(string javaExe, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            // java writes its version banner to stderr (legacy Sun behaviour preserved by
            // every modern JDK). Read both streams just in case a vendor changed that.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(VersionProbeTimeout);

            var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignored */ }
                return null;
            }

            var stderr = await stderrTask.ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            return ParseMajor(stderr + "\n" + stdout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Process.Start can throw Win32Exception (ERROR_FILE_NOT_FOUND) if the candidate
            // dangles, or InvalidOperationException on certain pipe failures. None of those
            // are recoverable for *this* candidate - just skip and let the caller try the
            // next one.
            return null;
        }
    }
}
