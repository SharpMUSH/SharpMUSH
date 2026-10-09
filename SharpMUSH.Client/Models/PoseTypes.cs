namespace SharpMUSH.Client.Models;

/// <summary>
/// One pose type as <c>GET api/scenes/types</c> sends it (the Scene plugin's <c>PoseType</c>): which layout the
/// portal draws its poses in, the theme colour it takes, its icon, and whether a reader who has not chosen starts
/// with it hidden.
/// </summary>
/// <param name="Key">The type's key, lower case: what a pose records (<c>ic</c>, <c>ooc</c>, ...).</param>
/// <param name="Label">Its name in the Show menu and the admin page.</param>
/// <param name="Presentation">One of <see cref="PoseTypeInfo.Presentations"/>.</param>
/// <param name="Tone">A theme colour's name (<see cref="PoseTypeInfo.Tones"/>), or empty for the text colour.</param>
/// <param name="Icon">One of <see cref="PoseTypeInfo.Icons"/>, or empty for none.</param>
/// <param name="Hidden">Whether a reader who has not chosen starts with this type hidden.</param>
/// <param name="Order">Its place in lists, lowest first.</param>
public sealed record PoseTypeInfo(string Key, string Label, string Presentation, string Tone, string Icon, bool Hidden, int Order)
{
	/// <summary>The type a pose has when none is given, and that a pose stored before types has.</summary>
	public const string InCharacter = "ic";

	/// <summary>The type the <c>ooc</c> command records.</summary>
	public const string OutOfCharacter = "ooc";

	public const string Prose = "prose";
	public const string Band = "band";
	public const string Message = "message";
	public const string Aside = "aside";
	public const string Notice = "notice";

	/// <summary>The portal layouts a type can take, as the plugin accepts them.</summary>
	public static readonly IReadOnlyList<string> Presentations = [Prose, Band, Message, Aside, Notice];

	/// <summary>The theme colours a type can take (<c>ToneMarkup</c>'s roles), as the plugin accepts them.</summary>
	public static readonly IReadOnlyList<string> Tones =
		["foreground", "primary", "secondary", "tertiary", "muted", "success", "warning", "error", "info"];

	/// <summary>The icons a type can show, as the plugin accepts them.</summary>
	public static readonly IReadOnlyList<string> Icons =
		["radio", "phone", "chat", "dice", "book", "scroll", "megaphone", "eye", "mask", "music", "star", "bolt"];

	/// <summary>A type the catalogue does not list: drawn as in character, named by its key.</summary>
	public static PoseTypeInfo Unlisted(string key) => new(key, key, Prose, "", "", false, 50);

	/// <summary>
	/// The presentation the portal draws: what the catalogue said, or prose for a value this portal does not
	/// know (a newer server's), so the pose still shows.
	/// </summary>
	public string Layout => Presentations.Contains(Presentation) ? Presentation : Prose;
}

/// <summary>A <c>TYPE`</c> attribute on the Scene Logger the plugin left out, and why.</summary>
public sealed record PoseTypeProblem(string Key, string Reason);

/// <summary>
/// <c>GET api/scenes/types</c>: the types in display order, the attributes that did not read as one, and the types
/// the caller's character hides (or, for nobody, the ones that start hidden).
/// </summary>
public sealed record PoseTypeCatalogue(IReadOnlyList<PoseTypeInfo> Types, IReadOnlyList<PoseTypeProblem> Problems, IReadOnlyList<string> Hidden)
{
	public static readonly PoseTypeCatalogue Empty = new([], [], []);
}
