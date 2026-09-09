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
			if (Explain(roles, scope).Allowed)
				granted.Add(scope);
		}

		return granted;
	}
	/// <summary>
	/// Explicit child opinions resolve first by priority, with Deny winning ties. Only an
	/// unopinionated child inherits a granted umbrella. Thus even a higher umbrella cannot
	/// bypass a resolved child denial; an explicit higher child Allow can replace that denial.
	/// </summary>
	public PermissionExplanation Explain(IEnumerable<SharpRole> roles, string scope)
	{
		if (!PortalPermission.IsKnown(scope))
			return new(false, null, [], "unknown-scope");
		// Walked once per scope and again per implying parent, so a lazy sequence is pinned here — but
		// a caller that already holds a collection (including this method recursing) is not re-copied.
		var materialized = roles as IReadOnlyCollection<SharpRole> ?? roles.ToArray();
		var top = materialized.Where(r => r.Permissions.Any(p =>
			string.Equals(p.Key, scope, StringComparison.OrdinalIgnoreCase) && p.Value != PermissionState.Inherit))
			.GroupBy(r => r.Priority).OrderByDescending(g => g.Key).FirstOrDefault();
		if (top is not null)
			return new(top.All(r => r.Permissions.Where(p =>
				string.Equals(p.Key, scope, StringComparison.OrdinalIgnoreCase) && p.Value != PermissionState.Inherit)
				.All(p => p.Value == PermissionState.Allow)),
				top.Key, top.Select(r => r.Slug).Order().ToArray(), "explicit");
		var parents = PortalPermission.AllScopes.Where(parent => PortalPermission.ImpliedScopes(parent)
			.Contains(scope, StringComparer.OrdinalIgnoreCase));
		foreach (var parent in parents)
		{
			var decision = Explain(materialized, parent);
			if (decision.Allowed)
				return decision with { Reason = $"implied:{parent}" };
		}
		return new(false, null, [], "default-deny");
	}

}

public sealed record PermissionExplanation(bool Allowed, int? Priority, string[] Roles, string Reason);
