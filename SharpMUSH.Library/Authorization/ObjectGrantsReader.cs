using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Reads what <see cref="ObjectGrants.For"/> needs from storage: the world's roles and custom permissions, the object's own
/// roles and overrides, and, for a character linked to an active account, the account's. Shared by the
/// cached query behind <see cref="SharpObject.Grants"/> and by a provider with no host cache to ask.
/// </summary>
public static class ObjectGrantsReader
{
	public static Task<ObjectGrants> ReadAsync(IRoleRegistryService roles, IAccountStore accounts,
		int number, bool isPlayer, CancellationToken cancellationToken)
		=> ReadAsync(roles, accounts.GetAccountForCharacterAsync, number, isPlayer, cancellationToken);

	/// <param name="roles">The role store.</param>
	/// <param name="accountFor">Finds the account a character is linked to.</param>
	/// <param name="number">The object's dbref number.</param>
	/// <param name="isPlayer">Whether the object is a player character.</param>
	/// <param name="cancellationToken">Cancels the reads.</param>
	public static async Task<ObjectGrants> ReadAsync(IRoleRegistryService roles,
		Func<DBRef, CancellationToken, ValueTask<SharpAccount?>> accountFor,
		int number, bool isPlayer, CancellationToken cancellationToken)
	{
		var all = await roles.GetRolesAsync(cancellationToken);
		var objectRoles = await roles.GetObjectRolesAsync(number, cancellationToken);
		var objectOverrides = await roles.GetObjectOverridesAsync(number, cancellationToken);
		AccountGrants? account = null;
		if (isPlayer && await accountFor(new DBRef(number), cancellationToken) is
			{ Status: AccountStatus.Active, Id: { } accountId })
		{
			account = new AccountGrants(
				await roles.GetRolesForAccountAsync(accountId, cancellationToken),
				await roles.GetAccountOverridesAsync(accountId, cancellationToken));
		}

		return ObjectGrants.For(number, isPlayer, all, objectRoles, objectOverrides, account,
			await roles.GetCustomPermissionsAsync(cancellationToken));
	}
}
