using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Native authority is checked under the same gate as provider credential, ownership and session writes.</summary>
public sealed class NativeLoginService(
	INativeLoginSecretStore secrets,
	IAccountSessionStore sessions,
	IRecentAccountProofStore proofs,
	IMediator mediator,
	AccountCredentialGate gate,
	INativeLoginPolicy policy,
	IPasswordService passwords,
	IValidateService validation,
	TimeProvider time,
	IAccountProofAuthorityValidator? authorityValidator = null)
{
	private static bool Dedicated(AccountProofAuthority authority)
		=> authority.Method is "password" or "passkey" or "external";

	public Task<Result<NativeLoginSecretMint>> MintAsync(string accountId, string sessionToken, DBRef character,
		string originIp, string? proofGrant, CancellationToken cancellationToken = default)
		=> gate.RunAsync<Result<NativeLoginSecretMint>>(async () =>
		{
			if (await sessions.InspectAsync(sessionToken, cancellationToken) is not IAccountSessionStore.SessionIdentity session
				|| session.AccountId != accountId) return new Error<string>("A live account session is required.");
			if (await mediator.Send(new GetNativeCharacterAuthorityQuery(accountId, character), cancellationToken) is not NativeCharacterAuthority current)
				return new Error<string>("The active account does not own this exact character.");
			if (!await policy.CanConnectAsync(character, originIp, originIp, cancellationToken))
				return new Error<string>("Character connections are not currently permitted.");
			if (proofs.Consume(proofGrant, accountId, sessionToken, AccountSecurityOperation.NativeLogin) is not AccountProofAuthority proof
				|| !Dedicated(proof) || proof.CredentialEpoch != current.AccountCredentialEpoch
				|| authorityValidator is not null && !await authorityValidator.IsCurrentAsync(accountId, proof, cancellationToken))
				return new Error<string>("Fresh account verification is required.");
			// Policy evaluation may take time; inspect the source again without sliding its lifetime.
			if (await sessions.InspectAsync(sessionToken, cancellationToken) is not IAccountSessionStore.SessionIdentity live
				|| live.AccountId != accountId) return new Error<string>("The source session expired.");
			return secrets.Issue(new(accountId, character, AccountSessionDigest.Compute(sessionToken),
				current.AccountCredentialEpoch, current.PasswordCredentialEpoch), originIp);
		}, cancellationToken);

	private async Task<bool> IsCurrentAsync(NativeLoginSecretLease lease, CancellationToken cancellationToken)
	{
		if (time.GetUtcNow() >= lease.ExpiresAt) return false;
		var authority = lease.Authority;
		if (await mediator.Send(new GetNativeCharacterAuthorityQuery(authority.AccountId, authority.Character), cancellationToken) is not NativeCharacterAuthority current
			|| current.AccountCredentialEpoch != authority.AccountCredentialEpoch || current.PasswordCredentialEpoch != authority.PasswordCredentialEpoch)
			return false;
		return await sessions.InspectDigestAsync(authority.SessionDigest, cancellationToken) is IAccountSessionStore.SessionIdentity session
			&& session.AccountId == authority.AccountId && time.GetUtcNow() < lease.ExpiresAt;
	}

	/// <summary>The bind callback must begin with the local connection bind, with no intervening policy or network work.</summary>
	public Task<Found<T>> RedeemAsync<T>(string secret, string sourceIp, string connectionId,
		Func<DBRef, Task<bool>> authorize, Func<DBRef, Func<Task<bool>>, Task<T>> bind, CancellationToken cancellationToken = default, string? sourceHost = null)
		=> gate.RunAsync<Found<T>>(async () =>
		{
			if (secrets.Consume(secret, sourceIp, connectionId) is not NativeLoginSecretLease lease) return new NotFound();
			try
			{
				if (!await IsCurrentAsync(lease, cancellationToken) || !await authorize(lease.Authority.Character)
					|| !await IsCurrentAsync(lease, cancellationToken))
				{
					secrets.RecordFailure(sourceIp, connectionId);
					return new NotFound();
				}
				var result = await bind(lease.Authority.Character, async () =>
					await policy.CanConnectAsync(lease.Authority.Character, sourceIp, sourceHost ?? sourceIp, cancellationToken)
					&& await IsCurrentAsync(lease, cancellationToken));
				if (result is false) secrets.RecordFailure(sourceIp, connectionId);
				return result;
			}
			catch
			{
				secrets.RecordFailure(sourceIp, connectionId);
				throw;
			}
		}, cancellationToken);

	public Task<Result<Success>> SetCharacterPasswordAsync(string accountId, string sessionToken, DBRef character,
		string newPassword, string confirmation, string? proofGrant, CancellationToken cancellationToken = default)
		=> gate.RunAsync<Result<Success>>(async () =>
		{
			if (newPassword != confirmation || newPassword.Length is 0 or > 1024
				|| !await validation.Valid(IValidateService.ValidationType.Password, MString.Plain(newPassword), new None()))
				return new Error<string>("A valid matching character password and confirmation are required.");
			if (await sessions.InspectAsync(sessionToken, cancellationToken) is not IAccountSessionStore.SessionIdentity session
				|| session.AccountId != accountId) return new Error<string>("A live account session is required.");
			if (await mediator.Send(new GetNativeCharacterAuthorityQuery(accountId, character), cancellationToken) is not NativeCharacterAuthority current)
				return new Error<string>("The active account does not own this exact character.");
			if (proofs.Consume(proofGrant, accountId, sessionToken, AccountSecurityOperation.CharacterPassword) is not AccountProofAuthority proof
				|| !Dedicated(proof) || proof.CredentialEpoch != current.AccountCredentialEpoch
				|| authorityValidator is not null && !await authorityValidator.IsCurrentAsync(accountId, proof, cancellationToken))
				return new Error<string>("Fresh account verification is required.");
			var hash = passwords.HashPassword(newPassword);
			if (await sessions.InspectAsync(sessionToken, cancellationToken) is not IAccountSessionStore.SessionIdentity live
				|| live.AccountId != accountId) return new Error<string>("The source session expired.");
			var result = await mediator.Send(new SetOwnedPlayerPasswordCommand(accountId, character, hash), cancellationToken);
			if (result is Success) secrets.RevokeCharacter(character);
			return result;
		}, cancellationToken);
}
