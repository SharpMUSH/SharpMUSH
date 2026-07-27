using SharpMUSH.Library.Models;
using SharpMUSH.Server.Hubs;
using System.Security.Claims;

namespace SharpMUSH.Server.Authentication;

public static class CharacterClaimsExtensions
{
	/// <summary>
	/// The character this request acts as, from the <c>character_dbref</c> claim, or
	/// <see langword="null"/> when the principal carries no character or an unparseable one. Not
	/// <see cref="ClaimTypes.NameIdentifier"/>, which carries the account id.
	/// </summary>
	/// <remarks>
	/// Returns a <see cref="DBRef"/> rather than the raw claim string so callers cannot compare or
	/// format two different spellings of the same character. Handlers emit the claim as an objid,
	/// so the creation time is normally present and <see cref="DBRef.Matches"/> will reject a
	/// recycled dbref; a bare <c>"#N"</c> claim still parses, with a null creation time.
	/// </remarks>
	public static DBRef? GetActingCharacter(this ClaimsPrincipal user)
		=> user.FindFirst(GameHub.CharacterDbrefClaim)?.Value is { } claim
			&& DBRef.TryParse(claim, out var dbref)
				? dbref
				: null;
}
