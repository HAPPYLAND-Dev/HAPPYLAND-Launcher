namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Inputs the New Headless Server dialog hands to <see cref="IHeadlessServerStore.CreateAsync"/>.
/// The store assigns the <c>Id</c> and <c>Path</c>; the rest comes straight from the UI form.
/// </summary>
public sealed record HeadlessServerCreateRequest
{
    /// <summary>Display name (required - the user types this in the dialog).</summary>
    public required string Name { get; init; }

    /// <summary>Minecraft version id from the manifest dropdown.</summary>
    public required string VersionId { get; init; }

    /// <summary>JVM max heap in MiB. Default 2 GiB.</summary>
    public int RamMb { get; init; } = 2048;

    /// <summary>TCP port. Default vanilla 25565.</summary>
    public int Port { get; init; } = 25565;
}
