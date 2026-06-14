using Microsoft.AspNetCore.Authorization;

namespace SharpMUSH.Client.Authentication;

/// <summary>
/// Requires the caller to hold a specific portal permission scope (a <c>perm</c> claim).
/// </summary>
public sealed class PermissionRequirement(string scope) : IAuthorizationRequirement
{
	/// <summary>The permission scope string this requirement demands (e.g. <c>wiki.admin</c>).</summary>
	public string Scope { get; } = scope;
}
