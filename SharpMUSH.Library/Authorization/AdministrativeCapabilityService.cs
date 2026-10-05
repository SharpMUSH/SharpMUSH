using SharpMUSH.Library.DiscriminatedUnions;
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

	/// <summary>What a game object holds and is granted, read fresh rather than from the object cache.</summary>
	Task<ObjectGrants> GetObjectGrantsAsync(AnySharpObject obj, CancellationToken ct = default);
}

/// <summary>
/// Fresh, persisted roles for web, game, queue and plugin gates. Resource ownership and locks remain
/// the consuming operation's responsibility.
/// <list type="bullet">
/// <item>Playing a character, an account holds what that character holds: the character's own roles
/// and overrides, and the account's (see <see cref="ObjectGrants"/>).</item>
/// <item>With no character chosen, it holds the account's roles and overrides, every role assigned to
/// one of its characters, and <c>player</c> when one of them is not a guest. Overrides set on a
/// character apply only while playing it.</item>
/// </list>
/// Account grants reach only an explicitly linked, active player executing as itself, never owned
/// objects.
/// </summary>
public sealed class AdministrativeCapabilityService(
	IAccountService accounts,
	IRoleRegistryService registry,
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
		=> (await GetGrantedScopesAsync(actor, ct)).Contains(scope);

	public async Task<IReadOnlySet<string>> GetGrantedScopesAsync(CapabilityActor actor, CancellationToken ct = default)
		=> resolver.Resolve(await GetContextAsync(actor, ct));

	public async Task<IReadOnlyDictionary<string, PermissionExplanation>> ExplainAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		var context = await GetContextAsync(actor, ct);
		return PortalPermission.AllScopes.Concat(context.CustomScopes).ToDictionary(scope => scope, scope => resolver.Explain(context, scope));
	}

	public async Task<ObjectGrants> GetObjectGrantsAsync(AnySharpObject obj, CancellationToken ct = default)
		=> await ObjectGrantsReader.ReadAsync(registry, accounts.GetAccountForCharacterAsync, obj.Object().Key, obj.IsPlayer, ct);

	public async Task<PermissionContext> GetContextAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		var account = await accounts.GetByIdAsync(actor.AccountId, ct);
		if (account?.Id is null || account.Status != AccountStatus.Active)
			return PermissionContext.None;
		if (actor.Executor != actor.ActiveCharacter)
			return PermissionContext.None;
		var characters = await accounts.GetCharactersAsync(account.Id, ct);
		var all = await registry.GetRolesAsync(ct);
		var custom = await registry.GetCustomPermissionsAsync(ct);
		var accountGrants = new AccountGrants(
			await registry.GetRolesForAccountAsync(account.Id, ct),
			await registry.GetAccountOverridesAsync(account.Id, ct));

		if (actor.ActiveCharacter is { } active)
		{
			// Exact DBRef equality includes creation identity, so recycled dbrefs cannot inherit authority.
			if (characters.Where(c => c.Object.DBRef == active).ToArray() is not [var character])
				return PermissionContext.None;
			var number = character.Object.Key;
			return ObjectGrants.For(number, true, all,
				await registry.GetObjectRolesAsync(number, ct),
				await registry.GetObjectOverridesAsync(number, ct),
				accountGrants, custom).Context;
		}

		var characterRoles = new List<IReadOnlyList<string>>();
		foreach (var character in characters)
			characterRoles.Add(await registry.GetObjectRolesAsync(character.Object.Key, ct));
		return AccountContext(all, accountGrants, characters, characterRoles) with
		{
			CustomScopes = custom.Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase)
		};
	}

	private static PermissionContext AccountContext(IReadOnlyList<SharpRole> all, AccountGrants account,
		IReadOnlyList<SharpPlayer> characters, IReadOnlyList<IReadOnlyList<string>> characterRoles)
	{
		var bySlug = all.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);
		var held = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);

		void Hold(string slug)
		{
			if (bySlug.TryGetValue(slug, out var role)) held[role.Slug] = role;
		}

		Hold(BuiltInRoles.EveryoneSlug);
		foreach (var role in account.Roles.Where(r => !BuiltInRoles.IsImplicit(r.Slug))) held[role.Slug] = role;
		foreach (var slug in characterRoles.SelectMany(roles => roles).Where(slug => !BuiltInRoles.IsImplicit(slug))) Hold(slug);
		var accountGuest = account.Roles.Any(r => r.Slug == BuiltInRoles.GuestSlug);
		if (!accountGuest && characterRoles.Any(roles => !roles.Contains(BuiltInRoles.GuestSlug, StringComparer.OrdinalIgnoreCase)))
			Hold(BuiltInRoles.PlayerSlug);
		var owner = characters.Any(c => c.Object.Key == 1);
		if (owner) Hold(BuiltInRoles.GodSlug);

		return new PermissionContext(held.Values.ToArray(), account.Overrides, owner);
	}
}
