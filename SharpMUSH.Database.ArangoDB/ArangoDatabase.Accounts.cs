using Core.Arango;
using Core.Arango.Protocol;
using SharpMUSH.Database.Models;
using SharpMUSH.Library.Models;
using System.Text.Json;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	#region Accounts

	public async ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR a IN @@c FILTER a.Email == @email RETURN a",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Accounts },
				{ "email", email }
			}, cancellationToken: cancellationToken);

		return result.FirstOrDefault() is { } elem ? AccountFromJson(elem) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR a IN @@c FILTER a.Username == @username RETURN a",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Accounts },
				{ "username", username }
			}, cancellationToken: cancellationToken);

		return result.FirstOrDefault() is { } elem ? AccountFromJson(elem) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(accountId);
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR a IN @@c FILTER a._key == @key RETURN a",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Accounts },
				{ "key", key }
			}, cancellationToken: cancellationToken);

		return result.FirstOrDefault() is { } elem ? AccountFromJson(elem) : null;
	}

	public async ValueTask<SharpAccount> CreateAccountAsync(string username, string? email, string hashedPassword, CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var doc = new
		{
			Username = username,
			Email = email,
			PasswordHash = hashedPassword,
			CreatedAt = now,
			UpdatedAt = now,
			IsVerified = false,
			IsDisabled = false
		};

		var created = await arangoDb.Document.CreateAsync<object, JsonElement>(
			handle, DatabaseConstants.Accounts, doc, returnNew: true,
			cancellationToken: cancellationToken);

		return AccountFromJson(created.New);
	}

	public async ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(accountId);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Accounts,
			new { _key = key, PasswordHash = newHash, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
			mergeObjects: true, cancellationToken: cancellationToken);
	}

	public async ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(accountId);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Accounts,
			new { _key = key, Email = newEmail, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
			mergeObjects: true, cancellationToken: cancellationToken);
	}

	public async ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(accountId);
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Accounts,
			new { _key = key, Username = newUsername, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
			mergeObjects: true, cancellationToken: cancellationToken);
	}

	public async ValueTask DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		// Remove all character links first
		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR e IN @@edge FILTER e._from == @accountId REMOVE e IN @@edge",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountOwnsCharacter },
				{ "accountId", accountId }
			}, cancellationToken: cancellationToken);

		var key = ExtractKey(accountId);
		await arangoDb.Document.DeleteAsync<JsonElement>(handle, DatabaseConstants.Accounts, key,
			cancellationToken: cancellationToken);
	}

	public async ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var player = await GetObjectNodeAsync(characterRef, cancellationToken);
		if (!player.IsPlayer) return;

		var playerId = player.AsPlayer.Id!;
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		await arangoDb.Document.CreateAsync(handle, DatabaseConstants.AccountOwnsCharacter,
			new { _from = accountId, _to = playerId, CreatedAt = now },
			cancellationToken: cancellationToken);
	}

	public async ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var player = await GetObjectNodeAsync(characterRef, cancellationToken);
		if (!player.IsPlayer) return;

		var playerId = player.AsPlayer.Id!;
		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR e IN @@edge FILTER e._from == @accountId AND e._to == @playerId REMOVE e IN @@edge",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountOwnsCharacter },
				{ "accountId", accountId },
				{ "playerId", playerId }
			}, cancellationToken: cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var playerIds = await arangoDb.Query.ExecuteAsync<string>(handle,
			"FOR e IN @@edge FILTER e._from == @accountId RETURN e._to",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountOwnsCharacter },
				{ "accountId", accountId }
			}, cancellationToken: cancellationToken);

		var result = new List<SharpPlayer>();
		foreach (var playerId in playerIds)
		{
			var obj = await GetObjectNodeAsync(playerId, cancellationToken);
			if (obj.IsPlayer)
				result.Add(obj.AsPlayer);
		}

		return result.AsReadOnly();
	}

	public async ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var player = await GetObjectNodeAsync(characterRef, cancellationToken);
		if (!player.IsPlayer) return null;

		var playerId = player.AsPlayer.Id!;
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR e IN @@edge FILTER e._to == @playerId FOR a IN @@accounts FILTER a._id == e._from RETURN a",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountOwnsCharacter },
				{ "@accounts", DatabaseConstants.Accounts },
				{ "playerId", playerId }
			}, cancellationToken: cancellationToken);

		return result.FirstOrDefault() is { } elem ? AccountFromJson(elem) : null;
	}

	private static SharpAccount AccountFromJson(JsonElement elem)
	{
		var id = elem.TryGetProperty("_id", out var idProp) ? idProp.GetString() : null;
		var email = elem.TryGetProperty("Email", out var emailProp) && emailProp.ValueKind != JsonValueKind.Null
			? emailProp.GetString()
			: null;

		return new SharpAccount
		{
			Id = id,
			Username = elem.GetProperty("Username").GetString()!,
			Email = email,
			PasswordHash = elem.GetProperty("PasswordHash").GetString()!,
			CreatedAt = elem.TryGetProperty("CreatedAt", out var createdProp) ? createdProp.GetInt64() : 0,
			UpdatedAt = elem.TryGetProperty("UpdatedAt", out var updatedProp) ? updatedProp.GetInt64() : 0,
			IsVerified = elem.TryGetProperty("IsVerified", out var verifiedProp) && verifiedProp.GetBoolean(),
			IsDisabled = elem.TryGetProperty("IsDisabled", out var disabledProp) && disabledProp.GetBoolean()
		};
	}

	private static string ExtractKey(string id) =>
		id.Contains('/') ? id.Split('/')[1] : id;

	#endregion
}
