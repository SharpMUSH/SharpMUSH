using SharpMUSH.Database;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountStore"/>: web accounts and the character links that tie them to players. Ported
/// from <c>SurrealDatabase.Accounts.cs</c>; unlike that provider, uniqueness on email and username is
/// enforced here inside the writer job (<c>TryGet</c> before <c>Put</c> on <see cref="Tables.AccountEmail"/>
/// / <see cref="Tables.AccountUser"/>) because there is exactly one writer, so check-then-put is atomic.
/// </summary>
public sealed partial class LightningDatabase
{
	/// <summary>Parses either a bare account key ("5") or a typed id ("node_accounts/5") into its raw key.</summary>
	private static string ParseAccountId(string accountId) => accountId.Contains('/') ? accountId[(accountId.IndexOf('/') + 1)..] : accountId;

	/// <summary>Reads and increments the <c>next_account_id</c> counter inside a write job, returning the id allocated to the caller.</summary>
	private static string AllocateAccountId(ITx tx)
	{
		var current = tx.TryGet(Tables.Meta, Keys.Str("next_account_id"), out var v) ? Keys.ReadDbref(v) : 0;
		tx.Put(Tables.Meta, Keys.Str("next_account_id"), Keys.Dbref(current + 1));
		return current.ToString();
	}

	private static SharpAccount MapToSharpAccount(string key, AccountRecord record) => new()
	{
		Id = $"node_accounts/{key}",
		Username = record.Username,
		Email = record.Email,
		PasswordHash = record.PasswordHash,
		CreatedAt = record.CreatedAt,
		UpdatedAt = record.UpdatedAt,
		IsVerified = record.IsVerified,
		MustChangePassword = record.MustChangePassword,
		Status = AccountStatusParser.Parse(record.Status)
	};

	private static SharpAccount? ReadAccountByKey(ITx tx, string key)
		=> tx.TryGet(Tables.Account, Keys.Str(key), out var bytes) ? MapToSharpAccount(key, Codec.Deserialize<AccountRecord>(bytes)) : null;

