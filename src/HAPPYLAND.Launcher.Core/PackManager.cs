using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text.Json;
using Happyland.Contracts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

namespace Happyland.Launcher.Core;

public sealed record InstalledPack(string Version, string Sha256, Instance Instance, IReadOnlyList<string> Files, string? TransactionId = null);

public sealed class PackManager(string root, HttpClient http)
{
    private readonly string gameDirectory = Path.Combine(root, "game");
    private readonly string statePath = Path.Combine(root, "installed-pack.json");
    private readonly string journalPath = Path.Combine(root, "install-journal.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InstalledPack?> GetInstalledAsync(CancellationToken ct = default)
    {
        await RecoverAsync(ct);
        return await ReadInstalledAsync(ct);
    }

    private async Task<InstalledPack?> ReadInstalledAsync(CancellationToken ct)
    {
        if (!File.Exists(statePath)) return null;
        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<InstalledPack>(stream, JsonOptions, ct);
    }

    public static bool IsCurrent(InstalledPack? installed, LauncherConfig config) =>
        installed is not null && installed.Version == config.PackVersion && installed.Sha256 == config.PackSha256;

    public async Task<InstalledPack> InstallAsync(LauncherConfig config, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        var transactionId = Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(root, ".install-" + transactionId);
        Directory.CreateDirectory(temporary);
        try
        {
            var archive = Path.Combine(temporary, "pack.mrpack");
            using (var response = await http.GetAsync(config.PackUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(archive);
                await input.CopyToAsync(output, ct);
            }
            await using (var input = File.OpenRead(archive))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
                if (input.Length != config.PackSize || !string.Equals(digest, config.PackSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("整合包校验失败，请重试。");
            }
            var importer = new ModrinthModpackImporter(Path.Combine(temporary, "instances"), http);
            var instance = await importer.ImportAsync(archive, "HAPPYLAND", progress, ct);
            var stage = instance.GameDirectory!;
            var newFiles = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(stage, path)).Where(path => !IsSave(path) || !SaveExists(path)).ToArray();
            var old = await GetInstalledAsync(ct);
            var oldFiles = new HashSet<string>((old?.Files ?? []).Where(path => !IsSave(path)), StringComparer.Ordinal);
            var newSet = new HashSet<string>(newFiles, StringComparer.Ordinal);
            Directory.CreateDirectory(gameDirectory);
            var disabledIds = oldFiles.Where(IsMod).Select(path => Path.Combine(gameDirectory, path) + ".disabled")
                .Where(File.Exists).SelectMany(ReadModIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var backup = Path.Combine(temporary, "backup");
            var operations = new List<FileOperation>();
            var journal = new InstallJournal(transactionId, temporary, operations);
            SaveJournal(journal);
            try
            {
                foreach (var relative in oldFiles.Union(newSet))
                {
                    ct.ThrowIfCancellationRequested();
                    var current = Path.Combine(gameDirectory, relative);
                    var disabled = IsMod(relative) && (File.Exists(current + ".disabled") ||
                        (newSet.Contains(relative) && ReadModIds(Path.Combine(stage, relative)).Any(disabledIds.Contains)));
                    var target = disabled ? current + ".disabled" : current;
                    if (!oldFiles.Contains(relative) && File.Exists(target))
                        throw new IOException($"整合包文件与玩家文件冲突：{relative}");
                    string? saved = null;
                    if (File.Exists(target))
                    {
                        saved = Path.Combine(backup, relative + (disabled ? ".disabled" : ""));
                        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                        File.Copy(target, saved);
                    }
                    operations.Add(new FileOperation(target, saved));
                    SaveJournal(journal);
                    if (newSet.Contains(relative))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(Path.Combine(stage, relative), target, true);
                    }
                    else if (File.Exists(target)) File.Delete(target);
                }
                var installed = new InstalledPack(config.PackVersion, config.PackSha256,
                    instance with { GameDirectory = gameDirectory }, newFiles, transactionId);
                var stateTemp = Path.Combine(temporary, "state.json");
                await File.WriteAllTextAsync(stateTemp, JsonSerializer.Serialize(installed, JsonOptions), ct);
                File.Move(stateTemp, statePath, true);
                File.Delete(journalPath);
                return installed;
            }
            catch
            {
                await RecoverAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            if (!File.Exists(journalPath) && Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private sealed record FileOperation(string Target, string? Backup);
    private sealed record InstallJournal(string TransactionId, string Temporary, List<FileOperation> Operations);

    private void SaveJournal(InstallJournal journal)
    {
        var temp = journalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, JsonOptions));
        File.Move(temp, journalPath, true);
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        if (!File.Exists(journalPath)) return;
        var journal = JsonSerializer.Deserialize<InstallJournal>(await File.ReadAllTextAsync(journalPath, ct), JsonOptions)
            ?? throw new InvalidDataException("无法读取整合包更新记录。");
        var installed = await ReadInstalledAsync(ct);
        if (installed?.TransactionId != journal.TransactionId)
        {
            foreach (var operation in journal.Operations.AsEnumerable().Reverse())
            {
                if (operation.Backup is null) File.Delete(operation.Target);
                else File.Copy(operation.Backup, operation.Target, true);
            }
        }
        File.Delete(journalPath);
        if (Directory.Exists(journal.Temporary)) Directory.Delete(journal.Temporary, true);
    }

    private static IEnumerable<string> ReadModIds(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric is not null)
            {
                using var json = JsonDocument.Parse(fabric.Open());
                if (json.RootElement.TryGetProperty("id", out var id) && id.GetString() is { } value) return [value];
            }
            var quilt = zip.GetEntry("quilt.mod.json");
            if (quilt is not null)
            {
                using var json = JsonDocument.Parse(quilt.Open());
                if (json.RootElement.TryGetProperty("quilt_loader", out var loader) && loader.TryGetProperty("id", out var id) && id.GetString() is { } value) return [value];
            }
            var forge = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
            if (forge is not null)
            {
                using var reader = new StreamReader(forge.Open());
                return Regex.Matches(reader.ReadToEnd(), "(?m)^\\s*modId\\s*=\\s*[\"']([^\"']+)[\"']")
                    .Select(match => match.Groups[1].Value).ToArray();
            }
        }
        catch (InvalidDataException) { }
        catch (JsonException) { }
        return [];
    }

    private bool SaveExists(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        var world = Path.Combine(gameDirectory, parts[0], parts[1]);
        return Directory.Exists(world) || File.Exists(world);
    }

    private static bool IsSave(string path) => path.Replace('\\', '/').StartsWith("saves/", StringComparison.OrdinalIgnoreCase);

    private static bool IsMod(string path) => path.Replace('\\', '/').StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
}
