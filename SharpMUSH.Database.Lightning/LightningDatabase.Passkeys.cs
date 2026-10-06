using System.Security.Cryptography;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountStore"/>'s passkeys. A passkey row is keyed by the SHA-256 of its credential id, which
/// is what sign-in looks it up by; <see cref="Tables.AccountPasskeyByAccount"/> lists an account's.
/// </summary>
public partial class LightningDatabase
{
	private static byte[] PasskeyKey(byte[] credentialId) => SHA256.HashData(credentialId);

	private static AccountPasskey MapToPasskey(AccountPasskeyRecord record) => new(
		$"node_accounts/{record.AccountKey}",
		record.CredentialId,
		record.PublicKey,
		record.SignCount,
		record.Name,
		record.Transports,
		record.IsBackedUp,
		DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtMs),
		record.LastUsedAtMs is { } used ? DateTimeOffset.FromUnixTimeMilliseconds(used) : null);

	/// <summary>The account's passkey under <paramref name="passkeyKey"/>, or null when it is another account's or gone.</summary>
	private static AccountPasskeyRecord? ReadOwnPasskey(ITx tx, string accountKey, byte[] passkeyKey)
		=> tx.TryGet(Tables.AccountPasskey, passkeyKey, out var bytes)
			&& Codec.Deserialize<AccountPasskeyRecord>(bytes) is { } record
			&& record.AccountKey == accountKey
				? record
				: null;

	public async ValueTask<PasskeyAddOutcome> AddAccountPasskeyAsync(AccountPasskey passkey, int maxPerAccount,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(passkey.AccountId);
		var key = PasskeyKey(passkey.CredentialId);
		var record = new AccountPasskeyRecord
		{
			AccountKey = accountKey,
			CredentialId = passkey.CredentialId,
			PublicKey = passkey.PublicKey,
			SignCount = passkey.SignCount,
			Name = passkey.Name,
			Transports = [.. passkey.Transports],
			IsBackedUp = passkey.IsBackedUp,
			CreatedAtMs = passkey.CreatedAt.ToUnixTimeMilliseconds(),
			LastUsedAtMs = passkey.LastUsedAt?.ToUnixTimeMilliseconds()
		};

		return await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.AccountPasskey, key, out _)) return PasskeyAddOutcome.AlreadyRegistered;
			if (tx.Dups(Tables.AccountPasskeyByAccount, Keys.Str(accountKey)).Count() >= maxPerAccount)
				return PasskeyAddOutcome.AccountFull;
			tx.Put(Tables.AccountPasskey, key, Codec.Serialize(record));
			tx.Put(Tables.AccountPasskeyByAccount, Keys.Str(accountKey), key);
			return PasskeyAddOutcome.Added;
		}, cancellationToken);
	}

	public ValueTask<AccountPasskey?> GetAccountPasskeyAsync(byte[] credentialId, CancellationToken cancellationToken = default)
	{
		var key = PasskeyKey(credentialId);
		return ValueTask.FromResult(Store.Read(tx => tx.TryGet(Tables.AccountPasskey, key, out var bytes)
			? MapToPasskey(Codec.Deserialize<AccountPasskeyRecord>(bytes))
			: null));
	}

	public ValueTask<IReadOnlyList<AccountPasskey>> GetAccountPasskeysAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		var result = Store.Read(tx => tx.Dups(Tables.AccountPasskeyByAccount, Keys.Str(accountKey))
			.Select(key => ReadOwnPasskey(tx, accountKey, key))
			.OfType<AccountPasskeyRecord>()
			.OrderBy(record => record.CreatedAtMs)
			.Select(MapToPasskey)
			.ToList());
		return ValueTask.FromResult<IReadOnlyList<AccountPasskey>>(result);
	}

	public async ValueTask<bool> RecordAccountPasskeyUseAsync(byte[] credentialId, uint signCount, bool isBackedUp,
		DateTimeOffset usedAt, CancellationToken cancellationToken = default)
	{
		var key = PasskeyKey(credentialId);
		return await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.AccountPasskey, key, out var bytes)) return false;
			var record = Codec.Deserialize<AccountPasskeyRecord>(bytes);
			if ((signCount != 0 || record.SignCount != 0) && signCount <= record.SignCount) return false;
			tx.Put(Tables.AccountPasskey, key, Codec.Serialize(record with
			{
				SignCount = signCount,
				IsBackedUp = isBackedUp,
				LastUsedAtMs = usedAt.ToUnixTimeMilliseconds()
			}));
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> RenameAccountPasskeyAsync(string accountId, byte[] credentialId, string name,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		var key = PasskeyKey(credentialId);
		return await Store.WriteAsync(tx =>
		{
			if (ReadOwnPasskey(tx, accountKey, key) is not { } record) return false;
			tx.Put(Tables.AccountPasskey, key, Codec.Serialize(record with { Name = name }));
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> RemoveAccountPasskeyAsync(string accountId, byte[] credentialId, CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		var key = PasskeyKey(credentialId);
		return await Store.WriteAsync(tx =>
		{
			if (ReadOwnPasskey(tx, accountKey, key) is null) return false;
			tx.Delete(Tables.AccountPasskey, key);
			tx.Delete(Tables.AccountPasskeyByAccount, Keys.Str(accountKey), key);
			return true;
		}, cancellationToken);
	}
}
