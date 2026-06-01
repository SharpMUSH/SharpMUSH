using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IAccountService
{
	/// <summary>
	/// Attempts to authenticate using either a display name or email plus password.
	/// Returns the account on success, null on failure.
	/// </summary>
	ValueTask<SharpAccount?> AuthenticateAsync(string displayNameOrEmail, string password, CancellationToken ct = default);

	/// <summary>
	/// Creates a new account. <paramref name="email"/> is optional (pass null to omit).
	/// Throws <see cref="InvalidOperationException"/> if display name or email already exist.
	/// </summary>
	ValueTask<SharpAccount> CreateAccountAsync(string displayName, string? email, string password, CancellationToken ct = default);

	ValueTask<bool> DisplayNameExistsAsync(string displayName, CancellationToken ct = default);

	ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default);

	ValueTask ChangePasswordAsync(string accountId, string oldPassword, string newPassword, CancellationToken ct = default);

	/// <summary>Adds, changes, or clears the email. Pass null to remove the email.</summary>
	ValueTask ChangeEmailAsync(string accountId, string? newEmail, string currentPassword, CancellationToken ct = default);

	ValueTask ChangeDisplayNameAsync(string accountId, string newDisplayName, CancellationToken ct = default);

	ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersAsync(string accountId, CancellationToken ct = default);

	ValueTask LinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default);

	ValueTask UnlinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByIdAsync(string accountId, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByDisplayNameAsync(string displayName, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByEmailAsync(string email, CancellationToken ct = default);

	// Admin operations
	ValueTask DisableAccountAsync(string accountId, CancellationToken ct = default);
	ValueTask DeleteAccountAsync(string accountId, CancellationToken ct = default);
}
