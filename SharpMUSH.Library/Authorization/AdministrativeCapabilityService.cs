using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Trusted entry points construct this identity from authenticated session state, never request
/// fields. Queued work persists the original identity and rechecks it at execution. A callback
/// or owned thing must not substitute its owner as executor. No granted scopes are captured.
/// </summary>
public sealed record CapabilityActor(string AccountId, DBRef? ActiveCharacter = null, DBRef? Executor = null);

public interface IAdministrativeCapabilityService
{
	Task<bool> AuthorizeAsync(CapabilityActor actor, string scope, CancellationToken ct = default);
	Task<CapabilityActor?> GetGameActorAsync(DBRef executor, CancellationToken ct = default);
	Task<IReadOnlySet<string>> GetGrantedScopesAsync(CapabilityActor actor, CancellationToken ct = default);
	Task<IReadOnlyDictionary<string, PermissionExplanation>> ExplainAsync(CapabilityActor actor, CancellationToken ct = default);
}

/// <summary>
/// Fresh, persisted portal roles for web, game, queue and plugin gates. Resource ownership and
/// locks remain the consuming operation's responsibility. Account grants apply only to an
/// explicitly linked, active player executing as itself, never transitively to owned objects.
/// </summary>
public sealed class AdministrativeCapabilityService(
	IAccountService accounts,
	IRoleRegistryService registry,
	IRoleDerivationService derivation,
	IPermissionResolver resolver) : IAdministrativeCapabilityService
{
	/// <summary>Pass the actual executing player objid, never its owner or enactor.</summary>
	public async Task<CapabilityActor?> GetGameActorAsync(DBRef executor, CancellationToken ct = default)
	{
		if (!executor.IsObjid) return null;
		var account = await accounts.GetAccountForCharacterAsync(executor, ct);
		return account is { IsActive: true, Id: not null } ? new(account.Id, executor, executor) : null;
	}

	public async Task<bool> AuthorizeAsync(CapabilityActor actor, string scope, CancellationToken ct = default)
		=> PortalPermission.IsKnown(scope) && (await GetGrantedScopesAsync(actor, ct)).Contains(scope);

	public async Task<IReadOnlySet<string>> GetGrantedScopesAsync(CapabilityActor actor, CancellationToken ct = default)
		=> resolver.Resolve(await GetRolesAsync(actor, ct));

	public async Task<IReadOnlyDictionary<string, PermissionExplanation>> ExplainAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		var roles = await GetRolesAsync(actor, ct);
		return PortalPermission.AllScopes.ToDictionary(scope => scope, scope => new PermissionResolver().Explain(roles, scope));
	}

	private async Task<IReadOnlyCollection<SharpRole>> GetRolesAsync(CapabilityActor actor, CancellationToken ct)
	{
		SharpRole[] denied = [];
		var account = await accounts.GetByIdAsync(actor.AccountId, ct);
		if (account is null || account.Status != AccountStatus.Active)
			return denied;
		if (actor.Executor != actor.ActiveCharacter)
			return denied;
		var characters = await accounts.GetCharactersAsync(actor.AccountId, ct);
		if (actor.ActiveCharacter is { } active)
		{
			// Exact DBRef equality includes creation identity, so recycled dbrefs cannot inherit authority.
			characters = characters.Where(c => c.Object.DBRef == active).ToArray();
			if (characters.Count != 1)
				return denied;
		}
		var role = PortalRole.Guest;
		foreach (var character in characters)
		{
			var current = derivation.DeriveRole(character.Object.Key,
				await character.Object.Flags.Value.ToListAsync(ct));
			if (current > role) role = current;
		}
		var effective = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		var builtIn = await registry.GetRoleAsync(BuiltInRoles.SlugFor(role));
		if (builtIn.IsT0) effective[builtIn.AsT0.Slug] = builtIn.AsT0;
		foreach (var assigned in await registry.GetRolesForAccountAsync(actor.AccountId, ct))
			effective[assigned.Slug] = assigned;
		return effective.Values;
	}
}
