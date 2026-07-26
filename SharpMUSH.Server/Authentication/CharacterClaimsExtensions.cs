using System.Security.Claims;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

public static class CharacterClaimsExtensions
{
	/// <summary>
	/// The dbref (<c>"#N"</c>) of the character this request acts as, from the <c>character_dbref</c>
	/// claim, or <see langword="null"/> when the principal carries no character. Not
	/// <see cref="ClaimTypes.NameIdentifier"/>, which carries the account id.
	/// </summary>
	public static string? GetActingCharacterDbref(this ClaimsPrincipal user)
		=> user.FindFirst(GameHub.CharacterDbrefClaim)?.Value;
}
