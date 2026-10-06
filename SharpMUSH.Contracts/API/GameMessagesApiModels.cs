namespace SharpMUSH.Library.API;

/// <summary>
/// A text the server shows a connection at a fixed point: PennMUSH's message files (<c>connect.txt</c>,
/// <c>motd.txt</c> and the rest), which SharpMUSH keeps in the database instead of on disk.
/// </summary>
public enum GameMessage
{
	/// <summary>The connect screen, shown to a connection arriving at the login prompt.</summary>
	Connect,

	/// <summary>Shown to every player after logging in.</summary>
	Motd,

	/// <summary>Shown after the MOTD to wizards and royalty.</summary>
	WizMotd,

	/// <summary>Shown to a guest after the MOTD.</summary>
	Guest,

	/// <summary>Shown after <c>register</c> creates an account.</summary>
	NewUser,

	/// <summary>Shown instead of creating a character or account while player creation is off.</summary>
	Register,

	/// <summary>Shown before the refusal while logins are off.</summary>
	Down,

	/// <summary>Shown after the goodbye on <c>QUIT</c> and <c>LOGOUT</c>.</summary>
	Quit
}

/// <summary>Where the game reads its messages from.</summary>
public enum GameMessageSource
{
	/// <summary>The text stored in the database, edited on the Messages page.</summary>
	Stored,

	/// <summary>An attribute on the Messages object, evaluated as softcode; the stored text where it has none.</summary>
	Object
}

/// <summary>Names shared by the server, the bundled Messages package and the portal.</summary>
public static class GameMessages
{
	/// <summary>The bundled package that creates the Messages object.</summary>
	public const string PackageId = "messages";

	/// <summary>The Messages object's ref in that package's manifest.</summary>
	public const string ObjectRef = "messages";

	/// <summary>Every message, in the order the Messages page lists them.</summary>
	public static IReadOnlyList<GameMessage> All { get; } = Enum.GetValues<GameMessage>();

	/// <summary>The attribute on the Messages object that holds <paramref name="message"/>: <c>CONNECT</c>, <c>MOTD</c>, ….</summary>
	public static string AttributeName(GameMessage message) => message.ToString().ToUpperInvariant();
}

/// <summary>The Messages page's state (<c>GET api/admin/messages</c>).</summary>
/// <param name="Source">Where the game reads its messages from now.</param>
/// <param name="SourceChosen">
/// Whether an administrator chose <paramref name="Source"/>. Until one does, the game reads the Messages object
/// exactly while the Messages package is installed.
/// </param>
/// <param name="PackageInstalled">Whether the Messages package is installed.</param>
/// <param name="ObjectDbref">The Messages object's dbref number, or null when it does not exist.</param>
/// <param name="ObjectName">The Messages object's name, or null when it does not exist.</param>
/// <param name="Messages">Each message, in <see cref="GameMessages.All"/> order.</param>
public sealed record GameMessagesResponse(
	GameMessageSource Source,
	bool SourceChosen,
	bool PackageInstalled,
	int? ObjectDbref,
	string? ObjectName,
	IReadOnlyList<GameMessageEntry> Messages);

/// <summary>One message as the Messages page shows it.</summary>
/// <param name="Message">Which message.</param>
/// <param name="Text">The stored text, ANSI escapes and all, as a terminal receives it.</param>
/// <param name="IsDefault">Whether <paramref name="Text"/> is the text SharpMUSH ships, never edited.</param>
/// <param name="Attribute">The attribute that holds it on the Messages object.</param>
/// <param name="ObjectHasAttribute">Whether the Messages object has that attribute.</param>
/// <param name="Preview">What a connection is shown now, with ANSI escapes for its colours, or null when it is shown nothing.</param>
public sealed record GameMessageEntry(
	GameMessage Message,
	string Text,
	bool IsDefault,
	string Attribute,
	bool ObjectHasAttribute,
	string? Preview);

/// <summary>A message's new stored text (<c>PUT api/admin/messages/{message}</c>).</summary>
/// <param name="Text">The text, with ANSI escapes for its colours.</param>
public sealed record GameMessageTextRequest(string Text);

/// <summary>Where to read messages from (<c>PUT api/admin/messages/source</c>).</summary>
/// <param name="Source">The source, or null to follow the Messages package again.</param>
public sealed record GameMessageSourceRequest(GameMessageSource? Source);
