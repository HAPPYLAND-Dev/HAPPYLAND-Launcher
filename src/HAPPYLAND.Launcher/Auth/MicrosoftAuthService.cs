using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Auth;

/// <summary>
/// Production <see cref="IMicrosoftAuthService"/>. Backed by <c>CmlLib.Core.Auth.Microsoft.JELoginHandler</c>
/// with the OAuth provider swapped to <c>MsalDeviceCodeProvider</c>:
///
///   * The default XboxAuthNet OAuth flow tries to host a WebView2 instance, which has no
///     binding into Avalonia and crashes with <c>"Current platform does not support to
///     provide default WebUI."</c> on first sign-in.
///   * The MSAL device-code flow reports a short user-code and verification URL to the UI.
///     MSAL polls in the background while the user completes verification.
///
/// Multi-account roster (v0.27.0+): the service also fronts an <see cref="IAccountStore"/>
/// that mirrors the XboxAuthNet account manager into our neutral <see cref="Account"/> DTO
/// + tracks which one is "active". Every successful sign-in upserts the account into the
/// store with a fresh <c>LastUsedAt</c> stamp.
/// </summary>
public sealed class MicrosoftAuthService : IMicrosoftAuthService
{
    /// <summary>The Mojang/Minecraft MSAL client_id - the same GUID every OSS launcher uses.</summary>
    private string MinecraftMsalClientId = "499c8d36-be2a-4231-9ebd-ef291b7bb64c";

    private readonly JELoginHandler _handler;
    private readonly ILauncherLogger _logger;
    private readonly IAccountStore _accountStore;

    /// <inheritdoc />
    public event EventHandler<MicrosoftDeviceCodeInfo>? DeviceCodeRequested;

    /// <summary>Default constructor: account cache under <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.json</c>.</summary>
    public MicrosoftAuthService(ILauncherLogger logger)
        : this(logger, DefaultAccountCachePath(), new FileAccountStore()) { }

    /// <summary>Test / advanced constructor with an explicit cache path. Uses the default JSON store.</summary>
    public MicrosoftAuthService(ILauncherLogger logger, string accountCachePath)
        : this(logger, accountCachePath, new FileAccountStore()) { }

    /// <summary>Full constructor with an injected account store (tests, custom storage layouts).</summary>
    public MicrosoftAuthService(ILauncherLogger logger, string accountCachePath, IAccountStore accountStore, string? clientId = null)
    {
        if (!string.IsNullOrWhiteSpace(clientId)) MinecraftMsalClientId = clientId;
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountCachePath);
        ArgumentNullException.ThrowIfNull(accountStore);

        _logger = logger;
        _accountStore = accountStore;
        Directory.CreateDirectory(Path.GetDirectoryName(accountCachePath)!);

        // Build an MSAL public-client app with its own on-disk token cache. The Build*WithCache
        // helper is async; we block briefly on construction because the cache load is tiny.
        var msalApp = MsalClientHelper.BuildApplicationWithCache(MinecraftMsalClientId)
            .GetAwaiter().GetResult();

        var deviceCodeProvider = new MsalDeviceCodeProvider(msalApp, deviceCode =>
        {
            var info = new MicrosoftDeviceCodeInfo
            {
                UserCode = deviceCode.UserCode ?? string.Empty,
                VerificationUrl = deviceCode.VerificationUrl ?? "https://microsoft.com/devicelogin",
                Message = deviceCode.Message ?? string.Empty,
            };
            _logger.Info("Microsoft device-code verification requested.");

            try
            {
                DeviceCodeRequested?.Invoke(this, info);
            }
            catch (Exception ex)
            {
                _logger.Warn($"DeviceCodeRequested subscriber threw: {ex.Message}");
            }

            return Task.CompletedTask;
        });

