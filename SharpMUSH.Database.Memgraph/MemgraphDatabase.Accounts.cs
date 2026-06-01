using Neo4j.Driver;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Accounts

	public async ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:Account {email: $email}) RETURN a",
			new { email }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToAccount(result.Result[0]["a"].As<INode>()) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:Account {username: $username}) RETURN a",
			new { username }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToAccount(result.Result[0]["a"].As<INode>()) : null;
	}

	public async ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) RETURN a",
			new { id = key }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToAccount(result.Result[0]["a"].As<INode>()) : null;
	}

	public async ValueTask<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (a:Account) RETURN a LIMIT 1",
			new { }, cancellationToken);
		return result.Result.Count > 0;
	}

	public async ValueTask<SharpAccount> CreateAccountAsync(string username, string? email, string hashedPassword, CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var id = Guid.NewGuid().ToString("N");
		await ExecuteWithRetryAsync("""
CREATE (a:Account {
	id: $id, username: $username, email: $email, passwordHash: $passwordHash,
	createdAt: $createdAt, updatedAt: $updatedAt, isVerified: false, mustChangePassword: false, isDisabled: false
})
""", new { id, username, email = (object?)email ?? DBNull.Value, passwordHash = hashedPassword, createdAt = now, updatedAt = now }, cancellationToken);

		return new SharpAccount
		{
			Id = $"node_accounts/{id}",
			Username = username,
			Email = email,
			PasswordHash = hashedPassword,
			CreatedAt = now,
			UpdatedAt = now
		};
	}

	public async ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) SET a.passwordHash = $hash, a.updatedAt = $now",
			new { id = key, hash = newHash, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken);
	}

	public async ValueTask UpdateAccountMustChangePasswordAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) SET a.mustChangePassword = $value, a.updatedAt = $now",
			new { id = key, value, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken);
	}

	public async ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) SET a.email = $email, a.updatedAt = $now",
			new { id = key, email = (object?)newEmail ?? DBNull.Value, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken);
	}

	public async ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) SET a.username = $username, a.updatedAt = $now",
			new { id = key, username = newUsername, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken);
	}

	public async ValueTask DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync(
			"MATCH (a:Account {id: $id}) DETACH DELETE a",
			new { id = key }, cancellationToken);
	}

	public async ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		await ExecuteWithRetryAsync("""
MATCH (a:Account {id: $id}), (p:Player)-[:IS_OBJECT]->(o:Object {key: $dbref})
MERGE (a)-[:OWNS_CHARACTER {createdAt: $now}]->(p)
""", new { id = key, dbref = characterRef.Number, now }, cancellationToken);
	}

	public async ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync("""
MATCH (a:Account {id: $id})-[r:OWNS_CHARACTER]->(p:Player)-[:IS_OBJECT]->(o:Object {key: $dbref})
DELETE r
""", new { id = key, dbref = characterRef.Number }, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		var result = await ExecuteWithRetryAsync("""
MATCH (a:Account {id: $id})-[:OWNS_CHARACTER]->(p:Player)-[:IS_OBJECT]->(o:Object)
RETURN o.key AS dbref
""", new { id = key }, cancellationToken);

		var players = new List<SharpPlayer>();
		foreach (var record in result.Result)
		{
			var dbrefNum = record["dbref"].As<int>();
			var obj = await GetObjectNodeAsync(new DBRef(dbrefNum), cancellationToken);
			if (obj.IsPlayer)
				players.Add(obj.AsPlayer);
		}
		return players.AsReadOnly();
	}

	public async ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (a:Account)-[:OWNS_CHARACTER]->(p:Player)-[:IS_OBJECT]->(o:Object {key: $dbref})
RETURN a
""", new { dbref = characterRef.Number }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToAccount(result.Result[0]["a"].As<INode>()) : null;
	}

	private static SharpAccount MapNodeToAccount(INode node)
	{
		var email = node.Properties.TryGetValue("email", out var emailVal) && emailVal is not null && emailVal is not DBNull
			? emailVal.ToString()
			: null;
		return new SharpAccount
		{
			Id = $"node_accounts/{node["id"]}",
			Username = node["username"].As<string>(),
			Email = email,
			PasswordHash = node["passwordHash"].As<string>(),
			CreatedAt = node.Properties.TryGetValue("createdAt", out var created) ? Convert.ToInt64(created) : 0,
			UpdatedAt = node.Properties.TryGetValue("updatedAt", out var updated) ? Convert.ToInt64(updated) : 0,
			IsVerified = node.Properties.TryGetValue("isVerified", out var verified) && (bool)verified,
			MustChangePassword = node.Properties.TryGetValue("mustChangePassword", out var mustChange) && (bool)mustChange,
			IsDisabled = node.Properties.TryGetValue("isDisabled", out var disabled) && (bool)disabled
		};
	}

	#endregion
}
