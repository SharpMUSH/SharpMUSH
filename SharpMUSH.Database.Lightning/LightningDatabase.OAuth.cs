using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IOAuthStore"/>. Grants are listed by account, character and app in
/// <see cref="Tables.OAuthGrantByAccount"/>, <see cref="Tables.OAuthGrantByCharacter"/> and
/// <see cref="Tables.OAuthGrantByClient"/>; tokens by grant in <see cref="Tables.OAuthTokenByGrant"/> and by expiry in
/// <see cref="Tables.OAuthTokenExpiry"/>. Every index moves in the same write job as the row it points at, and a grant
/// takes its tokens with it.
/// </summary>
public partial class LightningDatabase : IOAuthStore
{
	private static byte[] CharacterKeyOf(DBRef character) => Keys.Str($"#{character.Number}:{character.CreationMilliseconds ?? 0}");

	private static byte[] OAuthTokenExpiryKey(long expiresAtMs, byte[] hash) => Keys.Concat(Keys.Dbref(expiresAtMs), hash);

	private static OAuthClient MapToOAuthClient(string id, OAuthClientRecord record) => new(
		id, record.Name, record.Description, record.Homepage, record.RedirectUris, record.Scopes, record.SecretHash,
		DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtMs));

	private static OAuthGrant MapToOAuthGrant(string id, OAuthGrantRecord record) => new(
		id,
		$"node_accounts/{record.AccountKey}",
		new DBRef(record.CharacterKey, record.CharacterCreationTime),
		record.ClientId,
		record.ClientName,
		record.Scopes,
		DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtMs),
		record.LastUsedAtMs is { } used ? DateTimeOffset.FromUnixTimeMilliseconds(used) : null);

	private static OAuthToken MapToOAuthToken(byte[] hash, OAuthTokenRecord record) => new(
		hash,
		(OAuthTokenKind)record.Kind,
		record.GrantId,
		record.ClientId,
		$"node_accounts/{record.AccountKey}",
		new DBRef(record.CharacterKey, record.CharacterCreationTime),
		record.Scopes,
		DateTimeOffset.FromUnixTimeMilliseconds(record.ExpiresAtMs),
		record.RedirectUri,
		record.CodeChallenge);

	private static OAuthTokenRecord ToRecord(OAuthToken token) => new()
	{
		Kind = (int)token.Kind,
		GrantId = token.GrantId,
		ClientId = token.ClientId,
		AccountKey = ParseAccountId(token.AccountId),
		CharacterKey = token.Character.Number,
		CharacterCreationTime = token.Character.CreationMilliseconds ?? 0,
		Scopes = [.. token.Scopes],
		ExpiresAtMs = token.ExpiresAt.ToUnixTimeMilliseconds(),
		RedirectUri = token.RedirectUri,
		CodeChallenge = token.CodeChallenge
	};

	private static OAuthGrantRecord? ReadGrant(ITx tx, string id)
		=> tx.TryGet(Tables.OAuthGrant, Keys.Str(id), out var bytes) ? Codec.Deserialize<OAuthGrantRecord>(bytes) : null;

	/// <summary>Removes a token row and its index entries, inside the caller's write job.</summary>
	private static void DeleteOAuthTokenRow(ITx tx, byte[] hash, OAuthTokenRecord record)
	{
		tx.Delete(Tables.OAuthToken, hash);
		tx.Delete(Tables.OAuthTokenByGrant, Keys.Str(record.GrantId), hash);
		tx.Delete(Tables.OAuthTokenExpiry, OAuthTokenExpiryKey(record.ExpiresAtMs, hash));
	}

	/// <summary>Stores a token row and its index entries, inside the caller's write job.</summary>
	private static void PutOAuthTokenRow(ITx tx, OAuthToken token)
	{
		var record = ToRecord(token);
		tx.Put(Tables.OAuthToken, token.Hash, Codec.Serialize(record));
		tx.Put(Tables.OAuthTokenByGrant, Keys.Str(record.GrantId), token.Hash);
		tx.Put(Tables.OAuthTokenExpiry, OAuthTokenExpiryKey(record.ExpiresAtMs, token.Hash), []);
	}

	/// <summary>Removes a grant, its index entries and every token issued under it, inside the caller's write job.</summary>
	private static void DeleteOAuthGrantRow(ITx tx, string id, OAuthGrantRecord record)
	{
		var key = Keys.Str(id);
		foreach (var hash in tx.Dups(Tables.OAuthTokenByGrant, key).ToList())
		{
			if (tx.TryGet(Tables.OAuthToken, hash, out var bytes))
				DeleteOAuthTokenRow(tx, hash, Codec.Deserialize<OAuthTokenRecord>(bytes));
			tx.Delete(Tables.OAuthTokenByGrant, key, hash);
		}

		tx.Delete(Tables.OAuthGrant, key);
		tx.Delete(Tables.OAuthGrantByAccount, Keys.Str(record.AccountKey), key);
		tx.Delete(Tables.OAuthGrantByCharacter, CharacterKeyOf(new DBRef(record.CharacterKey, record.CharacterCreationTime)), key);
		tx.Delete(Tables.OAuthGrantByClient, Keys.Str(record.ClientId), key);
	}

	public async ValueTask SaveOAuthClientAsync(OAuthClient client, CancellationToken cancellationToken = default)
	{
		var record = new OAuthClientRecord
		{
			Name = client.Name,
			Description = client.Description,
			Homepage = client.Homepage,
			RedirectUris = [.. client.RedirectUris],
			Scopes = [.. client.Scopes],
			SecretHash = client.SecretHash,
			CreatedAtMs = client.CreatedAt.ToUnixTimeMilliseconds()
		};
		await Store.WriteAsync(tx => tx.Put(Tables.OAuthClient, Keys.Str(client.Id), Codec.Serialize(record)), cancellationToken);
	}

	public ValueTask<Found<OAuthClient>> GetOAuthClientAsync(string id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<OAuthClient>>(tx => tx.TryGet(Tables.OAuthClient, Keys.Str(id), out var bytes)
			? MapToOAuthClient(id, Codec.Deserialize<OAuthClientRecord>(bytes))
			: new NotFound()));

	public ValueTask<IReadOnlyList<OAuthClient>> GetOAuthClientsAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<OAuthClient>>(Store.Read(tx => tx.Range(Tables.OAuthClient, [])
			.Select(entry => MapToOAuthClient(Keys.ReadStr(entry.Key), Codec.Deserialize<OAuthClientRecord>(entry.Value)))
			.OrderBy(client => client.CreatedAt)
			.ToList()));

	public async ValueTask<bool> DeleteOAuthClientAsync(string id, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(id);
		return await Store.WriteAsync(tx =>
		{
			if (!tx.Delete(Tables.OAuthClient, key)) return false;
			foreach (var grantKey in tx.Dups(Tables.OAuthGrantByClient, key).ToList())
			{
				var grantId = Keys.ReadStr(grantKey);
				if (ReadGrant(tx, grantId) is { } grant) DeleteOAuthGrantRow(tx, grantId, grant);
				tx.Delete(Tables.OAuthGrantByClient, key, grantKey);
			}

			return true;
		}, cancellationToken);
	}

	public async ValueTask<OAuthGrant> SaveOAuthGrantAsync(OAuthGrant grant, CancellationToken cancellationToken = default)
	{
		var characterKey = CharacterKeyOf(grant.Character);
		return await Store.WriteAsync(tx =>
		{
			// One grant per character and app: asking again updates the one already there.
			var existing = tx.Dups(Tables.OAuthGrantByCharacter, characterKey)
				.Select(key => Keys.ReadStr(key))
				.Select(id => (Id: id, Record: ReadGrant(tx, id)))
				.FirstOrDefault(pair => pair.Record?.ClientId == grant.ClientId);

			var id = existing.Record is null ? grant.Id : existing.Id;
			var record = new OAuthGrantRecord
			{
				AccountKey = ParseAccountId(grant.AccountId),
				CharacterKey = grant.Character.Number,
				CharacterCreationTime = grant.Character.CreationMilliseconds ?? 0,
				ClientId = grant.ClientId,
				ClientName = grant.ClientName,
				Scopes = [.. grant.Scopes],
				CreatedAtMs = existing.Record?.CreatedAtMs ?? grant.CreatedAt.ToUnixTimeMilliseconds(),
				LastUsedAtMs = existing.Record?.LastUsedAtMs ?? grant.LastUsedAt?.ToUnixTimeMilliseconds()
			};

			var key = Keys.Str(id);
			tx.Put(Tables.OAuthGrant, key, Codec.Serialize(record));
			tx.Put(Tables.OAuthGrantByAccount, Keys.Str(record.AccountKey), key);
			tx.Put(Tables.OAuthGrantByCharacter, characterKey, key);
			tx.Put(Tables.OAuthGrantByClient, Keys.Str(record.ClientId), key);
			return MapToOAuthGrant(id, record);
		}, cancellationToken);
	}

	public ValueTask<Found<OAuthGrant>> GetOAuthGrantAsync(string id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<OAuthGrant>>(tx => ReadGrant(tx, id) is { } record
			? MapToOAuthGrant(id, record)
			: new NotFound()));

	public ValueTask<Found<OAuthGrant>> FindOAuthGrantAsync(DBRef character, string clientId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<OAuthGrant>>(tx => tx.Dups(Tables.OAuthGrantByCharacter, CharacterKeyOf(character))
			.Select(key => Keys.ReadStr(key))
			.Select(id => (Id: id, Record: ReadGrant(tx, id)))
			.FirstOrDefault(pair => pair.Record?.ClientId == clientId) is { Record: { } record } found
				? MapToOAuthGrant(found.Id, record)
				: new NotFound()));

	public ValueTask<IReadOnlyList<OAuthGrant>> GetOAuthGrantsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<OAuthGrant>>(Store.Read(tx => tx.Dups(Tables.OAuthGrantByAccount, Keys.Str(ParseAccountId(accountId)))
			.Select(key => Keys.ReadStr(key))
			.Select(id => (Id: id, Record: ReadGrant(tx, id)))
			.Where(pair => pair.Record is not null)
			.Select(pair => MapToOAuthGrant(pair.Id, pair.Record!))
			.OrderBy(grant => grant.CreatedAt)
			.ToList()));

	public async ValueTask TouchOAuthGrantAsync(string id, DateTimeOffset at, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (ReadGrant(tx, id) is not { } record) return;
			tx.Put(Tables.OAuthGrant, Keys.Str(id), Codec.Serialize(record with { LastUsedAtMs = at.ToUnixTimeMilliseconds() }));
		}, cancellationToken);

	public async ValueTask<bool> DeleteOAuthGrantAsync(string id, string? accountId = null, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (ReadGrant(tx, id) is not { } record
				|| (accountId is not null && record.AccountKey != ParseAccountId(accountId))) return false;
			DeleteOAuthGrantRow(tx, id, record);
			return true;
		}, cancellationToken);

	public async ValueTask<int> DeleteOAuthGrantsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var accountKey = Keys.Str(ParseAccountId(accountId));
		return await Store.WriteAsync(tx =>
		{
			var deleted = 0;
			foreach (var grantKey in tx.Dups(Tables.OAuthGrantByAccount, accountKey).ToList())
			{
				var grantId = Keys.ReadStr(grantKey);
				if (ReadGrant(tx, grantId) is { } record)
				{
					DeleteOAuthGrantRow(tx, grantId, record);
					deleted++;
				}

				tx.Delete(Tables.OAuthGrantByAccount, accountKey, grantKey);
			}

			return deleted;
		}, cancellationToken);
	}

	public async ValueTask<bool> AddOAuthTokenAsync(OAuthToken token, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (ReadGrant(tx, token.GrantId) is null) return false;
			PutOAuthTokenRow(tx, token);
			return true;
		}, cancellationToken);

	public ValueTask<Found<OAuthToken>> GetOAuthTokenAsync(byte[] hash, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<OAuthToken>>(tx => tx.TryGet(Tables.OAuthToken, hash, out var bytes)
			? MapToOAuthToken(hash, Codec.Deserialize<OAuthTokenRecord>(bytes))
			: new NotFound()));

	public async ValueTask<bool> RedeemOAuthTokenAsync(byte[] hash, IReadOnlyList<OAuthToken> issued,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.OAuthToken, hash, out var bytes)) return false;
			var record = Codec.Deserialize<OAuthTokenRecord>(bytes);
			DeleteOAuthTokenRow(tx, hash, record);
			if (ReadGrant(tx, record.GrantId) is null) return false;
			foreach (var token in issued) PutOAuthTokenRow(tx, token);
			return true;
		}, cancellationToken);

	public async ValueTask<bool> DeleteOAuthTokenAsync(byte[] hash, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.OAuthToken, hash, out var bytes)) return false;
			DeleteOAuthTokenRow(tx, hash, Codec.Deserialize<OAuthTokenRecord>(bytes));
			return true;
		}, cancellationToken);

	public async ValueTask<int> DeleteOAuthTokensForGrantAsync(string grantId, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(grantId);
		return await Store.WriteAsync(tx =>
		{
			var deleted = 0;
			foreach (var hash in tx.Dups(Tables.OAuthTokenByGrant, key).ToList())
			{
				if (tx.TryGet(Tables.OAuthToken, hash, out var bytes))
				{
					DeleteOAuthTokenRow(tx, hash, Codec.Deserialize<OAuthTokenRecord>(bytes));
					deleted++;
				}

				tx.Delete(Tables.OAuthTokenByGrant, key, hash);
			}

			return deleted;
		}, cancellationToken);
	}

	public async ValueTask<int> DeleteExpiredOAuthTokensAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default)
	{
		var nowMs = now.ToUnixTimeMilliseconds();
		return await Store.WriteAsync(tx =>
		{
			var due = tx.Range(Tables.OAuthTokenExpiry, [])
				.TakeWhile(entry => Keys.ReadDbref(entry.Key) <= nowMs)
				.Take(maxCount)
				.Select(entry => entry.Key)
				.ToList();

			var deleted = 0;
			foreach (var entry in due)
			{
				var hash = entry[8..];
				tx.Delete(Tables.OAuthTokenExpiry, entry);
				if (!tx.TryGet(Tables.OAuthToken, hash, out var bytes)) continue;
				var record = Codec.Deserialize<OAuthTokenRecord>(bytes);
				if (record.ExpiresAtMs > nowMs) continue;
				DeleteOAuthTokenRow(tx, hash, record);
				deleted++;
			}

			return deleted;
		}, cancellationToken);
	}
}
