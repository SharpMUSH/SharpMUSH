using Microsoft.AspNetCore.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authorization requirement satisfied when the caller carries a <c>perm</c> claim for the
/// named permission scope (see <see cref="SharpMUSH.Library.Authorization.PortalPermission"/>).
/// </summary>
public sealed class PermissionRequirement(string scope) : IAuthorizationRequirement
{
	public string Scope { get; } = scope;
}
