using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Resolves the set of granted permission scopes from an account's effective roles using the
/// Discord-style priority/three-state rule.
/// </summary>
public interface IPermissionResolver
{
	/// <summary>
	/// For each <see cref="PortalPermission"/> scope, the highest-<see cref="SharpRole.Priority"/>
	/// role that explicitly sets Allow/Deny decides; on a same-priority tie an explicit Deny wins
	/// (fail closed); a scope nobody opts into is denied. Returns the granted scopes.
	/// </summary>
	IReadOnlySet<string> Resolve(IEnumerable<SharpRole> effectiveRoles);
}

/// <inheritdoc />
public sealed class PermissionResolver : IPermissionResolver
{
	public IReadOnlySet<string> Resolve(IEnumerable<SharpRole> effectiveRoles)
	{
		var roles = effectiveRoles as IReadOnlyCollection<SharpRole> ?? effectiveRoles.ToList();
		var granted = new HashSet<string>(StringComparer.Ordinal);

		foreach (var scope in PortalPermission.AllScopes)
		{
			// The highest-priority roles that express an opinion on this scope decide.
			var top = roles
				.Where(r => r.Permissions.TryGetValue(scope, out var state) && state != PermissionState.Inherit)
				.GroupBy(r => r.Priority)
				.OrderByDescending(g => g.Key)
				.FirstOrDefault();

			if (top is null)
				continue; // nobody opts in → default deny

			// Deny wins ties: only granted when every opinion at the top priority is Allow.
			if (top.All(r => r.Permissions[scope] == PermissionState.Allow))
				granted.Add(scope);
		}

		return granted;
	}
}
