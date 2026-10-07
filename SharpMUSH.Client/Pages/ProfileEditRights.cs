using System.Security.Claims;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Pages;

/// <summary>
/// Who sees a profile's edit controls (the gallery's Add image, Make portrait, banner and Delete): the
/// account that owns the character, or staff (Royalty and up), whom the server lets control every
/// character. The server still decides every write; this only keeps a visitor from being offered
/// controls that would be refused.
/// </summary>
public static class ProfileEditRights
{
	public static bool CanEdit(ClaimsPrincipal? user, IEnumerable<AccountAuthService.CharacterSummary> ownCharacters, string characterName)
	{
		if (user?.Identity?.IsAuthenticated != true)
		{
			return false;
		}

		return PortalRoleHelper.Meets(user, PortalRole.Royalty) || Owns(user, ownCharacters, characterName);
	}

	/// <summary>
	/// Whether the signed-in account owns the character, whichever of its characters is acting. The server
	/// lets an account write and edit its own characters' biographies on that alone, with no wiki permission.
	/// </summary>
	public static bool Owns(ClaimsPrincipal? user, IEnumerable<AccountAuthService.CharacterSummary> ownCharacters, string characterName)
		=> user?.Identity?.IsAuthenticated == true
			&& ownCharacters.Any(c => string.Equals(c.Name, characterName, StringComparison.OrdinalIgnoreCase));
}
