namespace MarkupString.Ansi;

/// <summary>
/// Distinguishes a link that runs a MUSH command when clicked (e.g. <c>help topic</c>) from one
/// that navigates to a URL. <see cref="Url"/> is value 0 and therefore the default, so markup that
/// carries no explicit kind deserialises to navigation behaviour.
/// </summary>
public enum LinkKind
{
	/// <summary>The link navigates to <see cref="AnsiStyle.LinkUrl"/>.</summary>
	Url = 0,

	/// <summary>The link sends <see cref="AnsiStyle.LinkUrl"/> to the game as a command.</summary>
	Command = 1
}
