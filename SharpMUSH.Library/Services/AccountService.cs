using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class AccountService(ISharpDatabase database, IPasswordService passwordService, IAccountSessionStore accountSessionStore) : IAccountService
{
	// Account IDs are used as the "user" salt key for hashing
	private static string AccountKey(SharpAccount account) => $"account:{account.Id}:{account.CreatedAt}";

	public async ValueTask<SharpAccount?> AuthenticateAsync(string usernameOrEmail, string password, CancellationToken ct = default)
	{
		var account = usernameOrEmail.Contains('@')
			? await database.GetAccountByEmailAsync(usernameOrEmail, ct)
			: await database.GetAccountByUsernameAsync(usernameOrEmail, ct);

		// Character-like login: a character name resolves to its owning account,
		// authenticated by that character's password only.
		SharpPlayer? namedCharacter = null;
		if (account is null)
		{
			namedCharacter = await database.GetPlayerByNameOrAliasAsync(usernameOrEmail, ct)
				.FirstOrDefaultAsync(ct);
			if (namedCharacter is not null)
				account = await database.GetAccountForCharacterAsync(
					new DBRef(namedCharacter.Object.Key, namedCharacter.Object.CreationTime), ct);
		}

		if (account is null || account.IsDisabled)
			return null;

		// A character-name identifier authenticates only via that specific character's own
		// password: the owning account's password (or any *other* linked character's password)
		// must not be accepted through this identifier.
		if (namedCharacter is not null)
			return await CharacterPasswordMatchesAsync(namedCharacter, password) ? account : null;

		// Empty stored hashes never match at the account level: God's PennMUSH-default empty
		// character password stays a telnet-connect special case, and the pre-generated
		// (unclaimed) admin account stays unlobbable until first-run setup claims it.
		if (!string.IsNullOrEmpty(account.PasswordHash)
			&& passwordService.PasswordIsValid(AccountKey(account), password, account.PasswordHash))
			return account;

		var characters = await database.GetCharactersForAccountAsync(account.Id!, ct);
		foreach (var character in characters)
			if (await CharacterPasswordMatchesAsync(character, password))
				return account;

		return null;
	}

	private async ValueTask<bool> CharacterPasswordMatchesAsync(SharpPlayer character, string password)
	{
		if (string.IsNullOrEmpty(character.PasswordHash))
			return false;

		var key = $"#{character.Object.Key}:{character.Object.CreationTime}";
		if (!passwordService.PasswordIsValid(key, password, character.PasswordHash))
			return false;

		if (passwordService.NeedsRehash(character.PasswordHash))
			await passwordService.RehashPasswordAsync(character, password);

		return true;
	}

	public ValueTask<bool> HasAnyAccountAsync(CancellationToken ct = default)
		=> database.HasAnyAccountAsync(ct);

	public async ValueTask<OneOf<SharpAccount, Error<string>>> CreateAccountAsync(string username, string? email, string password, CancellationToken ct = default)
	{
		if (await database.GetAccountByUsernameAsync(username, ct) is not null)
			return new Error<string>($"Username '{username}' is already taken.");

		if (email is not null && await database.GetAccountByEmailAsync(email, ct) is not null)
			return new Error<string>($"Email '{email}' is already registered.");

		// Create with a temporary hash first to get the ID, then update with final hash
		// (We need the ID to salt the password, but we need the password to create)
		var tempHash = passwordService.HashPassword($"account:pending:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", password);
		var account = await database.CreateAccountAsync(username, email, tempHash, ct);

		// Rehash with the proper key now that we have the ID
		var realHash = passwordService.HashPassword(AccountKey(account), password);
		await database.UpdateAccountPasswordAsync(account.Id!, realHash, ct);
		account.PasswordHash = realHash;

		return account;
	}

	public async ValueTask<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
		=> await database.GetAccountByUsernameAsync(username, ct) is not null;

	public async ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default)
		=> await database.GetAccountByEmailAsync(email, ct) is not null;

	public ValueTask ForcePasswordChangeAsync(string accountId, CancellationToken ct = default)
		=> database.UpdateAccountMustChangePasswordAsync(accountId, true, ct);

	public async ValueTask<OneOf<Success, Error<string>>> ChangePasswordAsync(string accountId, string oldPassword, string newPassword, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		if (!passwordService.PasswordIsValid(AccountKey(account), oldPassword, account.PasswordHash))
			return new Error<string>("Current password is incorrect.");

		var newHash = passwordService.HashPassword(AccountKey(account), newPassword);
		await database.UpdateAccountPasswordAsync(accountId, newHash, ct);
		await database.UpdateAccountMustChangePasswordAsync(accountId, false, ct);
		return new Success();
	}

	public async ValueTask<OneOf<Success, Error<string>>> ChangeEmailAsync(string accountId, string? newEmail, string currentPassword, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		if (!passwordService.PasswordIsValid(AccountKey(account), currentPassword, account.PasswordHash))
			return new Error<string>("Current password is incorrect.");

		if (newEmail is not null && await database.GetAccountByEmailAsync(newEmail, ct) is not null)
			return new Error<string>($"Email '{newEmail}' is already registered.");

		await database.UpdateAccountEmailAsync(accountId, newEmail, ct);
		return new Success();
	}

	public async ValueTask<OneOf<Success, Error<string>>> ChangeUsernameAsync(string accountId, string newUsername, CancellationToken ct = default)
	{
		if (await database.GetAccountByUsernameAsync(newUsername, ct) is not null)
			return new Error<string>($"Username '{newUsername}' is already taken.");

		await database.UpdateAccountUsernameAsync(accountId, newUsername, ct);
		return new Success();
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

	public ValueTask<SharpAccount?> GetByUsernameAsync(string username, CancellationToken ct = default)
		=> database.GetAccountByUsernameAsync(username, ct);

	public ValueTask<SharpAccount?> GetByEmailAsync(string email, CancellationToken ct = default)
		=> database.GetAccountByEmailAsync(email, ct);

	public async ValueTask<OneOf<Success, Error<string>>> DisableAccountAsync(string accountId, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		await database.UpdateAccountDisabledAsync(accountId, true, ct);
		await accountSessionStore.RevokeAllForAccountAsync(accountId, ct);
		return new Success();
	}

	public ValueTask DeleteAccountAsync(string accountId, CancellationToken ct = default)
		=> database.DeleteAccountAsync(accountId, ct);

	public async ValueTask<OneOf<Success, Error<string>>> SetPasswordAsync(string accountId, string newPassword, bool mustChangePassword, CancellationToken ct = default)
	{
		var account = await database.GetAccountByIdAsync(accountId, ct);
		if (account is null)
			return new Error<string>("Account not found.");

		var newHash = passwordService.HashPassword(AccountKey(account), newPassword);
		await database.UpdateAccountPasswordAsync(accountId, newHash, ct);
		await database.UpdateAccountMustChangePasswordAsync(accountId, mustChangePassword, ct);
		return new Success();
	}

	public ValueTask<SharpAccount> CreateUnclaimedAccountAsync(string username, CancellationToken ct = default)
		=> database.CreateAccountAsync(username, null, string.Empty, ct);

	public async ValueTask<OneOf<Success, Error<string>>> EnableAccountAsync(string accountId, CancellationToken ct = default)
	{
		if (await database.GetAccountByIdAsync(accountId, ct) is null)
			return new Error<string>("Account not found.");
		await database.UpdateAccountDisabledAsync(accountId, false, ct);
		return new Success();
	}

	public ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken ct = default)
		=> database.GetAllAccountsAsync(ct);
}
