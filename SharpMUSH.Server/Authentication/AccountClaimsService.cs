using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Derives the account-level <see cref="PortalRole"/> and granted permission scopes for an
/// account. Extracted from <see cref="JwtService"/> so non-JWT callers (<c>AuthController</c>'s
/// account-login/register endpoints, <c>AdminAccountsController</c>'s Wizard gate) can compute
/// the same claims without depending on JWT being configured.
/// </summary>
public class AccountClaimsService(
	IAccountService accountService,
	IRoleDerivationService roleDerivation,
	IRoleRegistryService roleRegistry,
	IPermissionResolver permissionResolver,
	ILogger<AccountClaimsService> logger)
{
	/// <summary>
	/// The account-level flag-derived role: the highest <see cref="PortalRole"/> across every
	/// character the account owns (so a Wizard on any character lifts the whole account). Falls
	/// back to the active <paramref name="activeRole"/> if the character list can't be loaded, or
	/// if the account has no characters at all.
	/// Characters are resolved by stable key/dbref, so character renames never affect the result.
	/// </summary>
	public async Task<PortalRole> ComputeAccountRoleAsync(string accountId, PortalRole activeRole, CancellationToken ct = default)
	{
		try
		{
			var characters = await accountService.GetCharactersAsync(accountId, ct);
			if (characters.Count == 0)
				return activeRole;

			var perCharacter = new List<(int, IEnumerable<SharpObjectFlag>)>(characters.Count);
			foreach (var c in characters)
				perCharacter.Add((c.Object.Key, await c.Object.Flags.Value.ToListAsync(ct)));

			var accountRole = roleDerivation.DeriveAccountRole(perCharacter);
			return accountRole > activeRole ? accountRole : activeRole;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex,
				"Could not derive account-level role for account {AccountId}; using the active character's role.",
				accountId);
			return activeRole;
		}
	}

	/// <summary>
	/// Convenience overload for callers with no specific "active character" context (account
	/// login/register, admin gating): floors at <see cref="PortalRole.Guest"/>, the lowest
	/// <see cref="PortalRole"/>, so the result is simply the account's highest character-derived
	/// role, or Guest if it has no characters.
	/// </summary>
	public Task<PortalRole> ComputeAccountRoleAsync(string accountId, CancellationToken ct = default) =>
		ComputeAccountRoleAsync(accountId, PortalRole.Guest, ct);

	/// <summary>
	/// Computes the granted permission scopes for an account: the account's effective roles are
	/// the (current, possibly admin-edited) built-in role for its flag-derived <paramref name="role"/>
	/// unioned with its explicitly-assigned roles, resolved by priority/three-state.
	/// </summary>
	public async Task<IReadOnlySet<string>> ComputeGrantedScopesAsync(string accountId, PortalRole role)
	{
		var allRoles = await roleRegistry.GetRolesAsync();
		var bySlug = allRoles.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);

		var effective = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		if (bySlug.TryGetValue(BuiltInRoles.SlugFor(role), out var derived))
			effective[derived.Slug] = derived;
		foreach (var assigned in await roleRegistry.GetRolesForAccountAsync(accountId))
			effective[assigned.Slug] = assigned;

		// Expand umbrella scopes (e.g. wiki.admin ⇒ wiki.read/create/edit/delete) so the finer
		// gates authorize for holders of the coarser scope without per-gate "or admin" checks.
		return PortalPermission.Expand(permissionResolver.Resolve(effective.Values));
	}
}
