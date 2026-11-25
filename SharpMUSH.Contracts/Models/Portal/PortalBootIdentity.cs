namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// The game's identity as the server writes it into the portal's <c>index.html</c>, so the first render already
/// shows the game's own name, logos and guide rather than SharpMUSH's while <c>api/server-info</c> is on its way.
/// Each picture or name is empty when the game has none of its own.
/// </summary>
/// <param name="Name">The game's name (<c>mud_name</c>).</param>
/// <param name="Logo">The compact mark (<c>portal_logo</c>).</param>
/// <param name="LogoFull">The full logo (<c>portal_logo_full</c>, else <c>portal_logo</c>).</param>
/// <param name="GuideName">The guide's name (<c>onboarding_guide_name</c>).</param>
/// <param name="GuideArt">The guide's picture (<c>onboarding_guide_art</c>).</param>
public sealed record PortalBootIdentity(string Name, string Logo, string LogoFull, string GuideName, string GuideArt)
{
	/// <summary>The id of the <c>script type="application/json"</c> element in <c>index.html</c> that carries it.</summary>
	public const string ElementId = "sharp-identity";
}