	public ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.AccountEmail, Keys.Lower(email), out var idBytes)
			? ReadAccountByKey(tx, Keys.ReadStr(idBytes))
			: null);
		return ValueTask.FromResult(result);
	}

	public ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.AccountUser, Keys.Lower(username), out var idBytes)
			? ReadAccountByKey(tx, Keys.ReadStr(idBytes))
			: null);
		return ValueTask.FromResult(result);
	}

	public ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		var result = Store.Read(tx => ReadAccountByKey(tx, key));
		return ValueTask.FromResult(result);
	}

	public ValueTask<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx => tx.Count(Tables.Account)) > 0);

	public async ValueTask<SharpAccount> CreateAccountAsync(string username, string? email, string hashedPassword, CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		return await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.AccountUser, Keys.Lower(username), out _))
				throw new InvalidOperationException($"Username '{username}' is already taken.");
			if (email is not null && tx.TryGet(Tables.AccountEmail, Keys.Lower(email), out _))
				throw new InvalidOperationException($"Email '{email}' is already registered.");

			var key = AllocateAccountId(tx);
			var record = new AccountRecord
			{
				Username = username,
				Email = email,
				PasswordHash = hashedPassword,
				CreatedAt = now,
				UpdatedAt = now,
				IsVerified = false,
				MustChangePassword = false,
				Status = nameof(AccountStatus.Active)
			};

			tx.Put(Tables.Account, Keys.Str(key), Codec.Serialize(record));
			tx.Put(Tables.AccountUser, Keys.Lower(username), Keys.Str(key));
			if (email is not null)
				tx.Put(Tables.AccountEmail, Keys.Lower(email), Keys.Str(key));

			return MapToSharpAccount(key, record);
		}, cancellationToken);
	}

	public async ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out var bytes)) return;
			var record = Codec.Deserialize<AccountRecord>(bytes);
			var updated = record with { PasswordHash = newHash, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
			tx.Put(Tables.Account, Keys.Str(key), Codec.Serialize(updated));
		}, cancellationToken);
	}

	public async ValueTask UpdateAccountMustChangePasswordAsync(string accountId, bool value, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out var bytes)) return;
			var record = Codec.Deserialize<AccountRecord>(bytes);
			var updated = record with { MustChangePassword = value, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
			tx.Put(Tables.Account, Keys.Str(key), Codec.Serialize(updated));
		}, cancellationToken);
	}

	public async ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out var bytes)) return;
			var record = Codec.Deserialize<AccountRecord>(bytes);

			if (newEmail is not null && tx.TryGet(Tables.AccountEmail, Keys.Lower(newEmail), out var ownerBytes) && Keys.ReadStr(ownerBytes) != key)
				throw new InvalidOperationException($"Email '{newEmail}' is already registered.");

			if (record.Email is not null)
				tx.Delete(Tables.AccountEmail, Keys.Lower(record.Email));
			if (newEmail is not null)
				tx.Put(Tables.AccountEmail, Keys.Lower(newEmail), Keys.Str(key));

			var updated = record with { Email = newEmail, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
			tx.Put(Tables.Account, Keys.Str(key), Codec.Serialize(updated));
		}, cancellationToken);
	}

	public async ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out var bytes)) return;
			var record = Codec.Deserialize<AccountRecord>(bytes);

			if (tx.TryGet(Tables.AccountUser, Keys.Lower(newUsername), out var ownerBytes) && Keys.ReadStr(ownerBytes) != key)
				throw new InvalidOperationException($"Username '{newUsername}' is already taken.");

			tx.Delete(Tables.AccountUser, Keys.Lower(record.Username));
			tx.Put(Tables.AccountUser, Keys.Lower(newUsername), Keys.Str(key));

			var updated = record with { Username = newUsername, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
			tx.Put(Tables.Account, Keys.Str(key), Codec.Serialize(updated));
		}, cancellationToken);
	}

	public async ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			tx.Put(Tables.AccountChar.Forward, Keys.Str(key), Keys.Dbref(characterRef.Number));
			tx.Put(Tables.AccountChar.Reverse, Keys.Dbref(characterRef.Number), Keys.Str(key));
		}, cancellationToken);
	}

	public async ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.AccountChar.Forward, Keys.Str(key), Keys.Dbref(characterRef.Number));
			tx.Delete(Tables.AccountChar.Reverse, Keys.Dbref(characterRef.Number), Keys.Str(key));
		}, cancellationToken);
	}

	public ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		var result = Store.Read(tx => tx.Dups(Tables.AccountChar.Forward, Keys.Str(key))
			.Select(v => Keys.ReadDbref(v))
			.Select(dbref => ReadObject(tx, dbref))
			.Where(found => found is not null && found.Value.Record.Type == DatabaseConstants.TypePlayer)
			.Select(found => Hydrate(tx, found!.Value.Dbref, found.Value.Record).AsPlayer)
			.ToList());
		return ValueTask.FromResult<IReadOnlyList<SharpPlayer>>(result);
	}

	public ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.AccountChar.Reverse, Keys.Dbref(characterRef.Number), out var idBytes)
			? ReadAccountByKey(tx, Keys.ReadStr(idBytes))
			: null);
		return ValueTask.FromResult(result);
	}

	public async ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out var bytes)) return;
			var record = Codec.Deserialize<AccountRecord>(bytes);
			var updated = record with { Status = status.ToString(), UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
			tx.Put(Tables.Account, Keys.Str(key), Codec.Serialize(updated));
		}, cancellationToken);
	}

	public ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.Range(Tables.Account, [])
			.Select(e => MapToSharpAccount(Keys.ReadStr(e.Key), Codec.Deserialize<AccountRecord>(e.Value)))
			.OrderBy(a => a.Username, StringComparer.Ordinal)
			.ToList());
		return ValueTask.FromResult<IReadOnlyList<SharpAccount>>(result);
	}
}
