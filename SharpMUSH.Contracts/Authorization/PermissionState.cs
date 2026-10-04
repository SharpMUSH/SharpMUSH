namespace SharpMUSH.Library.Authorization;

/// <summary>
/// A role's (or a per-account override's) stance on one permission scope, the three states of a
/// Discord permission overwrite. See <see cref="PermissionResolver"/> for how they combine.
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
