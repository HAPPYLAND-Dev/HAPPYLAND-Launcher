using System;
using System.Globalization;
using System.IO;
using fNbt;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// Slim extracted view of a Minecraft <c>level.dat</c>: the handful of fields the
/// Worlds tab actually shows. Built by <see cref="LevelDatReader.Read"/>.
/// </summary>
public sealed record LevelDatInfo
{
    /// <summary>NBT <c>Data.LevelName</c> - the in-game display name of the world.</summary>
    public string? LevelName { get; init; }

    /// <summary>ISO 8601 round-trip representation of NBT <c>Data.LastPlayed</c> (ms since epoch UTC).</summary>
    public string? LastPlayedIso { get; init; }

    /// <summary>NBT <c>Data.Version.Name</c> (e.g. <c>"1.21.5"</c>). Null on pre-1.9 worlds.</summary>
    public string? VersionName { get; init; }

    /// <summary>NBT <c>Data.GameType</c>. Null when absent.</summary>
    public int? GameType { get; init; }
}

/// <summary>
/// Minimal <c>level.dat</c> reader: opens the gzip-compressed NBT and pulls just enough fields
/// to populate <see cref="LevelDatInfo"/>. Defensive: returns <c>null</c> on any read/parse error.
/// </summary>
public static class LevelDatReader
{
    /// <summary>Open the file, parse the gzip NBT, return the small projection. <c>null</c> on any failure.</summary>
    public static LevelDatInfo? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        try
        {
            var file = new NbtFile();
            file.LoadFromFile(path);

            // The root is an unnamed compound with a single "Data" child compound.
            var data = file.RootTag.Get<NbtCompound>("Data");
            if (data is null) return null;

            string? levelName = data.Get<NbtString>("LevelName")?.Value;

            string? lastPlayedIso = null;
            if (data.Get<NbtLong>("LastPlayed") is { } lastPlayed)
            {
                var dto = DateTimeOffset.FromUnixTimeMilliseconds(lastPlayed.Value);
                lastPlayedIso = dto.ToString("O", CultureInfo.InvariantCulture);
            }

            string? versionName = null;
            if (data.Get<NbtCompound>("Version") is { } versionTag)
            {
                versionName = versionTag.Get<NbtString>("Name")?.Value;
            }

            int? gameType = data.Get<NbtInt>("GameType")?.Value;

            return new LevelDatInfo
            {
                LevelName = levelName,
                LastPlayedIso = lastPlayedIso,
                VersionName = versionName,
                GameType = gameType,
            };
        }
        catch
        {
            return null;
        }
    }
}
