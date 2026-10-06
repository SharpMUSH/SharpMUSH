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

	/// <summary>
	/// Links <paramref name="characterRef"/> to the account, unless another account already holds it. The check
	/// and the write are one transaction, so of two accounts linking the same character at once exactly one wins.
	/// </summary>
	/// <returns>The account that holds the character instead, or null when it is now (or already was) this account's.</returns>
	ValueTask<SharpAccount?> LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default);

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
	/// <remarks>Any ban the account held is removed with it: the new status is what holds now.</remarks>
	ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default);

	/// <summary>
	/// Bans the account: sets it <see cref="AccountStatus.Disabled"/> and keeps <paramref name="ban"/>, in
	/// one write, replacing any ban it held. False when there is no such account.
	/// </summary>
	ValueTask<bool> BanAccountAsync(AccountBan ban, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lifts the account's ban: sets it <see cref="AccountStatus.Active"/> and removes the ban, in one
	/// write. With <paramref name="expiredBy"/>, only a ban that had run out by then is lifted, so a ban
	/// renewed after it was read is left alone. False when there was no ban to lift.
	/// </summary>
	ValueTask<bool> LiftAccountBanAsync(string accountId, DateTimeOffset? expiredBy = null,
		CancellationToken cancellationToken = default);

	/// <summary>The account's ban, or null when it is not banned.</summary>
	ValueTask<AccountBan?> GetAccountBanAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>Every ban. Admin tooling only — bans are few.</summary>
	ValueTask<IReadOnlyList<AccountBan>> GetAccountBansAsync(CancellationToken cancellationToken = default);

	/// <summary>Returns all accounts. Admin tooling only — account counts are small.</summary>
	ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default);
}
