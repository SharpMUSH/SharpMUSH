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
		=> user?.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit) == true
			&& IsOwnOrStaff(user, ownCharacters, characterName);

	/// <summary>
	/// The viewer's account owns the character, or the viewer is staff (Royalty and up): who may write its
	/// biography, given the wiki permission for the action.
	/// </summary>
	public static bool IsOwnOrStaff(ClaimsPrincipal? user, IEnumerable<AccountAuthService.CharacterSummary> ownCharacters, string characterName)
		=> user?.Identity?.IsAuthenticated == true
			&& (PortalRoleHelper.Meets(user, PortalRole.Royalty)
				|| ownCharacters.Any(c => string.Equals(c.Name, characterName, StringComparison.OrdinalIgnoreCase)));
}
