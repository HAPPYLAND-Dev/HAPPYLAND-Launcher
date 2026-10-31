using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

/// <summary>
/// Microsoft account sign-in for Minecraft: Java Edition. Hides the CmlLib /
/// XboxAuthNet stack so the Core launcher service can speak in neutral DTOs
/// (<see cref="AuthResult"/>) and tests can swap in a fake without pulling in WebView2.
/// </summary>
/// <remarks>
/// The multi-account surface (added in v0.27.0) sits on top of the existing
/// single-account methods. The "old" overloads operate on whatever account the
/// <see cref="Accounts.IAccountStore"/> currently marks as active; the new
/// id-suffixed overloads target a specific cached account by MSAL home-account id.
/// </remarks>
public interface IMicrosoftAuthService
{
    /// <summary>
    /// Raised when the underlying OAuth flow needs the user to enter a device code on a separate device / browser tab.
    /// The view-model subscribes and surfaces the message in the launcher log + opens the verification URL.
    /// </summary>
    event EventHandler<MicrosoftDeviceCodeInfo>? DeviceCodeRequested;

    /// <summary>True when the local cache already holds any Microsoft refresh token,
    /// so <see cref="SignInAsync"/> can complete silently without showing a browser.</summary>
    bool HasCachedAccount { get; }

    /// <summary>Try the cached refresh token first; fall back to interactive sign-in
    /// (system browser / WebView2) if the cache is empty or expired.</summary>
    Task<AuthResult> SignInAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Silent-only sign-in against the active account (or the only cached account when
    /// none is marked active). Throws if no cached account exists or the refresh token
    /// is invalid. Use this on startup to auto-restore the previous session without UI.
    /// </summary>
    Task<AuthResult> SignInSilentlyAsync(CancellationToken cancellationToken);

    /// <summary>Skip the cache and force an interactive sign-in.</summary>
    Task<AuthResult> SignInInteractiveAsync(CancellationToken cancellationToken);

    /// <summary>Clear the local cache for the active account.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enumerate every cached account known to MSAL / XboxAuthNet, hydrated into our
    /// neutral <see cref="Account"/> DTO. Returns an empty list when the cache is empty.
    /// </summary>
    Task<IReadOnlyList<Account>> ListCachedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Silent token acquisition for a specific cached account (looked up by
    /// <see cref="Account.Id"/>). Throws when the id is unknown or the refresh
    /// token has expired - the caller should fall back to <see cref="SignInInteractiveAsync"/>.
    /// </summary>
    Task<AuthResult> SignInSilentlyAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>Remove the specified cached account from MSAL / XboxAuthNet's on-disk cache.</summary>
    Task SignOutAsync(string accountId, CancellationToken cancellationToken);
}
