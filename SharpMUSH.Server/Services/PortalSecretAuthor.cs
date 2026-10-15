using System.Security.Claims;
using SharpMUSH.Library.API;

namespace SharpMUSH.Server.Services;

/// <summary>Who a secret stored through the portal is recorded as: the signed-in account.</summary>
public static class PortalSecretAuthor
{
	public static SecretAuthor Of(ClaimsPrincipal user)
		=> new(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
			user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
}
