using System.Security.Claims;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Pages;

/// <summary>
/// Who sees a profile's edit controls (the gallery's Add image, Make portrait, banner and Delete): a
/// holder of <c>wiki.edit</c> whose account owns the character, or who is staff (Royalty and up), whom
/// the server lets control every character. The server still decides every write; this only keeps a
/// visitor from being offered controls that would be refused.
/// </summary>
public static class ProfileEditRights
{
	public static bool CanEdit(ClaimsPrincipal? user, IEnumerable<AccountAuthService.CharacterSummary> ownCharacters, string characterName)
	{
		if (user?.Identity?.IsAuthenticated != true || !user.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit))
		{
			return false;
		}

		return PortalRoleHelper.Meets(user, PortalRole.Royalty)
			|| ownCharacters.Any(c => string.Equals(c.Name, characterName, StringComparison.OrdinalIgnoreCase));
	}
}
