using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private static void AdvanceAccountCredentialEpoch(ITx tx, string accountKey)
	{
		if (!tx.TryGet(Tables.Account, Keys.Str(accountKey), out var bytes)) return;
		var record = Codec.Deserialize<AccountRecord>(bytes);
		tx.Put(Tables.Account, Keys.Str(accountKey), Codec.Serialize(record with
		{
			CredentialEpoch = checked(record.CredentialEpoch + 1),
			UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	// Caller supplies the effective enabled registration set. Legacy character fallback never counts as dedicated proof.
	private static bool HasSurvivingAccountMethod(ITx tx, string accountKey, IReadOnlyCollection<string> enabledRegistrations,
		bool excludePassword = false, byte[]? excludedPasskey = null, byte[]? excludedIdentity = null)
	{
		var account = ReadAccountByKey(tx, accountKey);
		if (account is not { IsActive: true }) return false;
		if (!excludePassword && !string.IsNullOrEmpty(account.PasswordHash)) return true;
		if (tx.Dups(Tables.AccountPasskeyByAccount, Keys.Str(accountKey))
			.Any(key => (excludedPasskey is null || !key.SequenceEqual(excludedPasskey)) && ReadOwnPasskey(tx, accountKey, key) is not null)) return true;
		return ReadExternalIdentities(tx, accountKey).Any(row => (excludedIdentity is null || !row.Digest.SequenceEqual(excludedIdentity))
			&& enabledRegistrations.Contains(row.Record.Registration, StringComparer.Ordinal)
			&& row.Digest.SequenceEqual(ExternalIdentityDigest(new(row.Record.Registration, row.Record.Issuer, row.Record.Subject))));
	}

	public async ValueTask UpdateAccountCharacterPasswordLoginAsync(string accountId, bool enabled, CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		await WriteCredentialsAsync(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(accountKey), out var bytes)) return;
			var record = Codec.Deserialize<AccountRecord>(bytes);
			if (record.AllowCharacterPasswordAccountLogin == enabled) return;
			tx.Put(Tables.Account, Keys.Str(accountKey), Codec.Serialize(record with
			{
				AllowCharacterPasswordAccountLogin = enabled,
				CredentialEpoch = checked(record.CredentialEpoch + 1),
				UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}));
		}, cancellationToken, accountId: accountId);
	}

	public async ValueTask<Result<Success>> TryRemoveAccountPasswordAsync(string accountId, IReadOnlyCollection<string> enabledRegistrations,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		return await WriteCredentialsAsync<Result<Success>>(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(accountKey), out var bytes)) return new Error<string>("Account not found.");
			var record = Codec.Deserialize<AccountRecord>(bytes);
			if (string.IsNullOrEmpty(record.PasswordHash)) return new Error<string>("Account has no local password.");
			if (!HasSurvivingAccountMethod(tx, accountKey, enabledRegistrations, excludePassword: true))
				return new Error<string>("The final usable account sign-in method cannot be removed.");
			tx.Put(Tables.Account, Keys.Str(accountKey), Codec.Serialize(record with
			{
				PasswordHash = null,
				CredentialEpoch = checked(record.CredentialEpoch + 1),
				UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}));
			return new Success();
		}, cancellationToken, accountId: accountId);
	}

	public async ValueTask<Result<Success>> TryRemoveAccountPasskeyAsync(string accountId, byte[] credentialId,
		IReadOnlyCollection<string> enabledRegistrations, CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		var key = PasskeyKey(credentialId);
		return await WriteCredentialsAsync<Result<Success>>(tx =>
		{
			if (ReadOwnPasskey(tx, accountKey, key) is not { } record || !record.CredentialId.SequenceEqual(credentialId))
				return new Error<string>("Passkey not found.");
			if (!HasSurvivingAccountMethod(tx, accountKey, enabledRegistrations, excludedPasskey: key))
				return new Error<string>("The final usable account sign-in method cannot be removed.");
			tx.Delete(Tables.AccountPasskey, key);
			tx.Delete(Tables.AccountPasskeyByAccount, Keys.Str(accountKey), key);
			AdvanceAccountCredentialEpoch(tx, accountKey);
			return new Success();
		}, cancellationToken, accountId: accountId);
	}
}
