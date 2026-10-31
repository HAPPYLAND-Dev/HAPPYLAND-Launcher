namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// A single skin entry surfaced by an <see cref="ISkinBrowser"/> (NameMC, etc.).
/// All fields are populated best-effort: when a community source omits an uploader
/// or tag list, the corresponding property is null / empty rather than throwing.
/// </summary>
/// <remarks>
/// Records are immutable so the UI can keep a snapshot across panel re-renders
/// without worrying about the underlying browser mutating shared state.
/// </remarks>
/// <param name="Id">Stable identifier inside the source (e.g. NameMC skin hash, or <c>mojang:{uuid}</c> for live player results).</param>
/// <param name="SourceUrl">Browsable page URL on the source site (for "Open in browser").</param>
/// <param name="ThumbnailUrl">Pre-rendered head/body thumbnail (used in the gallery grid).</param>
/// <param name="PngDownloadUrl">Direct URL to the raw 64x64 skin PNG.</param>
/// <param name="Variant">Arm-width variant detected on the source.</param>
/// <param name="UploaderName">Optional user-name of the uploader (null when anonymous).</param>
/// <param name="Tags">Free-form tags (e.g. "boy", "anime"). Empty array when none.</param>
/// <param name="Likes">Likes / hearts shown on the source page (0 when unknown).</param>
/// <param name="IsLivePlayerSkin">
/// True when the record was resolved through Mojang's public profile API (live player
/// skin lookup) rather than from a community gallery upload. UI uses this to surface a
/// dedicated "Mojang account: {name}" label so the user can tell at a glance that the
/// tile points at a real online player's current skin, not an anonymous gallery entry.
/// Added in v0.32.5 alongside the hybrid nickname-search resolver in <see cref="MineSkinBrowser"/>.
/// </param>
public sealed record BrowsedSkin(
    string Id,
    string SourceUrl,
    string ThumbnailUrl,
    string PngDownloadUrl,
    SkinVariant Variant,
    string? UploaderName,
    string[] Tags,
    int Likes,
    bool IsLivePlayerSkin = false);
