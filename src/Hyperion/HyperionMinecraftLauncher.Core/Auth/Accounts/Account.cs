using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

/// <summary>
/// One cached player identity in the multi-account roster. The launcher persists a list
/// of these (plus a single "active" id) so the user can switch between Microsoft accounts
/// without re-running device-code sign-in every time, and so an offline session shows up
/// in the same UI affordance as a Microsoft session.
/// </summary>
/// <remarks>
/// <see cref="Id"/> mirrors MSAL's <c>IAccount.HomeAccountId.Identifier</c> for online
/// accounts, which is the key MSAL itself uses to look up a refresh token. For offline
/// entries the launcher synthesises an id so the same store can hold either kind.
/// </remarks>
public sealed record Account
{
    /// <summary>MSAL home-account id (online) or a synthetic id for offline entries.</summary>
    public required string Id { get; init; }

    /// <summary>Display name shown on the header chip and the account switcher menu.</summary>
    public required string Username { get; init; }

    /// <summary>Player UUID (Java edition format, no dashes).</summary>
    public required string Uuid { get; init; }

    /// <summary>
    /// URI of the cached head/face PNG used as the chip avatar when this account isn't
    /// currently signed in (silent sign-in then replaces it with the live skin head).
    /// Bundled Steve URI is the safe offline fallback.
    /// </summary>
    public string? SkinHeadUri { get; init; }

    /// <summary>Timestamp of the last successful sign-in / launch under this account.</summary>
    public required DateTimeOffset LastUsedAt { get; init; }

    /// <summary>True when this is an offline-only entry; <see cref="Id"/> is then synthetic.</summary>
    public required bool IsOffline { get; init; }
}
