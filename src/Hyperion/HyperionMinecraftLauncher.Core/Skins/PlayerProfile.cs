using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Profile snapshot returned by <c>GET https://api.minecraftservices.com/minecraft/profile</c>.
/// Contains the player's id and name plus the lists of skins and capes they own; each
/// entry carries a <see cref="OwnedSkin.State"/> / <see cref="OwnedCape.State"/> flag of
/// <c>ACTIVE</c> or <c>INACTIVE</c> identifying the currently-applied texture.
/// </summary>
public sealed record PlayerProfile
{
    /// <summary>Player UUID (32 chars, no dashes — same format as <c>AuthResult.Uuid</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Player's in-game display name.</summary>
    public required string Name { get; init; }

    /// <summary>All skins on the account (typically up to four).</summary>
    public required IReadOnlyList<OwnedSkin> Skins { get; init; }

    /// <summary>All capes the account has earned.</summary>
    public required IReadOnlyList<OwnedCape> Capes { get; init; }
}

/// <summary>
/// One skin entry from <see cref="PlayerProfile.Skins"/>. The Mojang API serves all
/// historical skins as inactive plus the currently-active one.
/// </summary>
public sealed record OwnedSkin
{
    /// <summary>Skin id (server-assigned GUID).</summary>
    public required string Id { get; init; }

    /// <summary><c>ACTIVE</c> or <c>INACTIVE</c>.</summary>
    public required string State { get; init; }

    /// <summary>Direct PNG URL on <c>textures.minecraft.net</c>.</summary>
    public required string Url { get; init; }

    /// <summary><c>CLASSIC</c> or <c>SLIM</c> (the API capitalises both).</summary>
    public required string Variant { get; init; }

    /// <summary>Optional Mojang-assigned alias (often null).</summary>
    public string? Alias { get; init; }
}

/// <summary>
/// One cape entry from <see cref="PlayerProfile.Capes"/>. Most accounts have zero;
/// some have many (e.g. Migrator, Founder's, Minecon).
/// </summary>
public sealed record OwnedCape
{
    /// <summary>Cape id (server-assigned GUID; used for <c>PUT /capes/active</c>).</summary>
    public required string Id { get; init; }

    /// <summary><c>ACTIVE</c> or <c>INACTIVE</c>.</summary>
    public required string State { get; init; }

    /// <summary>Direct PNG URL on <c>textures.minecraft.net</c>.</summary>
    public required string Url { get; init; }

    /// <summary>Human-readable label (e.g. <c>"Migrator"</c>).</summary>
    public required string Alias { get; init; }
}
