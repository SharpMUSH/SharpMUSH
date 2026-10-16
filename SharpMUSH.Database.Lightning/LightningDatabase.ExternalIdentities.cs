using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	// LMDB has bounded keys; hash the entire tuple with unambiguous UTF-8 byte lengths, never truncation.
	private static byte[] ExternalIdentityDigest(ExternalIdentityKey key)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> length = stackalloc byte[4];
		foreach (var component in new[] { key.Registration, key.Issuer, key.Subject })
		{
			var bytes = Encoding.UTF8.GetBytes(component);
			BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
			hash.AppendData(length);
			hash.AppendData(bytes);
		}
		return hash.GetHashAndReset();
	}

	private static bool ValidExternalIdentity(ExternalIdentityKey key, string? label = null)
		=> !string.IsNullOrWhiteSpace(key.Registration) && key.Registration.Length <= 256
			&& !string.IsNullOrWhiteSpace(key.Issuer) && key.Issuer.Length <= 2048
			&& !string.IsNullOrWhiteSpace(key.Subject) && key.Subject.Length <= 4096
			&& (label is null || label.Length <= 512);

	private static bool MatchesIdentity(ExternalIdentityRecord record, ExternalIdentityKey key)
		=> record.Registration == key.Registration && record.Issuer == key.Issuer && record.Subject == key.Subject;

	private static ExternalIdentity MapExternalIdentity(byte[] digest, ExternalIdentityRecord record) => new(
		Convert.ToHexString(digest), $"node_accounts/{record.AccountKey}",
		new(record.Registration, record.Issuer, record.Subject), record.Label,
		DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtMs),
		record.LastUsedAtMs is { } last ? DateTimeOffset.FromUnixTimeMilliseconds(last) : null);

	private static IEnumerable<(byte[] Digest, ExternalIdentityRecord Record)> ReadExternalIdentities(ITx tx, string accountKey)
	{
		foreach (var digest in tx.Dups(Tables.ExternalIdentityByAccount, Keys.Str(accountKey)))
		{
			if (!tx.TryGet(Tables.ExternalIdentity, digest, out var bytes)) continue;
			var record = Codec.Deserialize<ExternalIdentityRecord>(bytes);
			if (record.AccountKey == accountKey && digest.SequenceEqual(ExternalIdentityDigest(new(record.Registration, record.Issuer, record.Subject))))
				yield return (digest, record);
		}
	}

	public ValueTask<Found<ExternalIdentity>> GetExternalIdentityAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default)
	{
		if (!ValidExternalIdentity(key)) return ValueTask.FromResult<Found<ExternalIdentity>>(new NotFound());
		var digest = ExternalIdentityDigest(key);
		return ValueTask.FromResult(Store.Read<Found<ExternalIdentity>>(tx =>
			tx.TryGet(Tables.ExternalIdentity, digest, out var bytes)
			&& Codec.Deserialize<ExternalIdentityRecord>(bytes) is { } record && MatchesIdentity(record, key)
				? MapExternalIdentity(digest, record) : new NotFound()));
	}

	public ValueTask<IReadOnlyList<ExternalIdentity>> GetExternalIdentitiesAsync(string accountId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<ExternalIdentity>>(Store.Read(tx => ReadExternalIdentities(tx, ParseAccountId(accountId))
			.Select(row => MapExternalIdentity(row.Digest, row.Record)).OrderBy(row => row.CreatedAt).ToArray()));

	private static ExternalIdentity WriteExternalIdentity(ITx tx, string accountKey, byte[] digest, ExternalIdentityKey key, string? label)
	{
		var record = new ExternalIdentityRecord
		{
			AccountKey = accountKey, Registration = key.Registration, Issuer = key.Issuer,
			Subject = key.Subject, Label = label, CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		};
		tx.Put(Tables.ExternalIdentity, digest, Codec.Serialize(record));
		tx.Put(Tables.ExternalIdentityByAccount, Keys.Str(accountKey), digest);
		AdvanceAccountCredentialEpoch(tx, accountKey);
		return MapExternalIdentity(digest, record);
	}

	public async ValueTask<Result<SharpAccount>> ProvisionExternalAccountAsync(ExternalIdentityKey key, string username, string? label,
		CancellationToken cancellationToken = default)
	{
		if (!ValidExternalIdentity(key, label) || string.IsNullOrWhiteSpace(username) || username.Length > 128
			|| SystemAccount.IsReserved(username)) return new Error<string>("Invalid account or external identity.");
		var digest = ExternalIdentityDigest(key);
		return await Store.WriteAsync<Result<SharpAccount>>(tx =>
		{
			// Any stored tuple (including a digest collision or unavailable holder) reserves this key.
			if (tx.TryGet(Tables.ExternalIdentity, digest, out _)) return new Error<string>("External identity is already registered.");
			if (tx.TryGet(Tables.AccountUser, Keys.Lower(username), out _)) return new Error<string>("Username is already taken.");
			var accountKey = AllocateAccountId(tx);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var account = new AccountRecord
			{
				Username = username, PasswordHash = null, AllowCharacterPasswordAccountLogin = false,
				CreatedAt = now, UpdatedAt = now
			};
			tx.Put(Tables.Account, Keys.Str(accountKey), Codec.Serialize(account));
			tx.Put(Tables.AccountUser, Keys.Lower(username), Keys.Str(accountKey));
			WriteExternalIdentity(tx, accountKey, digest, key, label);
			return ReadAccountByKey(tx, accountKey)!;
		}, cancellationToken);
	}

	public async ValueTask<Result<ExternalIdentity>> AttachExternalIdentityAsync(string accountId, ExternalIdentityKey key, string? label,
		CancellationToken cancellationToken = default)
	{
		if (!ValidExternalIdentity(key, label)) return new Error<string>("Invalid external identity.");
		var digest = ExternalIdentityDigest(key);
		var accountKey = ParseAccountId(accountId);
		return await Store.WriteAsync<Result<ExternalIdentity>>(tx =>
		{
			if (ReadAccountByKey(tx, accountKey) is not { IsActive: true }) return new Error<string>("Account is unavailable.");
			if (tx.TryGet(Tables.ExternalIdentity, digest, out var bytes))
			{
				var existing = Codec.Deserialize<ExternalIdentityRecord>(bytes);
				return existing.AccountKey == accountKey && MatchesIdentity(existing, key)
					? MapExternalIdentity(digest, existing)
					: new Error<string>("External identity is already registered.");
			}
			return WriteExternalIdentity(tx, accountKey, digest, key, label);
		}, cancellationToken);
	}

	public async ValueTask<bool> RecordExternalIdentityUseAsync(string accountId, ExternalIdentityKey key, DateTimeOffset usedAt,
		CancellationToken cancellationToken = default)
	{
		if (!ValidExternalIdentity(key)) return false;
		var digest = ExternalIdentityDigest(key);
		var accountKey = ParseAccountId(accountId);
		return await Store.WriteAsync(tx =>
		{
			if (ReadAccountByKey(tx, accountKey) is not { IsActive: true }
				|| !tx.TryGet(Tables.ExternalIdentity, digest, out var bytes)) return false;
			var record = Codec.Deserialize<ExternalIdentityRecord>(bytes);
			if (record.AccountKey != accountKey || !MatchesIdentity(record, key)) return false;
			tx.Put(Tables.ExternalIdentity, digest, Codec.Serialize(record with
			{
				LastUsedAtMs = Math.Max(record.LastUsedAtMs ?? long.MinValue, usedAt.ToUnixTimeMilliseconds())
			}));
			return true;
		}, cancellationToken);
	}

	public async ValueTask<Result<Success>> RemoveExternalIdentityAsync(string accountId, ExternalIdentityKey key,
		IReadOnlyCollection<string> enabledRegistrations, CancellationToken cancellationToken = default)
	{
		if (!ValidExternalIdentity(key)) return new Error<string>("Invalid external identity.");
		var accountKey = ParseAccountId(accountId);
		var digest = ExternalIdentityDigest(key);
		return await Store.WriteAsync<Result<Success>>(tx =>
		{
			if (!tx.TryGet(Tables.ExternalIdentity, digest, out var bytes)) return new Error<string>("Identity not found.");
			var record = Codec.Deserialize<ExternalIdentityRecord>(bytes);
			if (record.AccountKey != accountKey || !MatchesIdentity(record, key)) return new Error<string>("Identity not found.");
			if (!HasSurvivingAccountMethod(tx, accountKey, enabledRegistrations, excludedIdentity: digest))
				return new Error<string>("The final usable account sign-in method cannot be removed.");
			tx.Delete(Tables.ExternalIdentity, digest);
			tx.Delete(Tables.ExternalIdentityByAccount, Keys.Str(accountKey), digest);
			AdvanceAccountCredentialEpoch(tx, accountKey);
			return new Success();
		}, cancellationToken);
	}
}
