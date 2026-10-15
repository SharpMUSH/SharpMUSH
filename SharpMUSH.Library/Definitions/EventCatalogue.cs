namespace SharpMUSH.Library.Definitions;

/// <summary>What one event argument holds. Softcode still receives plain values; the kind documents them.</summary>
public enum EventArgumentKind
{
	/// <summary>An objid (<c>#12:1700000000</c>), or empty.</summary>
	Objid,

	/// <summary>Objids separated by spaces.</summary>
	ObjidList,

	/// <summary>Plain text.</summary>
	Text,

	/// <summary>Text that keeps its colour and links (a channel line, a page).</summary>
	Markup,

	/// <summary>A whole number, such as a descriptor or a count.</summary>
	Number,

	/// <summary><c>1</c> or <c>0</c>.</summary>
	Boolean,

	/// <summary>One word from a fixed set, such as a cause or a style.</summary>
	Word,

	/// <summary>Milliseconds since 1970.</summary>
	Time,

	/// <summary>An IP address.</summary>
	Address,

	/// <summary>A JSON document.</summary>
	Json
}

/// <summary>
/// Who may be told about an event other than the event handler, which hears every event: what a consumer outside
/// the handler (a webhook) must hold to receive it.
/// </summary>
public enum EventPrivacy
{
	/// <summary>Any object. The event carries nothing a player could not see for themselves.</summary>
	Public,

	/// <summary>An object able to see everything (<c>game.see_all</c>): the event shows hidden or dark objects.</summary>
	Staff,

	/// <summary>Staff who may read private content: the event carries what players wrote to each other, or addresses.</summary>
	Private
}

/// <summary>Which engine an event comes from. A PennMUSH event keeps PennMUSH's argument text exactly.</summary>
public enum EventOrigin
{
	PennMUSH,
	SharpMUSH
}

/// <summary>One argument of an event, by the name handler code reads it as (<c>%&lt;name&gt;</c>).</summary>
/// <param name="Name">
/// The argument's name. A name holding <c>/</c> names the parts of a value that is several numbers joined by
/// <c>/</c> (PennMUSH's <c>recv/sent/cmds</c>), so each part has a name of its own.
/// </param>
/// <param name="Kind">What the argument holds.</param>
public sealed record EventArgument(string Name, EventArgumentKind Kind);

/// <summary>One event in <see cref="EventCatalogue"/>.</summary>
/// <param name="Name">The event's name, <c>GROUP`EVENT</c>, upper case: the event handler's attribute is named after it.</param>
/// <param name="Arguments">Its arguments, in order: the first is <c>%0</c>.</param>
/// <param name="Enactor">Who <c>%#</c> is while the handler's attribute runs.</param>
/// <param name="Description">What happened, one sentence.</param>
/// <param name="Privacy">Who besides the event handler may be told about it.</param>
/// <param name="Stoppable">
/// True when one consumer of the event may stop the ones after it from hearing it; false when every consumer always
/// hears it. Fixed per event.
/// </param>
/// <param name="Origin">Which engine the event comes from.</param>
public sealed record EventDefinition(
	string Name,
	IReadOnlyList<EventArgument> Arguments,
	string Enactor,
	string Description,
	EventPrivacy Privacy,
	bool Stoppable,
	EventOrigin Origin)
{
	/// <summary>The group the event is listed under in <c>help event list</c>, lower case.</summary>
	public string Group => Name[..Name.IndexOf('`')].ToLowerInvariant();

	/// <summary>The arguments' names, in order.</summary>
	public IReadOnlyList<string> ArgumentNames { get; } = Arguments.Select(argument => argument.Name).ToArray();
}

