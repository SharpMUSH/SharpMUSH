using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountStore"/>: web accounts and character links. Not ported yet — every member throws
/// <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask<SharpAccount?> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAccount?> GetAccountByUsernameAsync(string username, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAccount?> GetAccountByIdAsync(string accountId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAccount> CreateAccountAsync(string username, string? email, string hashedPassword, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateAccountPasswordAsync(string accountId, string newHash, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateAccountMustChangePasswordAsync(string accountId, bool value, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateAccountEmailAsync(string accountId, string? newEmail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateAccountUsernameAsync(string accountId, string newUsername, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask LinkCharacterToAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UnlinkCharacterFromAccountAsync(string accountId, DBRef characterRef, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<IReadOnlyList<SharpPlayer>> GetCharactersForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAccount?> GetAccountForCharacterAsync(DBRef characterRef, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<IReadOnlyList<SharpAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
