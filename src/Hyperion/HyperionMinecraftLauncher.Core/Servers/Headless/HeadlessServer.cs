using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// One headless (CLI / dedicated) Minecraft server registered with Hyperion. The launcher
/// owns the on-disk layout under <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/headless_servers/{Id}/</c>
/// (one folder per server), so add / edit / delete are local file operations and never rewrite
/// a single master list.
/// </summary>
/// <remarks>
/// The launcher's server-jar download wiring is intentionally deferred to v0.29; this record only
/// describes the metadata and the on-disk skeleton (<c>eula.txt</c>, <c>server.properties</c>).
/// </remarks>
public sealed record HeadlessServer
{
    /// <summary>Stable identifier (GUID hex). Doubles as the on-disk folder name.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in the headless servers page.</summary>
    public required string Name { get; init; }

    /// <summary>Minecraft version id (e.g. <c>"1.21.5"</c>). Matches a manifest entry.</summary>
    public required string VersionId { get; init; }

    /// <summary>Absolute path of the server's working directory. Equals
    /// <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/headless_servers/{Id}</c> for stores using
    /// the default root.</summary>
    public required string Path { get; init; }

    /// <summary>JVM max heap in MiB. Default 2 GiB matches the Mojang dedicated-server recommendation.</summary>
    public int RamMb { get; init; } = 2048;

    /// <summary>TCP port the server listens on. Default 25565.</summary>
    public int Port { get; init; } = 25565;

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last time the server process was started (<c>null</c> = never).</summary>
    public DateTimeOffset? LastStartedAt { get; init; }
}