/// <summary>
/// Every event a SharpMUSH game raises. The single description
/// of an event: <see cref="EventArguments"/> names the arguments from it, help is checked against it, and an event
/// raised anywhere must be in it.
/// </summary>
public static class EventCatalogue
{
	private const EventArgumentKind O = EventArgumentKind.Objid;
	private const EventArgumentKind OL = EventArgumentKind.ObjidList;
	private const EventArgumentKind T = EventArgumentKind.Text;
	private const EventArgumentKind M = EventArgumentKind.Markup;
	private const EventArgumentKind N = EventArgumentKind.Number;
	private const EventArgumentKind B = EventArgumentKind.Boolean;
	private const EventArgumentKind W = EventArgumentKind.Word;
	private const EventArgumentKind Ms = EventArgumentKind.Time;
	private const EventArgumentKind Ip = EventArgumentKind.Address;
	private const EventArgumentKind J = EventArgumentKind.Json;

	private const string Nobody = "God (#1): nothing in the game caused it";
	private const EventOrigin Penn = EventOrigin.PennMUSH;
	private const EventOrigin Sharp = EventOrigin.SharpMUSH;

	private static EventArgument[] Args(params (string Name, EventArgumentKind Kind)[] arguments)
		=> arguments.Select(argument => new EventArgument(argument.Name, argument.Kind)).ToArray();

