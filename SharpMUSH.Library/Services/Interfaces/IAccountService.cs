using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IAccountService
{
	/// <summary>
	/// Attempts to authenticate against the full login matrix. <paramref name="usernameOrEmail"/> may be:
	/// an account username or email, matched against that account's own password, or against the
	/// password of any character linked to the account; or a character name (no linked account of its
	/// own), matched only against that specific character's password, resolving to the character's
	/// owning account. Legacy PennMUSH character password hashes are transparently rehashed on success.
	/// An account with an empty stored password hash (e.g. the pre-generated, unclaimed admin account)
	/// can never match at the account level — only a linked character's own password can authenticate it.
	/// Returns the account on success; <see cref="AccountUnavailable"/> when the password matched an
	/// account that is disabled, banned or closed; <see cref="NotFound"/> otherwise, a deleted account
	/// included.
	/// </summary>
	ValueTask<AccountSignIn> AuthenticateAsync(string usernameOrEmail, string password, CancellationToken ct = default);

	/// <summary>Why <paramref name="account"/>, which is not active, may not sign in.</summary>
	ValueTask<AccountUnavailable> UnavailableAsync(SharpAccount account, CancellationToken ct = default);

	/// <summary>Returns true if at least one account exists.</summary>
	ValueTask<bool> HasAnyAccountAsync(CancellationToken ct = default);

	/// <summary>
	/// Creates a new account. <paramref name="email"/> is optional (pass null to omit).
	/// Returns an <see cref="Error{T}"/> if the username or email is already taken.
	/// </summary>
	ValueTask<Result<SharpAccount>> CreateAccountAsync(string username, string? email, string password, CancellationToken ct = default);

	ValueTask<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

	ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default);

	ValueTask ForcePasswordChangeAsync(string accountId, CancellationToken ct = default);

	/// <summary>
	/// Changes the account password. Returns an error if the account is not found or the old password is wrong.
	/// </summary>
	ValueTask<Result<Success>> ChangePasswordAsync(string accountId, string oldPassword, string newPassword, CancellationToken ct = default);

	/// <summary>Adds, changes, or clears the email. Pass null to remove the email.</summary>
	ValueTask<Result<Success>> ChangeEmailAsync(string accountId, string? newEmail, string currentPassword, CancellationToken ct = default);

	/// <summary>
	/// Changes the username. Returns an error if the new username is already taken.
	/// </summary>
	ValueTask<Result<Success>> ChangeUsernameAsync(string accountId, string newUsername, CancellationToken ct = default);

	ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersAsync(string accountId, CancellationToken ct = default);

	ValueTask LinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default);

	/// <summary>
	/// Links an existing character to the account once the holder proves they own it: with the character's
	/// own password, or, for a character on another account, with that account's password, which moves the
	/// character here. A character on another account cannot be taken with its own password alone. A
	/// character with no password on no account cannot be claimed, since nothing proves who owns it; staff
	/// link those with <see cref="AttachCharacterAsync"/>. Claiming a character the account already holds
	/// succeeds and changes nothing. Legacy PennMUSH hashes are rehashed on success.
	/// </summary>
	ValueTask<CharacterClaim> ClaimCharacterAsync(string accountId, string characterName, string password, CancellationToken ct = default);

	/// <summary>
	/// Links <paramref name="character"/> to the account on staff's say-so, with no password. Refused
	/// while another account holds the character; that account has to let it go first.
	/// </summary>
	ValueTask<CharacterLink> AttachCharacterAsync(string accountId, SharpPlayer character, CancellationToken ct = default);

	ValueTask UnlinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByIdAsync(string accountId, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByUsernameAsync(string username, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByEmailAsync(string email, CancellationToken ct = default);

	ValueTask<Result<Success>> DisableAccountAsync(string accountId, CancellationToken ct = default);
	/// <summary>
	/// Sets the account's lifecycle status. Accounts are never removed, so this is how an account is
	/// disabled, closed, deleted, or restored. Any transition away from
	/// <see cref="AccountStatus.Active"/> revokes live sessions.
	/// Returns an error if the account is not found, or if it is the reserved system account.
	/// </summary>
	ValueTask<Result<Success>> SetAccountStatusAsync(string accountId, AccountStatus status, CancellationToken ct = default);

	/// <summary>Marks the account closed — the holder has left. Reversible by an admin.</summary>
	ValueTask<Result<Success>> CloseAccountAsync(string accountId, CancellationToken ct = default);

	/// <summary>Marks the account deleted. The document is retained; see <see cref="AccountStatus"/>.</summary>
	ValueTask<Result<Success>> MarkAccountDeletedAsync(string accountId, CancellationToken ct = default);

	/// <summary>
	/// Returns the reserved system account, creating it if absent. Idempotent — safe to call on
	/// every startup.
	/// </summary>
	ValueTask<SharpAccount> GetOrCreateSystemAccountAsync(CancellationToken ct = default);

	/// <summary>Admin/setup password set: no old-password proof. Optionally flags MustChangePassword.</summary>
	ValueTask<Result<Success>> SetPasswordAsync(string accountId, string newPassword, bool mustChangePassword, CancellationToken ct = default);

	/// <summary>
	/// Creates an account with an EMPTY password hash (unclaimed — cannot be logged into
	/// until a password is set). Used by BootstrapService for the pre-generated admin.
	/// </summary>
	ValueTask<SharpAccount> CreateUnclaimedAccountAsync(string username, CancellationToken ct = default);

	ValueTask<Result<Success>> EnableAccountAsync(string accountId, CancellationToken ct = default);

	/// <summary>
	/// Bans the account: disables it, keeps the ban's reason, author and expiry, and revokes its sessions
	/// and live connections as a disable does. Replaces any ban it already had.
	/// Returns an error if the account is not found, or if it is the reserved system account.
	/// </summary>
	ValueTask<Result<Success>> BanAsync(AccountBan ban, CancellationToken ct = default);

	/// <summary>Lifts the account's ban and makes it active again; <see cref="NotFound"/> when it had none.</summary>
	ValueTask<Found<None>> LiftBanAsync(string accountId, CancellationToken ct = default);

	/// <summary>Lifts every ban that has run out by <paramref name="now"/>, and returns them.</summary>
	ValueTask<IReadOnlyList<AccountBan>> LiftExpiredBansAsync(DateTimeOffset now, CancellationToken ct = default);

	/// <summary>The account's ban, or null when it is not banned.</summary>
	ValueTask<AccountBan?> GetBanAsync(string accountId, CancellationToken ct = default);

	/// <summary>Every ban.</summary>
	ValueTask<IReadOnlyList<AccountBan>> GetBansAsync(CancellationToken ct = default);

	ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken ct = default);
}
