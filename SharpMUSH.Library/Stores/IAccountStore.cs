using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Web accounts and the character links that tie them to players.
/// </summary>
public interface IAccountStore
{
	/// <summary>Finds an account by its unique email address. Returns null if not found or email is null.</summary>
	ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default);

	/// <summary>Finds an account by its unique username.</summary>
	ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default);

	/// <summary>Finds an account by its internal document ID (e.g. "node_accounts/123").</summary>
	ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>Returns true if at least one account exists in the database.</summary>
	ValueTask<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default);

	/// <summary>Creates a new account. Email is optional; pass null to omit.</summary>
	ValueTask<SharpAccount> CreateAccountAsync(string username, string? email, string hashedPassword, CancellationToken cancellationToken = default);

	ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default);

	ValueTask UpdateAccountMustChangePasswordAsync(string accountId, bool value, CancellationToken cancellationToken = default);

	/// <summary>Updates the account email. Pass null to clear the email.</summary>
	ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default);

	ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default);

	/// <summary>Creates a graph edge linking <paramref name="characterRef"/> to the account.</summary>
	ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default);

	/// <summary>Removes the graph edge linking <paramref name="characterRef"/> to the account.</summary>
	ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default);

	/// <summary>Returns all SharpPlayer characters linked to the given account.</summary>
	ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>Returns the account that owns <paramref name="characterRef"/>, or null if the character has no account.</summary>
	ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the account's lifecycle status. Account documents are never removed, so this is the
	/// only way an account leaves <see cref="AccountStatus.Active"/>.
	/// </summary>
	ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default);

	/// <summary>Returns all accounts. Admin tooling only — account counts are small.</summary>
	ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default);
}
