using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>Recent explicit-account authority, distinct from legacy character-password account login.</summary>
public sealed class AccountRecentProofService(IAccountService accounts, IAccountSessionStore sessions,
	IPasswordService passwords, IRecentAccountProofStore proofs, AccountCredentialGate gate, IAccountProofAuthorityValidator? authorityValidator = null)
{
	private const string Refused = "A fresh proof of an account sign-in method is required.";

	public Task<Result<RecentAccountProof>> PasswordAsync(string accountId, string sessionToken, string password,
		AccountSecurityOperation operation, CancellationToken ct = default)
		=> gate.RunAsync<Result<RecentAccountProof>>(async () => await FreshAccountAsync(accountId, sessionToken, operation, ct) switch
		{
			SharpAccount account when !string.IsNullOrEmpty(account.PasswordHash) && passwords.PasswordIsValid(password, account.PasswordHash)
				=> proofs.Issue(accountId, sessionToken, operation, new AccountProofAuthority("password", CredentialEpoch: account.CredentialEpoch)),
			_ => new Error<string>(Refused)
		}, ct);

	/// <summary>Called only after an actual passkey or remote authentication ceremony. The callback checks
	/// the proved method is still attached using local storage; it must not contact a provider.</summary>
	public Task<Result<RecentAccountProof>> VerifiedAsync(string accountId, string sessionToken,
		AccountSecurityOperation operation, string method, string identityId, Func<CancellationToken, Task<bool>> stillAttached,
		CancellationToken ct = default, string? providerGeneration = null)
		=> gate.RunAsync<Result<RecentAccountProof>>(async () => await FreshAccountAsync(accountId, sessionToken, operation, ct) switch
		{
			SharpAccount account when method is "passkey" or "external" && (method != "external" || providerGeneration is not null) && await stillAttached(ct)
				=> proofs.Issue(accountId, sessionToken, operation, new AccountProofAuthority(method, identityId, account.CredentialEpoch, providerGeneration)),
			_ => new Error<string>(Refused)
		}, ct);

	/// <summary>Checks current source authority, consumes the grant once and performs its local mutation in
	/// one credential decision. The callback must not perform remote-provider HTTP requests.</summary>
	public Task<Result<T>> WithProofAsync<T>(string accountId, string sessionToken, AccountSecurityOperation operation,
		string? grant, Func<SharpAccount, AccountProofAuthority, Task<Result<T>>> action, CancellationToken ct = default)
		=> gate.RunAsync<Result<T>>(async () => await FreshAccountAsync(accountId, sessionToken, operation, ct) switch
		{
			SharpAccount account when proofs.Consume(grant, accountId, sessionToken, operation) is AccountProofAuthority authority
				&& authority.CredentialEpoch == account.CredentialEpoch
								&& (authorityValidator is null || await authorityValidator.IsCurrentAsync(accountId, authority, ct)) => await action(account, authority),
			_ => new Error<string>(Refused)
		}, ct);

	private async Task<Found<SharpAccount>> FreshAccountAsync(string accountId, string sessionToken,
		AccountSecurityOperation operation, CancellationToken ct)
	{
		if (!Enum.IsDefined(operation) || await sessions.InspectAsync(sessionToken, ct) is not IAccountSessionStore.SessionIdentity session
			|| session.AccountId != accountId)
			return new NotFound();
		return await accounts.GetByIdAsync(accountId, ct) switch
		{
			{ IsActive: true } account when !account.MustChangePassword || operation == AccountSecurityOperation.AccountPassword => account,
			_ => new NotFound()
		};
	}
}
