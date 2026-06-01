using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class AccountService(ISharpDatabase database, IPasswordService passwordService) : IAccountService
{
	// Account IDs are used as the "user" salt key for hashing
	private static string AccountKey(SharpAccount account) => $"account:{account.Id}:{account.CreatedAt}";

	public async ValueTask<SharpAccount?> AuthenticateAsync(string displayNameOrEmail, string password, CancellationToken ct = default)
	{
		// Detect email vs display name by presence of '@'
		SharpAccount? account = displayNameOrEmail.Contains('@')
			? await database.GetAccountByEmailAsync(displayNameOrEmail, ct)
			: await database.GetAccountByDisplayNameAsync(displayNameOrEmail, ct);

		if (account is null || account.IsDisabled)
			return null;

		if (!passwordService.PasswordIsValid(AccountKey(account), password, account.PasswordHash))
			return null;

		return account;
	}

	public async ValueTask<SharpAccount> CreateAccountAsync(string displayName, string? email, string password, CancellationToken ct = default)
	{
		if (await database.GetAccountByDisplayNameAsync(displayName, ct) is not null)
			throw new InvalidOperationException($"Display name '{displayName}' is already taken.");

		if (email is not null && await database.GetAccountByEmailAsync(email, ct) is not null)
			throw new InvalidOperationException($"Email '{email}' is already registered.");

		// Create with a temporary hash first to get the ID, then update with final hash
		// (We need the ID to salt the password, but we need the password to create)
		// Use a predictable temp placeholder, then immediately rehash
		var tempHash = passwordService.HashPassword($"account:pending:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", password);
		var account = await database.CreateAccountAsync(displayName, email, tempHash, ct);

		// Now rehash with the proper key
		var realHash = passwordService.HashPassword(AccountKey(account), password);
		await database.UpdateAccountPasswordAsync(account.Id!, realHash, ct);
		account.PasswordHash = realHash;

		return account;
	}

	public async ValueTask<bool> DisplayNameExistsAsync(string displayName, CancellationToken ct = default)
		=> await database.GetAccountByDisplayNameAsync(displayName, ct) is not null;

	public async ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default)
		=> await database.GetAccountByEmailAsync(email, ct) is not null;

	public async ValueTask ChangePasswordAsync(string accountId, string oldPassword, string newPassword, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct)
			?? throw new InvalidOperationException("Account not found.");

		if (!passwordService.PasswordIsValid(AccountKey(account), oldPassword, account.PasswordHash))
			throw new UnauthorizedAccessException("Current password is incorrect.");

		var newHash = passwordService.HashPassword(AccountKey(account), newPassword);
		await database.UpdateAccountPasswordAsync(accountId, newHash, ct);
	}

	public async ValueTask ChangeEmailAsync(string accountId, string? newEmail, string currentPassword, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct)
			?? throw new InvalidOperationException("Account not found.");

		if (!passwordService.PasswordIsValid(AccountKey(account), currentPassword, account.PasswordHash))
			throw new UnauthorizedAccessException("Current password is incorrect.");

		if (newEmail is not null && await database.GetAccountByEmailAsync(newEmail, ct) is not null)
			throw new InvalidOperationException($"Email '{newEmail}' is already registered.");

		await database.UpdateAccountEmailAsync(accountId, newEmail, ct);
	}

	public async ValueTask ChangeDisplayNameAsync(string accountId, string newDisplayName, CancellationToken ct = default)
	{
		if (await database.GetAccountByDisplayNameAsync(newDisplayName, ct) is not null)
			throw new InvalidOperationException($"Display name '{newDisplayName}' is already taken.");

		await database.UpdateAccountDisplayNameAsync(accountId, newDisplayName, ct);
	}

	public ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersAsync(string accountId, CancellationToken ct = default)
		=> database.GetCharactersForAccountAsync(accountId, ct);

	public ValueTask LinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default)
		=> database.LinkCharacterToAccountAsync(accountId, characterRef, ct);

	public ValueTask UnlinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default)
		=> database.UnlinkCharacterFromAccountAsync(accountId, characterRef, ct);

	public ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken ct = default)
		=> database.GetAccountForCharacterAsync(characterRef, ct);

	public ValueTask<SharpAccount?> GetByIdAsync(string accountId, CancellationToken ct = default)
		=> database.GetAccountByIdAsync(accountId, ct);

	public ValueTask<SharpAccount?> GetByDisplayNameAsync(string displayName, CancellationToken ct = default)
		=> database.GetAccountByDisplayNameAsync(displayName, ct);

	public ValueTask<SharpAccount?> GetByEmailAsync(string email, CancellationToken ct = default)
		=> database.GetAccountByEmailAsync(email, ct);

	public async ValueTask DisableAccountAsync(string accountId, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct)
			?? throw new InvalidOperationException("Account not found.");
		// IsDisabled is stored as part of the document; update via a targeted call
		// We use UpdateAccountEmailAsync as a proxy here — but actually we need a dedicated method.
		// For now, we'll mark it via display-name convention; TODO: add UpdateAccountDisabledAsync
		_ = account; // suppress warning — will be handled properly once disable field update is added
		throw new NotImplementedException("DisableAccountAsync requires a dedicated DB update method.");
	}

	public ValueTask DeleteAccountAsync(string accountId, CancellationToken ct = default)
		=> database.DeleteAccountAsync(accountId, ct);
}
