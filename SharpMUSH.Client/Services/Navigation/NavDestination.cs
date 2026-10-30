using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Services.Navigation;

/// <summary>Who is looking, as far as the navigation's gates need to know.</summary>
/// <param name="Role">The viewer's portal role.</param>
/// <param name="Holds">Whether the viewer holds a permission scope.</param>
/// <param name="LoggedIn">Signed in to an account.</param>
/// <param name="GuestsEnabled">The game accepts guest logins.</param>
/// <param name="CharacterActive">A character is connected in the command terminal.</param>
/// <param name="HasFeature">Whether the game has an optional application on (a <c>GameFeatures</c> id).</param>
public sealed record NavViewer(
	PortalRole Role,
	Func<string, bool> Holds,
	bool LoggedIn,
	bool GuestsEnabled,
	bool CharacterActive,
	Func<string, bool> HasFeature);

/// <summary>
/// One place the navigation can send the viewer: a core section, a page application or a staff-added link. It
/// carries its own gate, so wherever a layout puts it, it shows only to whoever may open it.
/// </summary>
/// <param name="Key">The <c>NavKeys</c> key a layout names it by.</param>
/// <param name="Href">Where it goes.</param>
/// <param name="OutlineIcon">The rail's icon.</param>
/// <param name="FillIcon">The drawer's and the tab bar's icon.</param>
/// <param name="Label">Its name, already localized.</param>
/// <param name="Under">The paths that mark it current, each with the pages beneath it.</param>
/// <param name="Hint">The group it asks to be in when no layout places it: a group's id or label.</param>
/// <param name="Order">Its place among the others hinting at the same group.</param>
/// <param name="Admits">Whether the viewer may see it.</param>
/// <param name="External">Opens another site, in a new tab.</param>
public sealed record NavDestination(
	string Key,
	string Href,
	string OutlineIcon,
	string FillIcon,
	string Label,
	IReadOnlyList<string> Under,
	string? Hint,
	int Order,
	Func<NavViewer, bool> Admits,
	bool External = false)
{
	/// <summary>Whether <paramref name="path"/> (an absolute path, no trailing slash) is this destination or beneath it.</summary>
	public bool IsCurrent(string path) => Under.Any(prefix =>
		path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
}
