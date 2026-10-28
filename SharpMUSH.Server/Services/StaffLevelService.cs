using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>The staff roles the administrator chose in the setup wizard (expanded server data).</summary>
public sealed class StaffLevelChoice
{
	public string Set { get; set; } = StaffLevelSets.SharpMUSH;
}

/// <summary>
/// The setup wizard's choice of staff roles: SharpMUSH's starter roles (helper, moderator) or RhostMUSH's levels
/// (guildmaster, architect, councilor, immortal), beside the system roles both share. Choosing one set makes the roles
/// of it the game lacks and deletes the other set's roles nobody holds; a held one stays, and is reported. A game that
/// has not chosen uses RhostMUSH's levels when it has any of them, which is what a RhostMUSH import leaves.
/// </summary>
public sealed class StaffLevelService(
	IRoleRegistryService registry,
	IRoleManagementService management,
	IExpandedObjectDataService serverData)
{
	public async Task<StaffLevelsState> StateAsync(CancellationToken ct)
	{
		var roles = await RoleSlugsAsync(ct);
		var set = (await serverData.GetExpandedServerDataAsync<StaffLevelChoice>())?.Set
			?? (BuiltInRoles.RhostLevels.Any(level => roles.Contains(level.Slug)) ? StaffLevelSets.RhostMUSH : StaffLevelSets.SharpMUSH);
		return new StaffLevelsState(set, [.. Other(set).Select(role => role.Slug).Where(roles.Contains)]);
	}

	/// <summary>
	/// Makes the chosen set's missing roles as <paramref name="actor"/>, and deletes each role of the other set that no
	/// account or object holds. A role the game already has is left as it is. Fails naming each role that could not be
	/// made or deleted; the rest still were.
	/// </summary>
	public async Task<Result<Success>> ApplyAsync(CapabilityActor actor, string set, CancellationToken ct)
	{
		if (!StaffLevelSets.IsKnown(set)) return new Error<string>($"No such set of staff roles: {set}.");

		var roles = await RoleSlugsAsync(ct);
		var failures = new List<string>();
		foreach (var role in BuiltInRoles.StaffRolesOf(set).Where(role => !roles.Contains(role.Slug)))
		{
			var draft = new RoleDraft(role.Slug, role.Name, role.Category, role.Color, role.Priority, role.Permissions);
			if (await management.SaveRoleAsync(actor, draft, ct) is RoleRefusal refusal)
				failures.Add($"{role.Name}: {refusal.Message}");
		}

		foreach (var role in Other(set).Where(role => roles.Contains(role.Slug)))
		{
			if (await HeldAsync(role.Slug, ct)) continue;
			if (await management.DeleteRoleAsync(actor, role.Slug, ct) is RoleRefusal refusal)
				failures.Add($"{role.Name}: {refusal.Message}");
		}

		await serverData.SetExpandedServerDataAsync(new StaffLevelChoice { Set = set });
		return failures.Count == 0 ? new Success() : new Error<string>(string.Join(" ", failures));
	}

	private static IReadOnlyList<SharpRole> Other(string set)
		=> BuiltInRoles.StaffRolesOf(set == StaffLevelSets.RhostMUSH ? StaffLevelSets.SharpMUSH : StaffLevelSets.RhostMUSH);

	private async Task<HashSet<string>> RoleSlugsAsync(CancellationToken ct)
		=> (await registry.GetRolesAsync(ct)).Select(role => role.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

	private async Task<bool> HeldAsync(string slug, CancellationToken ct)
		=> (await registry.GetAccountIdsForRoleAsync(slug)).Count > 0 || (await registry.GetObjectsForRoleAsync(slug, ct)).Count > 0;
}
