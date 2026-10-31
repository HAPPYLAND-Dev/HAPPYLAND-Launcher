using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

/// <summary>
/// JSON-backed <see cref="IAccountStore"/>. The v2 cache lives at
/// <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\accounts.v2.json</c> as a single
/// document holding the active id + the account array. Writes are atomic
/// (write-temp + rename). <c>accounts.json</c> next to it is XboxAuthNet's live
/// account-manager cache (rewritten on every sign-in), which this store never reads:
/// <c>MicrosoftAuthService</c> hydrates the real accounts from it directly.
/// </summary>
public sealed class FileAccountStore : IAccountStore
{
    /// <summary>
    /// Id of the phantom row that v0.27.0-v0.32.23 synthesised from <c>accounts.json</c>.
    /// It ended up persisted next to the real account on first sign-in, so reads prune it.
    /// </summary>
    private const string StalePlaceholderId = "legacy-msal";

    private readonly string _v2Path;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Use the platform-default paths.</summary>
    public FileAccountStore() : this(DefaultV2Path(), DefaultV1Path()) { }

    /// <summary>Use explicit paths (tests / advanced wiring). <paramref name="v1Path"/> is kept for API compatibility and ignored.</summary>
    public FileAccountStore(string v2Path, string v1Path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(v2Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(v1Path);
        _v2Path = v2Path;
        var dir = Path.GetDirectoryName(v2Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken)
    {
        var doc = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return doc.Accounts;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Account account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Id);

        var doc = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        var without = doc.Accounts.Where(a => !string.Equals(a.Id, account.Id, StringComparison.Ordinal));
        var next = without.Append(account).ToList();
        var updated = new AccountDocument { ActiveId = doc.ActiveId, Accounts = next };
        await WriteDocumentAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var doc = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        var pruned = doc.Accounts.Where(a => !string.Equals(a.Id, id, StringComparison.Ordinal)).ToList();
        var activeId = string.Equals(doc.ActiveId, id, StringComparison.Ordinal) ? null : doc.ActiveId;
        var updated = new AccountDocument { ActiveId = activeId, Accounts = pruned };
        await WriteDocumentAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Account?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var doc = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(doc.ActiveId)) return null;
        return doc.Accounts.FirstOrDefault(a => string.Equals(a.Id, doc.ActiveId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(string? id, CancellationToken cancellationToken)
    {
        var doc = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        var updated = new AccountDocument { ActiveId = id, Accounts = doc.Accounts };
        await WriteDocumentAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the v2 document. Missing or malformed v2 JSON returns an empty document - never
    /// crashes startup. The stale <see cref="StalePlaceholderId"/> row is dropped on the way out.
    /// </summary>
    private async Task<AccountDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_v2Path))
        {
            try
            {
                await using var stream = File.OpenRead(_v2Path);
                var dto = await JsonSerializer
                    .DeserializeAsync<AccountDocumentDto>(stream, ReadOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (dto is not null)
                    return new AccountDocument
                    {
                        ActiveId = string.Equals(dto.ActiveId, StalePlaceholderId, StringComparison.Ordinal)
                            ? null
                            : dto.ActiveId,
                        Accounts = (dto.Accounts ?? new List<Account>())
                            .Where(a => !string.Equals(a.Id, StalePlaceholderId, StringComparison.Ordinal))
                            .ToList(),
                    };
            }
            catch
            {
                // Bad JSON - fall through to empty document. We don't auto-delete the file:
                // future versions might want to attempt recovery.
            }
        }

        return AccountDocument.Empty;
    }

    private async Task WriteDocumentAsync(AccountDocument doc, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_v2Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _v2Path + ".tmp";
        var dto = new AccountDocumentDto
        {
            ActiveId = doc.ActiveId,
            Accounts = doc.Accounts.ToList(),
        };
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, dto, WriteOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tmp, _v2Path, overwrite: true);
    }

    private static string DefaultV2Path() => Path.Combine(DefaultDir(), "accounts.v2.json");
    private static string DefaultV1Path() => Path.Combine(DefaultDir(), "accounts.json");
    // Accounts are configuration - XDG_CONFIG_HOME on Linux, %LOCALAPPDATA% on Windows.
    private static string DefaultDir() => XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Config);

    private sealed record AccountDocument
    {
        public string? ActiveId { get; init; }
        public IReadOnlyList<Account> Accounts { get; init; } = Array.Empty<Account>();

        public static AccountDocument Empty { get; } = new()
        {
            ActiveId = null,
            Accounts = Array.Empty<Account>(),
        };
    }

    private sealed class AccountDocumentDto
    {
        [JsonPropertyName("activeId")]
        public string? ActiveId { get; set; }

        [JsonPropertyName("accounts")]
        public List<Account>? Accounts { get; set; }
    }
}
