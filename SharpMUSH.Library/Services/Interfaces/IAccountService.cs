using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IAccountService
{
	/// <summary>
	/// Attempts to authenticate using either a display name or email plus password.
	/// Returns the account on success, or <c>null</c> on auth failure / disabled / not found.
	/// </summary>
	ValueTask<SharpAccount?> AuthenticateAsync(string displayNameOrEmail, string password, CancellationToken ct = default);

	/// <summary>
	/// Creates a new account. <paramref name="email"/> is optional (pass null to omit).
	/// Returns an <see cref="Error{T}"/> if the display name or email is already taken.
	/// </summary>
	ValueTask<OneOf<SharpAccount, Error<string>>> CreateAccountAsync(string displayName, string? email, string password, CancellationToken ct = default);

	ValueTask<bool> DisplayNameExistsAsync(string displayName, CancellationToken ct = default);

	ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default);

	/// <summary>
	/// Changes the account password. Returns an error if the account is not found or the old password is wrong.
	/// </summary>
	ValueTask<OneOf<Success, Error<string>>> ChangePasswordAsync(string accountId, string oldPassword, string newPassword, CancellationToken ct = default);

	/// <summary>Adds, changes, or clears the email. Pass null to remove the email.</summary>
	ValueTask<OneOf<Success, Error<string>>> ChangeEmailAsync(string accountId, string? newEmail, string currentPassword, CancellationToken ct = default);

	/// <summary>
	/// Changes the display name. Returns an error if the new name is already taken.
	/// </summary>
	ValueTask<OneOf<Success, Error<string>>> ChangeDisplayNameAsync(string accountId, string newDisplayName, CancellationToken ct = default);

	ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersAsync(string accountId, CancellationToken ct = default);

	ValueTask LinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default);

	ValueTask UnlinkCharacterAsync(string accountId, DBRef characterRef, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByIdAsync(string accountId, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByDisplayNameAsync(string displayName, CancellationToken ct = default);

	ValueTask<SharpAccount?> GetByEmailAsync(string email, CancellationToken ct = default);

	// Admin operations
	ValueTask<OneOf<Success, Error<string>>> DisableAccountAsync(string accountId, CancellationToken ct = default);
	ValueTask DeleteAccountAsync(string accountId, CancellationToken ct = default);
}
