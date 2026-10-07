using SharpMUSH.Client.Models;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Per-character context cascaded into the widgets placed in the <c>"profile"</c> layout scope. A
/// profile layout is one shared arrangement of widgets; the character it renders for comes from the
/// route. Widgets read this via <c>[CascadingParameter]</c> instead of taking a direct parameter, so
/// they work unchanged whether the page positions them directly or an admin places them through the
/// layout editor.
/// </summary>
/// <param name="CharacterName">The character whose profile is being viewed (from the route).</param>
/// <param name="CanEdit">Whether the current viewer may edit this character's profile content: the
/// account that owns the character, or staff.</param>
/// <param name="Dbref">The character's dbref once the page has resolved it, so widgets need not ask
/// the directory again; null until then.</param>
/// <param name="Gallery">The gallery as the page's own last write left it (its banner and avatar
/// controls), for a gallery widget to show; null until the page writes.</param>
/// <param name="GalleryChanged">Tells the page a widget changed the gallery, so the banner and the
/// avatar follow it.</param>
/// <param name="Owned">The viewer's account owns the character, so may write its biography without any
/// wiki permission.</param>
public record ProfilePageContext(
	string CharacterName,
	bool CanEdit,
	string? Dbref = null,
	IReadOnlyList<GalleryEntry>? Gallery = null,
	Action<IReadOnlyList<GalleryEntry>>? GalleryChanged = null,
	bool Owned = false);

/// <summary>
/// What the Play page offers the widgets in its <c>"play"</c> layout scope (README §7.4): opening a
/// character's sheet and sending a command down the play connection. A widget placed outside Play has
/// no context and falls back to the row's own command.
/// </summary>
/// <param name="OpenCharacter">Opens the character sheet for a room row.</param>
/// <param name="Send">Sends a command line through the play connection.</param>
/// <param name="InSheet">Rendered in the phone Room sheet: no card chrome, exits as rows, and the page's
/// exit keys stay with the aside.</param>
public record PlayPageContext(Func<RoomOccupant, Task> OpenCharacter, Func<string, Task> Send, bool InSheet = false);
