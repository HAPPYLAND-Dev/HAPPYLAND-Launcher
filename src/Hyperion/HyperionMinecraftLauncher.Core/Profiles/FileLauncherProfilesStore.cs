using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;

/// <summary>
/// JSON parser for Mojang's <c>launcher_profiles.json</c>. We tolerate every field we
/// don't care about (auth db, analytics tokens, etc.) - they pass through untouched
/// because we only read, never write.
/// </summary>
public sealed class FileLauncherProfilesStore : ILauncherProfilesStore
{
    /// <inheritdoc />
    public async Task<LauncherProfilesFile> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return new LauncherProfilesFile { Profiles = Array.Empty<LauncherProfile>(), SchemaVersion = 0 };

        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = doc.RootElement;
        var list = new List<LauncherProfile>();

        if (root.TryGetProperty("profiles", out var profilesEl) && profilesEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in profilesEl.EnumerateObject())
                list.Add(Parse(entry.Name, entry.Value));
        }

        int schemaVersion = 0;
        if (root.TryGetProperty("version", out var verEl) && verEl.ValueKind == JsonValueKind.Number)
            schemaVersion = verEl.GetInt32();

        return new LauncherProfilesFile { Profiles = list, SchemaVersion = schemaVersion };
    }

    private static LauncherProfile Parse(string key, JsonElement el)
    {
        return new LauncherProfile
        {
            Key = key,
            Name = ReadString(el, "name") ?? string.Empty,
            Type = ReadString(el, "type") ?? "custom",
            Created = ReadDate(el, "created"),
            LastUsed = ReadDate(el, "lastUsed"),
            LastVersionId = ReadString(el, "lastVersionId"),
            Icon = ReadString(el, "icon"),
            GameDir = ReadString(el, "gameDir"),
            JavaDir = ReadString(el, "javaDir"),
            JavaArgs = ReadString(el, "javaArgs"),
            ResolutionWidth = ReadResolution(el, "width"),
            ResolutionHeight = ReadResolution(el, "height"),
        };
    }

    private static string? ReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement el, string property)
    {
        var s = ReadString(el, property);
        return DateTimeOffset.TryParse(s, out var dt) ? dt : null;
    }

    private static int? ReadResolution(JsonElement el, string property)
    {
        if (!el.TryGetProperty("resolution", out var resEl) || resEl.ValueKind != JsonValueKind.Object)
            return null;
        return resEl.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;
    }
}
