using SharpMUSH.Library.Models;
using SurrealDb.Net;
using System.Text.Json.Serialization;

namespace SharpMUSH.Database.SurrealDB;

internal record AccountCharacterRef(
	[property: JsonPropertyName("dbref")] int Dbref
);

internal record AccountDbRecord(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("username")] string Username,
	[property: JsonPropertyName("email")] string? Email,
	[property: JsonPropertyName("passwordHash")] string PasswordHash,
	[property: JsonPropertyName("createdAt")] long CreatedAt,
	[property: JsonPropertyName("updatedAt")] long UpdatedAt,
	[property: JsonPropertyName("isVerified")] bool IsVerified,
	[property: JsonPropertyName("mustChangePassword")] bool MustChangePassword,
	[property: JsonPropertyName("isDisabled")] bool IsDisabled
);

public partial class SurrealDatabase
{
	#region Accounts

	public async ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["email"] = email };
		var response = await ExecuteAsync("SELECT * FROM account WHERE email = $email", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["username"] = username };
		var response = await ExecuteAsync("SELECT * FROM account WHERE username = $username", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains(':') ? accountId : $"account:{(accountId.Contains('/') ? accountId.Split('/')[1] : accountId)}";
		var parameters = new Dictionary<string, object?> { ["id"] = key };
		var response = await ExecuteAsync("SELECT * FROM $id", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	public async ValueTask<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM account LIMIT 1", new Dictionary<string, object?>(), cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0;
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
		var results = response.GetValue<List<AccountDbRecord>>(0)!;
		return MapRecordToAccount(results[0]);
	}

	public async ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = key, ["hash"] = newHash, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET passwordHash = $hash, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask UpdateAccountMustChangePasswordAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = key, ["value"] = value, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET mustChangePassword = $value, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = key, ["email"] = newEmail, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET email = $email, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = key, ["username"] = newUsername, ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
		await ExecuteAsync("UPDATE $id SET username = $username, updatedAt = $now", parameters, cancellationToken);
	}

	public async ValueTask DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["id"] = key };
		await ExecuteAsync("DELETE $id", parameters, cancellationToken);
		await ExecuteAsync("DELETE account_owns_character WHERE in = $id", parameters, cancellationToken);
	}

	public async ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var parameters = new Dictionary<string, object?> { ["accountId"] = key, ["dbref"] = characterRef.Number, ["now"] = now };
		await ExecuteAsync("""
LET $p = (SELECT id FROM player WHERE object.key = $dbref)[0].id;
RELATE $accountId->account_owns_character->$p SET createdAt = $now
""", parameters, cancellationToken);
	}

	public async ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["accountId"] = key, ["dbref"] = characterRef.Number };
		await ExecuteAsync("""
DELETE account_owns_character WHERE in = $accountId AND out.object.key = $dbref
""", parameters, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["accountId"] = key };
		var response = await ExecuteAsync("""
SELECT out.object.key AS dbref FROM account_owns_character WHERE in = $accountId
""", parameters, cancellationToken);

		var records = response.GetValue<List<AccountCharacterRef>>(0) ?? [];
		var players = new List<SharpPlayer>();
		foreach (var record in records)
		{
			var obj = await GetObjectNodeAsync(new DBRef(record.Dbref), cancellationToken);
			if (obj.IsPlayer)
				players.Add(obj.AsPlayer);
		}
		return players.AsReadOnly();
	}

	public async ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["dbref"] = characterRef.Number };
		var response = await ExecuteAsync("""
SELECT in.* FROM account_owns_character WHERE out.object.key = $dbref
""", parameters, cancellationToken);
		var results = response.GetValue<List<AccountDbRecord>>(0);
		return results?.Count > 0 ? MapRecordToAccount(results[0]) : null;
	}

	private static SharpAccount MapRecordToAccount(AccountDbRecord rec) => new()
	{
		Id = rec.Id.Contains(':') ? $"node_accounts/{rec.Id.Split(':')[1]}" : rec.Id,
		Username = rec.Username,
		Email = rec.Email,
		PasswordHash = rec.PasswordHash,
		CreatedAt = rec.CreatedAt,
		UpdatedAt = rec.UpdatedAt,
		IsVerified = rec.IsVerified,
		MustChangePassword = rec.MustChangePassword,
		IsDisabled = rec.IsDisabled
	};

	private static string NormalizeSurrealId(string id, string table) =>
		id.Contains(':') ? id : $"{table}:{(id.Contains('/') ? id.Split('/')[1] : id)}";

	#endregion
}