        _handler = new JELoginHandlerBuilder()
            .WithAccountManager(accountCachePath)
            .WithOAuthProvider(deviceCodeProvider)
            .Build();
    }

    /// <inheritdoc />
    public bool HasCachedAccount
    {
        get
        {
            try
            {
                return _handler.AccountManager.GetAccounts().Count > 0;
            }
            catch (Exception ex)
            {
                _logger.Warn($"HasCachedAccount probe failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-in: silent-then-device-code (MSAL).");
        try
        {
            var session = await _handler.Authenticate(cancellationToken).ConfigureAwait(false);
            var result = ToAuthResult(session);
            await PersistAfterSignInAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Microsoft sign-in cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            throw TranslateAndLog(ex, interactive: false);
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInSilentlyAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-in: silent-only (active account).");
        try
        {
            var active = await _accountStore.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            MSession session;
            if (active is not null && !active.IsOffline
                && TryFindAccount(active.Id, out var xboxAccount))
            {
                session = await _handler.AuthenticateSilently(xboxAccount!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                session = await _handler.AuthenticateSilently(cancellationToken).ConfigureAwait(false);
            }
            var result = ToAuthResult(session);
            await PersistAfterSignInAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Silent failure is expected on first launch / when refresh-token expired.
            throw new AuthenticationFailedException($"Silent sign-in unavailable: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInInteractiveAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-in: forced device-code (MSAL).");
        try
        {
            var session = await _handler.AuthenticateInteractively(cancellationToken).ConfigureAwait(false);
            var result = ToAuthResult(session);
            await PersistAfterSignInAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Microsoft interactive sign-in cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            throw TranslateAndLog(ex, interactive: true);
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Microsoft sign-out (active account).");
        var active = await _accountStore.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (active is not null && TryFindAccount(active.Id, out var xboxAccount))
                await _handler.Signout(xboxAccount!, cancellationToken).ConfigureAwait(false);
            else
                await _handler.Signout(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Microsoft sign-out failed (cache will be force-cleared): {ex.Message}");
        }

        if (active is not null)
        {
            await _accountStore.RemoveAsync(active.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> ListCachedAsync(CancellationToken cancellationToken)
    {
        var stored = (await _accountStore.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.Id, StringComparer.Ordinal);

        var live = new List<Account>();
        try
        {
            foreach (var raw in _handler.AccountManager.GetAccounts())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hydrated = HydrateFromRaw(raw, stored);
                if (hydrated is not null) live.Add(hydrated);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Warn($"ListCachedAsync: account enumeration failed: {ex.Message}");
        }

        // Merge MSAL-known accounts with anything else the store holds (offline entries,
        // accounts whose token cache was wiped externally) so the UI never loses a saved row.
        var liveIds = new HashSet<string>(live.Select(a => a.Id), StringComparer.Ordinal);
        foreach (var s in stored.Values)
        {
            if (!liveIds.Contains(s.Id)) live.Add(s);
        }
        return live;
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInSilentlyAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        _logger.Info($"Microsoft sign-in: silent-only (account '{accountId}').");
        try
        {
            if (!TryFindAccount(accountId, out var xboxAccount))
                throw new AuthenticationFailedException(
                    $"Cached account '{accountId}' not found in MSAL store; reconnect via Add account.");

            var session = await _handler.AuthenticateSilently(xboxAccount!, cancellationToken).ConfigureAwait(false);
            var result = ToAuthResult(session);
            await PersistAfterSignInAsync(result, cancellationToken).ConfigureAwait(false);
            await _accountStore.SetActiveAsync(result.Username is { Length: > 0 }
                ? UnpackId(session) ?? accountId
                : accountId, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthenticationFailedException) { throw; }
        catch (Exception ex)
        {
            throw new AuthenticationFailedException($"Silent sign-in failed for '{accountId}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        _logger.Info($"Microsoft sign-out (account '{accountId}').");
        if (_handler.AccountManager.GetAccounts().TryGetAccount(accountId, out var xboxAccount))
            await _handler.Signout(xboxAccount!, cancellationToken).ConfigureAwait(false);
        await _accountStore.RemoveAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    private bool TryFindAccount(string id, out IXboxGameAccount? account)
    {
        try
        {
            return _handler.AccountManager.GetAccounts().TryGetAccount(id, out account);
        }
        catch
        {
            account = null;
            return false;
        }
    }

    /// <summary>Map a XboxAuthNet account into our <see cref="Account"/> DTO, preferring stored metadata where available.</summary>
    private static Account? HydrateFromRaw(IXboxGameAccount raw, IReadOnlyDictionary<string, Account> stored)
    {
        var id = raw.Identifier;
        if (string.IsNullOrEmpty(id)) return null;

        // The JEGameAccount projection lets us pull Profile + Gamertag without re-authenticating.
        // It's the same pattern JELoginHandler uses internally when it wraps the raw cache row.
        JEGameAccount? je = null;
        try { je = JEGameAccount.FromSessionStorage(raw.SessionStorage); } catch { /* malformed cache row */ }

        var existing = stored.TryGetValue(id, out var s) ? s : null;
        return new Account
        {
            Id = id,
            Username = je?.Profile?.Username ?? existing?.Username ?? je?.Gamertag ?? "Microsoft account",
            Uuid = je?.Profile?.UUID ?? existing?.Uuid ?? string.Empty,
            SkinHeadUri = existing?.SkinHeadUri,
            LastUsedAt = je is not null && je.LastAccess != default
                ? new DateTimeOffset(DateTime.SpecifyKind(je.LastAccess, DateTimeKind.Utc), TimeSpan.Zero)
                : existing?.LastUsedAt ?? DateTimeOffset.UtcNow,
            IsOffline = false,
        };
    }

    /// <summary>Best-effort: extract the MSAL home-account id we used for this session, or null if we can't.</summary>
    private string? UnpackId(MSession session)
    {
        // MSession itself doesn't carry the MSAL identifier directly; defer to the default account
        // (which JELoginHandler just authenticated). This keeps the store id aligned with the cache.
        try
        {
            var def = _handler.AccountManager.GetDefaultAccount();
            return def?.Identifier;
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistAfterSignInAsync(AuthResult result, CancellationToken cancellationToken)
    {
        try
        {
            // Best-effort: grab the default account from the manager (the one we just signed into)
            // and use its Identifier as the store id. Falls back to UUID if the manager isn't ready.
            var id = TryGetDefaultIdentifier() ?? result.Uuid;
            if (string.IsNullOrEmpty(id)) return;

            var existing = (await _accountStore.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

            var account = new Account
            {
                Id = id,
                Username = string.IsNullOrEmpty(result.Username) ? existing?.Username ?? "Microsoft account" : result.Username,
                Uuid = result.Uuid,
                SkinHeadUri = existing?.SkinHeadUri,
                LastUsedAt = DateTimeOffset.UtcNow,
                IsOffline = result.IsOffline,
            };
            await _accountStore.SaveAsync(account, cancellationToken).ConfigureAwait(false);
            await _accountStore.SetActiveAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to persist account after sign-in: {ex.Message}");
        }
    }

    private string? TryGetDefaultIdentifier()
    {
        try { return _handler.AccountManager.GetDefaultAccount()?.Identifier; }
        catch { return null; }
    }

    private static AuthResult ToAuthResult(MSession session) => new()
    {
        Username = session.Username ?? string.Empty,
        Uuid = session.UUID ?? string.Empty,
        AccessToken = session.AccessToken ?? string.Empty,
        IsOffline = false,
    };

    private AuthenticationFailedException TranslateAndLog(Exception ex, bool interactive)
    {
        var prefix = interactive ? "Microsoft interactive sign-in failed" : "Microsoft sign-in failed";
        var wrapped = new AuthenticationFailedException($"{prefix}: {ex.Message}", ex);
        _logger.Error(prefix, wrapped);
        return wrapped;
    }

    private static string DefaultAccountCachePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperionMinecraftLauncher");
        return Path.Combine(dir, "accounts.json");
    }
}
