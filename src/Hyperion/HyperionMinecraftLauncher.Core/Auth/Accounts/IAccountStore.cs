using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

/// <summary>
/// Persistent roster of cached <see cref="Account"/> entries plus a pointer to the
/// currently active one. Pure storage contract - the concrete file-backed
/// implementation lives next door in <see cref="FileAccountStore"/>.
/// </summary>
public interface IAccountStore
{
    /// <summary>Return every cached account. Order is unspecified - callers sort if needed.</summary>
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Add or replace (by <see cref="Account.Id"/>) one account.</summary>
    Task SaveAsync(Account account, CancellationToken cancellationToken);

    /// <summary>Remove the account with the given id. No-op when absent. Clears the active id if it matched.</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken);

    /// <summary>Return the currently active account, or <c>null</c> if no active id is set / the active id no longer exists.</summary>
    Task<Account?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Set the active id (pass <c>null</c> to clear). Caller is responsible for ensuring the id matches a stored account.</summary>
    Task SetActiveAsync(string? id, CancellationToken cancellationToken);
}
