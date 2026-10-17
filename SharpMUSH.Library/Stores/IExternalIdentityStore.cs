using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>External account credentials. Each mutation performs its checks and writes in one transaction.</summary>
public interface IExternalIdentityStore
{
	ValueTask<Found<ExternalIdentity>> GetExternalIdentityAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default);
	ValueTask<IReadOnlyList<ExternalIdentity>> GetExternalIdentitiesAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>Atomically reserves the username and identity for a passwordless account. Never merges email or reuses an existing identity.</summary>
	ValueTask<Result<SharpAccount>> ProvisionExternalAccountAsync(ExternalIdentityKey key, string username, string? label,
		CancellationToken cancellationToken = default);

	/// <summary>Attaches an unclaimed identity to an active account. Existing ownership is immutable.</summary>
	ValueTask<Result<ExternalIdentity>> AttachExternalIdentityAsync(string accountId, ExternalIdentityKey key, string? label,
		CancellationToken cancellationToken = default);

	ValueTask<bool> RecordExternalIdentityUseAsync(string accountId, ExternalIdentityKey key, DateTimeOffset usedAt,
		CancellationToken cancellationToken = default);

	/// <summary>Removes an owned identity only when a local password, passkey or other enabled identity survives.</summary>
	ValueTask<Result<Success>> RemoveExternalIdentityAsync(string accountId, ExternalIdentityKey key,
		IReadOnlyCollection<string> enabledRegistrations, CancellationToken cancellationToken = default);
}
