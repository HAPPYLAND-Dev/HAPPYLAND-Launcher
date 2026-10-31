using System;
using System.IO;
using System.Text.Json;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

/// <summary>
/// Distilled view of MultiMC / Prism's <c>mmc-pack.json</c>: the Minecraft base version, the
/// mod loader, and the loader's version (null for vanilla). The original component list is
/// rich (intermediary, lwjgl, mojang-stuff, ...); we only retain the loader-relevant bits.
/// </summary>
public sealed record MultiMcPack
{
    /// <summary>Minecraft version id from the <c>net.minecraft</c> component (e.g. <c>"1.21.4"</c>).</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>The mod loader identified by component UID (<see cref="ModLoader.None"/> = vanilla).</summary>
    public ModLoader Loader { get; init; } = ModLoader.None;

    /// <summary>Loader version string, or <c>null</c> when the loader is None.</summary>
    public string? LoaderVersion { get; init; }
}

/// <summary>
/// Parser for MultiMC / Prism <c>mmc-pack.json</c>. The format is a JSON object with a
/// <c>components</c> array; each component is <c>{ uid, version, cachedName?, ... }</c>. We
/// walk the array once, picking up the Minecraft version and (whichever lands first) loader.
/// </summary>
public static class MultiMcPackParser
{
    /// <summary>Parse a raw <c>mmc-pack.json</c> string. Throws <see cref="InvalidDataException"/> when malformed.</summary>
    public static MultiMcPack Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("mmc-pack.json is not valid JSON.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("mmc-pack.json root must be a JSON object.");

            if (!root.TryGetProperty("components", out var components) ||
                components.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("mmc-pack.json is missing the 'components' array.");

            string? mcVersion = null;
            var loader = ModLoader.None;
            string? loaderVersion = null;

            foreach (var component in components.EnumerateArray())
            {
                if (component.ValueKind != JsonValueKind.Object) continue;

                var uid = ReadString(component, "uid");
                var version = ReadString(component, "version");
                if (string.IsNullOrWhiteSpace(uid)) continue;

                switch (uid)
                {
                    case "net.minecraft":
                        mcVersion = version;
                        break;
                    case "net.minecraftforge":
                        loader = ModLoader.Forge;
                        loaderVersion = version;
                        break;
                    case "net.neoforged":
                        loader = ModLoader.NeoForge;
                        loaderVersion = version;
                        break;
                    case "net.fabricmc.fabric-loader":
                        // Fabric Loader is the source of truth for Hyperion's "Fabric loader version";
                        // net.fabricmc.intermediary is the per-MC mapping and we ignore its version here.
                        loader = ModLoader.Fabric;
                        loaderVersion = version;
                        break;
                    case "org.quiltmc.quilt-loader":
                        loader = ModLoader.Quilt;
                        loaderVersion = version;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new InvalidDataException("mmc-pack.json has no 'net.minecraft' component.");

            return new MultiMcPack
            {
                MinecraftVersion = mcVersion!,
                Loader = loader,
                LoaderVersion = loader == ModLoader.None ? null : loaderVersion,
            };
        }
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
