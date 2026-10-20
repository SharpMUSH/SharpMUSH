using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountStore"/>'s sign-ins through other services. A row is keyed by provider + 0x00 + subject, which
/// is what a sign-in looks it up by; <see cref="Tables.AccountExternalLoginByAccount"/> lists an account's.
/// </summary>
public partial class LightningDatabase
{
	private static byte[] ExternalLoginKey(string provider, string subject) => Keys.Composite(provider, subject);

	private static AccountExternalLogin MapToExternalLogin(AccountExternalLoginRecord record) => new(
		$"node_accounts/{record.AccountKey}",
		record.Provider,
		record.Subject,
		record.Name,
		DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtMs),
		record.LastUsedAtMs is { } used ? DateTimeOffset.FromUnixTimeMilliseconds(used) : null);

	private static AccountExternalLoginRecord ExternalLoginRecord(string accountKey, AccountExternalLogin login) => new()
	{
		AccountKey = accountKey,
		Provider = login.Provider,
		Subject = login.Subject,
		Name = login.Name,
		CreatedAtMs = login.CreatedAt.ToUnixTimeMilliseconds(),
		LastUsedAtMs = login.LastUsedAt?.ToUnixTimeMilliseconds()
	};

	/// <summary>
	/// Whether <paramref name="key"/> is free for a new link inside this write. A link held by a deleted account is
	/// dropped from that account here, so it can move.
	/// </summary>
	private static bool ClaimExternalLoginKey(ITx tx, byte[] key)
	{
		if (!tx.TryGet(Tables.AccountExternalLogin, key, out var bytes)) return true;

		var holder = Codec.Deserialize<AccountExternalLoginRecord>(bytes).AccountKey;
		if (ReadAccountByKey(tx, holder) is { Status: not AccountStatus.Deleted }) return false;

		tx.Delete(Tables.AccountExternalLogin, key);
		tx.Delete(Tables.AccountExternalLoginByAccount, Keys.Str(holder), key);
		return true;
	}

	private static IEnumerable<AccountExternalLoginRecord> ReadExternalLogins(ITx tx, string accountKey)
		=> tx.Dups(Tables.AccountExternalLoginByAccount, Keys.Str(accountKey))
			.Select(key => tx.TryGet(Tables.AccountExternalLogin, key, out var bytes)
				? Codec.Deserialize<AccountExternalLoginRecord>(bytes)
				: null)
			.OfType<AccountExternalLoginRecord>()
			.Where(record => record.AccountKey == accountKey);

	private static void PutExternalLogin(ITx tx, byte[] key, AccountExternalLoginRecord record)
	{
		tx.Put(Tables.AccountExternalLogin, key, Codec.Serialize(record));
		tx.Put(Tables.AccountExternalLoginByAccount, Keys.Str(record.AccountKey), key);
	}

	public ValueTask<AccountExternalLogin?> GetAccountExternalLoginAsync(string provider, string subject,
		CancellationToken cancellationToken = default)
	{
		var key = ExternalLoginKey(provider, subject);
		return ValueTask.FromResult(Store.Read(tx => tx.TryGet(Tables.AccountExternalLogin, key, out var bytes)
			? MapToExternalLogin(Codec.Deserialize<AccountExternalLoginRecord>(bytes))
			: null));
	}

	public ValueTask<IReadOnlyList<AccountExternalLogin>> GetAccountExternalLoginsAsync(string accountId,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		var result = Store.Read(tx => ReadExternalLogins(tx, accountKey)
			.OrderBy(record => record.CreatedAtMs)
			.Select(MapToExternalLogin)
			.ToList());
		return ValueTask.FromResult<IReadOnlyList<AccountExternalLogin>>(result);
	}

	public async ValueTask<ExternalLoginAddOutcome> AddAccountExternalLoginAsync(AccountExternalLogin login,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(login.AccountId);
		var key = ExternalLoginKey(login.Provider, login.Subject);
		var record = ExternalLoginRecord(accountKey, login);

		return await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.AccountExternalLogin, key, out var held)
				&& Codec.Deserialize<AccountExternalLoginRecord>(held).AccountKey == accountKey)
				return ExternalLoginAddOutcome.ProviderAlreadyLinked;
			if (ReadExternalLogins(tx, accountKey).Any(existing => existing.Provider == login.Provider))
				return ExternalLoginAddOutcome.ProviderAlreadyLinked;
			if (!ClaimExternalLoginKey(tx, key))
				return ExternalLoginAddOutcome.LinkedElsewhere;

			PutExternalLogin(tx, key, record);
			return ExternalLoginAddOutcome.Added;
		}, cancellationToken);
	}

	public async ValueTask<ExternalAccountCreation> CreateAccountWithExternalLoginAsync(string username, string? email,
		AccountExternalLogin login, CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var loginKey = ExternalLoginKey(login.Provider, login.Subject);

		return await Store.WriteAsync<ExternalAccountCreation>(tx =>
		{
			if (tx.TryGet(Tables.AccountUser, Keys.Lower(username), out _))
				return new Error<string>($"Username '{username}' is already taken.");
			if (email is not null && tx.TryGet(Tables.AccountEmail, Keys.Lower(email), out _))
				return new Error<string>($"Email '{email}' is already registered.");
			if (!ClaimExternalLoginKey(tx, loginKey))
				return new ExternalLoginTaken();

			var key = AllocateAccountId(tx);
			var record = new AccountRecord
			{
				Username = username,
				Email = email,
				PasswordHash = string.Empty,
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
			PutExternalLogin(tx, loginKey, ExternalLoginRecord(key, login));

			return MapToSharpAccount(key, record);
		}, cancellationToken);
	}

	public async ValueTask RecordAccountExternalLoginUseAsync(string provider, string subject, string name,
		DateTimeOffset usedAt, CancellationToken cancellationToken = default)
	{
		var key = ExternalLoginKey(provider, subject);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.AccountExternalLogin, key, out var bytes)) return;
			var record = Codec.Deserialize<AccountExternalLoginRecord>(bytes);
			tx.Put(Tables.AccountExternalLogin, key, Codec.Serialize(record with
			{
				Name = name,
				LastUsedAtMs = usedAt.ToUnixTimeMilliseconds()
			}));
		}, cancellationToken);
	}

	public async ValueTask<bool> RemoveAccountExternalLoginAsync(string accountId, string provider,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		return await Store.WriteAsync(tx =>
		{
			if (ReadExternalLogins(tx, accountKey).FirstOrDefault(record => record.Provider == provider) is not { } found)
				return false;

			var key = ExternalLoginKey(found.Provider, found.Subject);
			tx.Delete(Tables.AccountExternalLogin, key);
			tx.Delete(Tables.AccountExternalLoginByAccount, Keys.Str(accountKey), key);
			return true;
		}, cancellationToken);
	}
}
