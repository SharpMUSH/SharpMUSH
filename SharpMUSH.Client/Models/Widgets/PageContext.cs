namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Per-character context cascaded into the widgets placed in the <c>"profile"</c> layout scope. A
/// profile layout is one shared arrangement of widgets; the character it renders for comes from the
/// route. Widgets read this via <c>[CascadingParameter]</c> instead of taking a direct parameter, so
/// they work unchanged whether the page positions them directly or an admin places them through the
/// layout editor.
/// </summary>
/// <param name="CharacterName">The character whose profile is being viewed (from the route).</param>
/// <param name="CanEdit">Whether the current viewer may edit this character's profile content.</param>
public record ProfilePageContext(string CharacterName, bool CanEdit);
