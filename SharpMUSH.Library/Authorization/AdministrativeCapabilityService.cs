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

	/// <summary>
	/// The roles, overrides and owner status that decide <paramref name="actor"/>'s permissions, read
	/// fresh. <see cref="PermissionContext.None"/> for a disabled account or a mismatched executor.
	/// </summary>
	Task<PermissionContext> GetContextAsync(CapabilityActor actor, CancellationToken ct = default);
}

/// <summary>
/// Fresh, persisted roles for web, game, queue and plugin gates. Resource ownership and
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
		=> resolver.Resolve(await GetContextAsync(actor, ct));

	public async Task<IReadOnlyDictionary<string, PermissionExplanation>> ExplainAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		var context = await GetContextAsync(actor, ct);
		return PortalPermission.AllScopes.ToDictionary(scope => scope, scope => resolver.Explain(context, scope));
	}

	public async Task<PermissionContext> GetContextAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		var account = await accounts.GetByIdAsync(actor.AccountId, ct);
		if (account?.Id is null || account.Status != AccountStatus.Active)
			return PermissionContext.None;
		if (actor.Executor != actor.ActiveCharacter)
			return PermissionContext.None;
		var characters = await accounts.GetCharactersAsync(account.Id, ct);
		if (actor.ActiveCharacter is { } active)
		{
			// Exact DBRef equality includes creation identity, so recycled dbrefs cannot inherit authority.
			characters = characters.Where(c => c.Object.DBRef == active).ToArray();
			if (characters.Count != 1)
				return PermissionContext.None;
		}

		var tier = PortalRole.Guest;
		foreach (var character in characters)
		{
			var current = await derivation.DeriveRoleAsync(character, ct);
			if (current > tier) tier = current;
		}

		var all = (await registry.GetRolesAsync(ct)).ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);
		var held = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		foreach (var builtIn in BuiltInRoles.TierSlugs(tier).Prepend(BuiltInRoles.EveryoneSlug).Where(all.ContainsKey).Select(slug => all[slug]))
			held[builtIn.Slug] = builtIn;
		foreach (var assigned in await registry.GetRolesForAccountAsync(account.Id, ct))
			held[assigned.Slug] = assigned;

		return new PermissionContext(held.Values.ToArray(),
			await registry.GetAccountOverridesAsync(account.Id, ct),
			IsOwner: tier == PortalRole.God);
	}
}
