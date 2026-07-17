using SharpMUSH.Library.Models;
using SurrealDb.Net;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

// SurrealDb.Net deserializes by exact (case-sensitive) field name and does NOT honor
// [JsonPropertyName]; record property names must match the stored camelCase fields
// verbatim, exactly as ObjectRecord/PlayerRecord do.
internal record AccountCharacterRef(int dbref);

internal class AccountDbRecord : Record
{
	public string username { get; set; } = "";
	public string? email { get; set; }
	public string passwordHash { get; set; } = "";
	public long createdAt { get; set; }
	public long updatedAt { get; set; }
	public bool isVerified { get; set; }
	public bool mustChangePassword { get; set; }
	public bool isDisabled { get; set; }
}

public partial class SurrealDatabase
{
	#region Accounts

	private const string AccountFieldSelection = "id, username, email, passwordHash, createdAt, updatedAt, isVerified, mustChangePassword, isDisabled";

	public async ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["email"] = email };
		var response = await ExecuteAsync($"SELECT {AccountFieldSelection} FROM account WHERE email = $email", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["username"] = username };
		var response = await ExecuteAsync($"SELECT {AccountFieldSelection} FROM account WHERE username = $username", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
		var response = await ExecuteAsync($"SELECT {AccountFieldSelection} FROM $id", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT count() AS cnt FROM account GROUP ALL", new Dictionary<string, object?>(), cancellationToken);
		var results = response.GetValue<List<CountRecord>>(0);
		return results?.Count > 0 && results[0].cnt > 0;
	}

	public async ValueTask<SharpAccount> CreateAccountAsync(string username, string? email, string hashedPassword, CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var parameters = new Dictionary<string, object?>
		{
			["username"] = username,
			["email"] = email,
			["passwordHash"] = hashedPassword,
			["createdAt"] = now,
			["updatedAt"] = now
		};
		var response = await ExecuteAsync("""
CREATE account CONTENT {
	username: $username, email: $email, passwordHash: $passwordHash,
	createdAt: $createdAt, updatedAt: $updatedAt, isVerified: false, mustChangePassword: false, isDisabled: false
}
""", parameters, cancellationToken);
		return await GetAccountByUsernameAsync(username, cancellationToken)
			?? throw new InvalidOperationException($"Failed to read back created account '{username}'.");
	}

	public async ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key), ["hash"] = newHash, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET passwordHash = $hash, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask UpdateAccountMustChangePasswordAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key), ["value"] = value, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET mustChangePassword = $value, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key), ["email"] = newEmail, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET email = $email, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key), ["username"] = newUsername, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET username = $username, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
		// One transaction so the account row and its ownership edges are torn down atomically.
		await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"DELETE account_owns_character WHERE in = $id;" +
			"DELETE $id;" +
			"COMMIT TRANSACTION",
			parameters, cancellationToken);
	}

	public async ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var parameters = new Dictionary<string, object?> { ["accountId"] = new StringRecordId(key), ["dbref"] = characterRef.Number, ["now"] = now };
		await ExecuteAsync("""
LET $p = (SELECT id FROM player WHERE key = $dbref)[0].id;
RELATE $accountId->account_owns_character->$p SET createdAt = $now
""", parameters, cancellationToken);
	}

	public async ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["accountId"] = new StringRecordId(key), ["dbref"] = characterRef.Number };
		await ExecuteAsync("""
DELETE account_owns_character WHERE in = $accountId AND out.key = $dbref
""", parameters, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["accountId"] = new StringRecordId(key) };
		var response = await ExecuteAsync("""
SELECT out.key AS dbref FROM account_owns_character WHERE in = $accountId
""", parameters, cancellationToken);

		var records = response.GetValue<List<AccountCharacterRef>>(0) ?? [];
		var players = new List<SharpPlayer>();
		foreach (var record in records)
		{
			var obj = await GetObjectNodeAsync(new DBRef(record.dbref), cancellationToken);
			if (obj.IsPlayer)
				players.Add(obj.AsPlayer);
		}
		return players.AsReadOnly();
	}

	public async ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["dbref"] = characterRef.Number };
		var response = await ExecuteAsync("""
SELECT in.id AS id, in.username AS username, in.email AS email, in.passwordHash AS passwordHash, in.createdAt AS createdAt, in.updatedAt AS updatedAt, in.isVerified AS isVerified, in.mustChangePassword AS mustChangePassword, in.isDisabled AS isDisabled
FROM account_owns_character WHERE out.key = $dbref
""", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask UpdateAccountDisabledAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		await ExecuteAsync("UPDATE $accountId SET isDisabled = $value, updatedAt = $now",
			new Dictionary<string, object?>
			{
				["accountId"] = new StringRecordId(key),
				["value"] = value,
				["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync($"SELECT {AccountFieldSelection} FROM account ORDER BY username",
			new Dictionary<string, object?>(), cancellationToken);
		var rows = response.GetValue<List<AccountDbRecord>>(0) ?? [];
		return rows.Select(MapRecordToAccount).ToList();
	}

	private static SharpAccount MapRecordToAccount(AccountDbRecord rec) => new()
	{
		Id = NormalizeAccountId(rec.Id),
		Username = rec.username,
		Email = rec.email,
		PasswordHash = rec.passwordHash,
		CreatedAt = rec.createdAt,
		UpdatedAt = rec.updatedAt,
		IsVerified = rec.isVerified,
		MustChangePassword = rec.mustChangePassword,
		IsDisabled = rec.isDisabled
	};

	private static string NormalizeAccountId(RecordId? id)
	{
		ArgumentNullException.ThrowIfNull(id);

		if (id.TryDeserializeId<string>(out var stringId))
			return $"node_accounts/{stringId}";

		if (id.TryDeserializeId<long>(out var longId))
			return $"node_accounts/{longId}";

		if (id.TryDeserializeId<int>(out var intId))
			return $"node_accounts/{intId}";

		throw new InvalidOperationException($"Unsupported SurrealDB account record ID type for table '{id.Table}'.");
	}

	private static string NormalizeSurrealId(string id, string table) =>
		id.Contains(':') ? id : $"{table}:{(id.Contains('/') ? id.Split('/')[1] : id)}";

	#endregion
}
