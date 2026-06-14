namespace SharpMUSH.Library.Authorization;

/// <summary>
/// A role's stance on a single permission scope. Discord-style three-state:
/// the highest-<see cref="Models.SharpRole.Priority"/> role that explicitly sets
/// <see cref="Allow"/> or <see cref="Deny"/> wins; roles left on <see cref="Inherit"/>
/// don't vote, and a scope nobody allows defaults to denied.
/// </summary>
public enum PermissionState
{
	/// <summary>Role expresses no opinion on this scope (the default).</summary>
	Inherit = 0,

	/// <summary>Role grants this scope.</summary>
	Allow = 1,

	/// <summary>Role explicitly forbids this scope.</summary>
	Deny = 2
}