	/// <summary>Every event, in the order <c>help event list</c> gives them.</summary>
	public static readonly IReadOnlyList<EventDefinition> All =
	[
		new(SharpEvents.Dump5Min, Args(("message", T), ("forking", B)), Nobody,
			"A scheduled backup of the world will be taken in five minutes.", EventPrivacy.Public, false, Penn),
		new(SharpEvents.Dump1Min, Args(("message", T), ("forking", B)), Nobody,
			"A scheduled backup of the world will be taken in one minute.", EventPrivacy.Public, false, Penn),
		new(SharpEvents.DumpComplete, Args(("message", T), ("forking", B)), "Whoever ran @backup or @dump, or God (#1) for a scheduled backup",
			"A backup of the world was written.", EventPrivacy.Public, false, Penn),
		new(SharpEvents.DumpError, Args(("message", T), ("forking", B), ("status", T)), "Whoever ran @backup or @dump, or God (#1) for a scheduled backup",
			"A backup of the world could not be written.", EventPrivacy.Public, false, Penn),

		new(SharpEvents.DbDbck, [], "Whoever ran @dbck",
			"@dbck read the newest backup of the world through without a fault.", EventPrivacy.Public, false, Penn),
		new("DB`PURGE", [], Nobody,
			"The scheduled purge of destroyed objects finished.", EventPrivacy.Public, false, Penn),
		new("DB`WCHECK", [], Nobody,
			"The scheduled @warnings check of every object finished.", EventPrivacy.Public, false, Penn),

		new("LOG`ERR", Args(("message", T)), "Whoever ran @log",
			"A message was written to the error log.", EventPrivacy.Private, false, Penn),
		new("LOG`CMD", Args(("message", T)), "Whoever ran @log",
			"A message was written to the command log.", EventPrivacy.Private, false, Penn),
		new("LOG`WIZ", Args(("message", T)), "Whoever ran @log",
			"A message was written to the wizard log.", EventPrivacy.Private, false, Penn),
		new("LOG`CONN", Args(("message", T)), "Whoever ran @log",
			"A message was written to the connection log.", EventPrivacy.Private, false, Penn),
		new("LOG`TRACE", Args(("message", T)), "Whoever ran @log",
			"A message was written to the trace log.", EventPrivacy.Private, false, Penn),
		new("LOG`CHECK", Args(("message", T)), "Whoever ran @log",
			"A message was written to the check log.", EventPrivacy.Private, false, Penn),
		new("LOG`HUH", Args(("message", T)), Nobody,
			"A command was not understood (Huh?).", EventPrivacy.Private, false, Penn),

		new("OBJECT`CREATE", Args(("objid", O), ("clone", O)), "The creator",
			"An object other than a player was created.", EventPrivacy.Staff, false, Penn),
		new("OBJECT`DESTROY", Args(("objid", O), ("name", T), ("type", W), ("owner", O), ("parent", O), ("zone", O)), Nobody,
			"An object was destroyed.", EventPrivacy.Staff, false, Penn),
		new("OBJECT`MOVE", Args(("objid", O), ("destination", O), ("origin", O), ("silent", B), ("cause", W)),
			"Whoever caused the move", "An object moved to a new location.", EventPrivacy.Staff, false, Penn),
		new("OBJECT`RENAME", Args(("objid", O), ("name", T), ("oldname", T)), "Whoever renamed it",
			"An object was renamed.", EventPrivacy.Staff, false, Penn),
		new("OBJECT`FLAG", Args(("objid", O), ("flag", W), ("type", W), ("set", B), ("change", W)), "Whoever set or cleared it",
			"A flag or power with the event restriction was set or cleared.", EventPrivacy.Staff, false, Penn),

		new("SQL`CONNECT", Args(("platform", W)), Nobody,
			"The game connected to its SQL database.", EventPrivacy.Public, false, Penn),
		new("SQL`CONNECTFAIL", Args(("platform", W), ("error", T)), Nobody,
			"The game could not connect to its SQL database.", EventPrivacy.Public, false, Penn),
		new("SQL`DISCONNECT", Args(("platform", W), ("error", T)), Nobody,
			"The game lost its SQL connection.", EventPrivacy.Public, false, Penn),

		new("SIGNAL`USR1", [], Nobody,
			"The server process received SIGUSR1.", EventPrivacy.Public, true, Penn),
		new("SIGNAL`USR2", [], Nobody,
			"The server process received SIGUSR2.", EventPrivacy.Public, true, Penn),

		new("PLAYER`CREATE", Args(("objid", O), ("name", T), ("how", W), ("descriptor", N), ("email", T)),
			"Whoever ran @pcreate, or God (#1) for a player made at the connect screen",
			"A player was created.", EventPrivacy.Private, false, Penn),
		new("PLAYER`CONNECT", Args(("objid", O), ("connections", N), ("descriptor", N)), "The player",
			"A player connected.", EventPrivacy.Staff, false, Penn),
		new("PLAYER`DISCONNECT",
			Args(("objid", O), ("connections", N), ("hidden", B), ("cause", W), ("ip", Ip), ("descriptor", N), ("connected", N),
				("idle", N), ("received/sent/commands", T)),
			"The player", "One of a player's connections closed.", EventPrivacy.Private, false, Penn),
		new("PLAYER`INACTIVITY", [], Nobody,
			"Idle players were disconnected or hidden.", EventPrivacy.Staff, false, Penn),
		new(SharpEvents.PlayerChannels, Args(("objid", O), ("cause", W), ("channel", T)), "Whoever caused the change",
			"A connected player's channel list may have changed.", EventPrivacy.Staff, false, Sharp),
		new("PLAYER`WARN", Args(("objid", O), ("reason", T), ("account", T)), "The staff member's character",
			"Staff warned a character from the web portal.", EventPrivacy.Private, true, Sharp),
		new(SharpEvents.GalleryAdd, Args(("objid", O), ("asset", W), ("url", T), ("use", W)), "The character who uploaded it",
			"An image was added to a character's profile gallery on the web portal.", EventPrivacy.Public, false, Sharp),
		new(SharpEvents.GalleryReport, Args(("objid", O), ("asset", W), ("url", T), ("reason", T), ("caption", T)), "The reporter's character",
			"Someone reported a gallery image on the web portal.", EventPrivacy.Private, false, Sharp),

		new("SOCKET`CONNECT", Args(("descriptor", N), ("ip", Ip)), Nobody,
			"A connection reached the connect screen.", EventPrivacy.Private, false, Penn),
		new("SOCKET`DISCONNECT", Args(("descriptor", N), ("ip", Ip), ("cause", W), ("received/sent/commands", T)), Nobody,
			"A connection closed.", EventPrivacy.Private, false, Penn),
		new("SOCKET`LOGINFAIL", Args(("descriptor", N), ("ip", Ip), ("count", N), ("reason", T), ("objid", O), ("name", T)), Nobody,
			"A login attempt failed.", EventPrivacy.Private, false, Penn),
		new("SOCKET`CREATEFAIL", Args(("descriptor", N), ("ip", Ip), ("count", N), ("reason", T), ("name", T), ("error", T)), Nobody,
			"An attempt to create an account or a character at the connect screen failed.", EventPrivacy.Private, false, Penn),
		new("SOCKET`GMCP", Args(("descriptor", N), ("package", T), ("message", J), ("player", O)),
			"Whoever is logged in on the descriptor, or God (#1) before login",
			"A client sent a GMCP package.", EventPrivacy.Private, true, Sharp),

		new("HTTP`BLOCKED", Args(("descriptor", N), ("ip", Ip), ("method", W), ("path", T), ("reason", T)), Nobody,
			"A sitelock refused an HTTP request.", EventPrivacy.Private, false, Penn),
		new("HTTP`FAIL", Args(("descriptor", N), ("ip", Ip), ("reason", T)), Nobody,
			"An HTTP request could not be read.", EventPrivacy.Private, false, Penn),
		new("HTTP`COMMAND",
			Args(("ip", Ip), ("method", W), ("path", T), ("status", N), ("content-type", T), ("request-length", N),
				("response-length", N)),
			"The HTTP handler", "The HTTP handler answered a request.", EventPrivacy.Private, false, Penn),

		new(SharpEvents.RoomContents, Args(("room", O), ("cause", W)), "Whoever caused the change",
			"What is in a room changed.", EventPrivacy.Staff, false, Sharp),

		new(SharpEvents.ChannelMessage,
			Args(("channel", T), ("speaker", O), ("style", W), ("name", M), ("message", M), ("recipients", OL), ("time", Ms),
				("id", N)),
			"The speaker", "A channel line was delivered.", EventPrivacy.Private, false, Sharp),
		new(SharpEvents.ChannelWho,
			Args(("channel", T), ("member", O), ("name", T), ("change", W), ("viewers", OL), ("cause", W)), "The member",
			"A member came onto or went off a channel's member list.", EventPrivacy.Staff, false, Sharp),

		new(SharpEvents.PageMessage,
			Args(("speaker", O), ("recipients", OL), ("style", W), ("name", T), ("message", M), ("time", Ms), ("id", N)),
			"The pager", "A page reached at least one recipient.", EventPrivacy.Private, false, Sharp),

		new(SharpEvents.MailReceived,
			Args(("recipient", O), ("sender", O), ("number", N), ("subject", M), ("urgent", B)),
			"The sender", "A player received @mail.", EventPrivacy.Private, false, Sharp),

		new(SharpEvents.SceneStatus, Args(("scene", N), ("status", W), ("previous", W)), "Whoever made the scene or changed its status",
			"A scene was made, or its status changed (the Scene plugin).", EventPrivacy.Staff, false, Sharp),

		new(SharpEvents.BackupComplete, Args(("name", T), ("bytes", N)),
			"Whoever ran @backup or @dump, or God (#1) for a scheduled copy",
			"A copy of the world was written.", EventPrivacy.Public, false, Sharp),
		new(SharpEvents.BackupError, Args(("reason", T)),
			"Whoever ran @backup or @dump, or God (#1) for a scheduled copy",
			"A copy of the world could not be written.", EventPrivacy.Public, false, Sharp),
	];

	private static readonly Dictionary<string, EventDefinition> ByName =
		All.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

	/// <summary>The event named <paramref name="name"/> (any case), or null when the catalogue has none.</summary>
	public static EventDefinition? Find(string name) => ByName.GetValueOrDefault(name);
}
