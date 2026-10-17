using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
namespace SharpMUSH.Server.Authentication;
/// <summary>Recovery reads target grants directly: inactive accounts retain their place in the hierarchy.</summary>
public sealed class AccountRecoveryHierarchy(IAccountService accounts, IRoleRegistryService registry, IAdministrativeCapabilityService capabilities)
{
	public async Task<FoundResult<SharpAccount>> AuthorizeAsync(string actorId, string targetId, CancellationToken ct = default)
	{
		if (await accounts.GetByIdAsync(targetId, ct) is not { } target) return new NotFound();
		if (await accounts.GetByIdAsync(actorId, ct) is not { IsActive: true }) return Refused();
		if (!await capabilities.AuthorizeAsync(new CapabilityActor(actorId), PortalPermission.PlayersModerate, ct))
			return new Error<string>("players.moderate required.");
		var actor = await capabilities.GetContextAsync(new CapabilityActor(actorId), ct);
		if (actor.IsOwner) return target;
		if (target.Id == actorId || SystemAccount.IsReserved(target.Username)) return Refused();
		var characters = await accounts.GetCharactersAsync(target.Id!, ct);
		if (characters.Any(character => character.Object.Key == 1)) return Refused();
		// Match the account hierarchy's implicit everyone/player rules while bypassing the active-account
		// filter of AdministrativeCapabilityService.GetContextAsync for this target only.
		var all = await registry.GetRolesAsync(ct);
		var bySlug = all.ToDictionary(role => role.Slug, StringComparer.OrdinalIgnoreCase);
		var accountRoles = await registry.GetRolesForAccountAsync(target.Id!, ct);
		var held = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		void Hold(string slug) { if (bySlug.TryGetValue(slug, out var role)) held[role.Slug] = role; }
		Hold(BuiltInRoles.EveryoneSlug);
		foreach (var role in accountRoles.Where(role => !BuiltInRoles.IsImplicit(role.Slug))) held[role.Slug] = role;
		var characterRoles = new List<IReadOnlyList<string>>();
		foreach (var character in characters)
		{
			var roles = await registry.GetObjectRolesAsync(character.Object.Key, ct); characterRoles.Add(roles);
			foreach (var slug in roles.Where(slug => !BuiltInRoles.IsImplicit(slug))) Hold(slug);
		}
		if (!accountRoles.Any(role => role.Slug.Equals(BuiltInRoles.GuestSlug, StringComparison.OrdinalIgnoreCase))
		 && characterRoles.Any(roles => !roles.Contains(BuiltInRoles.GuestSlug, StringComparer.OrdinalIgnoreCase))) Hold(BuiltInRoles.PlayerSlug);
		var targetContext = new PermissionContext(held.Values.ToArray(), new Dictionary<string, PermissionState>(), false);
		return RoleHierarchy.Outranks(actor, targetContext.TopPriority) ? target : Refused();
	}
	private static Error<string> Refused() => new("You may recover only another account below your highest role; owner and reserved accounts require the owner.");
}
