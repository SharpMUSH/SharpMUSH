using System.Text;

namespace SharpMUSH.Configuration.Mssp;

/// <summary>The sections of the MSSP specification's variable tables.</summary>
public enum MsspGroup
{
	Required,
	Generic,
	Categorization,
	World,
	Protocols,
	Commercial,
	Hiring
}

/// <summary>What a variable's values look like, which decides how it is checked and edited.</summary>
public enum MsspValueKind
{
	/// <summary>Free text.</summary>
	Text,

	/// <summary>A whole number; <c>-1</c> is the specification's "not available".</summary>
	Number,

	/// <summary><c>1</c> or <c>0</c>.</summary>
	Flag,

	/// <summary>One value, picked from <see cref="MsspVariable.Choices"/>.</summary>
	Choice,

	/// <summary>Any number of values, picked from <see cref="MsspVariable.Choices"/>.</summary>
	Choices,

	/// <summary>Any number of free-text values, the most important last.</summary>
	List
}

/// <summary>Who supplies a variable's value.</summary>
public enum MsspSource
{
	/// <summary>The administrator, through <see cref="Options.MsspOptions"/>.</summary>
	Setting,

	/// <summary>A configuration option, named by <see cref="MsspVariable.Option"/>.</summary>
	Option,

	/// <summary>The running game: counted or read when the report is made.</summary>
	Live,

	/// <summary>SharpMUSH itself: what it is and what it supports.</summary>
	Server
}

/// <summary>One MSSP variable as the specification defines it, and where SharpMUSH takes its value from.</summary>
/// <param name="Name">The variable's canonical name: upper case, words separated by one space.</param>
/// <param name="Description">The specification's description, shortened.</param>
/// <param name="Choices">
/// The values the specification lists. With <see cref="OpenEnded"/> they are suggestions; otherwise
/// the editor offers only these, though a value outside them is still accepted.
/// </param>
/// <param name="Option">The configuration option the value comes from, for <see cref="MsspSource.Option"/>.</param>
public sealed record MsspVariable(
	string Name,
	MsspGroup Group,
	MsspValueKind Kind,
	string Description,
	MsspSource Source = MsspSource.Setting,
	string? Option = null,
	IReadOnlyList<string>? Choices = null,
	bool OpenEnded = false)
{
	public IReadOnlyList<string> Choices { get; init; } = Choices ?? [];

	/// <summary>True when the server supplies the value and an administrator cannot set it.</summary>
	public bool ReportedByServer => Source != MsspSource.Setting;
}

/// <summary>
/// The MSSP variables (<see href="https://tintin.mudhalla.net/protocols/mssp/"/>): the specification's
/// official tables, plus the protocol flags crawlers still read from its earlier extended list.
/// </summary>
/// <remarks>
/// The server reports the variables it can know — PennMUSH's <c>report_mssp</c> (<c>src/bsd.c</c>)
/// handles "name, players, uptime, port, codebase, family, pueblo, ssl, website" the same way
/// (<c>game/mushcnf.dst</c>) — and everything else is the administrator's, set through
/// <see cref="Options.MsspOptions"/>. A name this catalog does not hold is still accepted there: MSSP
/// is a protocol for servers describing themselves, and crawlers read names they have never heard of.
/// </remarks>
public static class MsspCatalog
{
	/// <summary>The longest value accepted, in characters.</summary>
	public const int MaxValueLength = 512;

	/// <summary>The most values one variable may carry.</summary>
	public const int MaxValues = 32;

	/// <summary>The most variables an administrator may set.</summary>
	public const int MaxVariables = 128;

	private static readonly string[] Genres =
		["Adult", "Fantasy", "Historical", "Horror", "Modern", "Mystery", "None", "Romance", "Science Fiction", "Spiritual"];

	private static readonly string[] Gameplay =
	[
		"Adventure", "Educational", "Hack and Slash", "None", "Player versus Player", "Player versus Environment",
		"Questing", "Roleplaying", "Simulation", "Social", "Strategy"
	];

	private static readonly string[] Statuses = ["Alpha", "Closed Beta", "Open Beta", "Live"];

	private static readonly string[] GameSystems =
		["D&D", "d20 System", "World of Darkness", "Real Time", "Tick Based", "Turn Based", "Custom", "None"];

	private static readonly string[] Intermud = ["AberChat", "I3", "IMC2", "MudNet"];

	private static readonly string[] Subgenres =
	[
		"Alternate History", "Anime", "Cyberpunk", "Detective", "Discworld", "Dragonlance", "Christian Fiction",
		"Classical Fantasy", "Crime", "Dark Fantasy", "Epic Fantasy", "Erotic", "Exploration", "Forgotten Realms",
		"Frankenstein", "Gothic", "High Fantasy", "Magical Realism", "Medieval Fantasy", "Multiverse", "Paranormal",
		"Post-Apocalyptic", "Military Science Fiction", "Mythology", "Pulp", "Star Wars", "Steampunk", "Suspense",
		"Time Travel", "Weird Fiction", "World War II", "Urban Fantasy", "None"
	];

	private static readonly string[] CrawlDelays = ["-1", "1", "5", "11", "23"];

	/// <summary>Every variable, in the order the specification lists them.</summary>
	public static readonly IReadOnlyList<MsspVariable> All =
	[
		new("NAME", MsspGroup.Required, MsspValueKind.Text, "Name of the game.", MsspSource.Option, "mud_name"),
		new("PLAYERS", MsspGroup.Required, MsspValueKind.Number, "Players connected now, counted the way WHO counts them.", MsspSource.Live),
		new("UPTIME", MsspGroup.Required, MsspValueKind.Number, "When the server started, as Unix time.", MsspSource.Live),

		new("CODEBASE", MsspGroup.Generic, MsspValueKind.List, "The codebase and its version.", MsspSource.Server),
		new("PORT", MsspGroup.Generic, MsspValueKind.Number, "The telnet port.", MsspSource.Option, "port"),
		new("SSL", MsspGroup.Generic, MsspValueKind.Number, "The TLS telnet port. Not reported while it is 0.", MsspSource.Option, "ssl_port"),
		new("WEBSITE", MsspGroup.Generic, MsspValueKind.Text, "The game's website. Not reported while it is unset.", MsspSource.Option, "mud_url"),
		new("CHARSET", MsspGroup.Generic, MsspValueKind.List, "Character sets the server speaks, the preferred one last.", MsspSource.Server),
		new("CONTACT", MsspGroup.Generic, MsspValueKind.Text, "Email address for contacting the game."),
		new("DISCORD", MsspGroup.Generic, MsspValueKind.Text, "Invite link to the game's Discord server, with https://."),
		new("ICON", MsspGroup.Generic, MsspValueKind.Text, "Link to a square image (bmp, png, jpg or gif), at least 64x64 pixels and at most 256KB."),
		new("CREATED", MsspGroup.Generic, MsspValueKind.Number, "Year the game was created."),
		new("LANGUAGE", MsspGroup.Generic, MsspValueKind.Text, "English name of the language played in, such as English or German."),
		new("LOCATION", MsspGroup.Generic, MsspValueKind.Text, "English name of the country the server is in."),
		new("MINIMUM AGE", MsspGroup.Generic, MsspValueKind.Number, "Minimum age to play. Leave it out if there is none."),
		new("HOSTNAME", MsspGroup.Generic, MsspValueKind.Text, "Hostname players connect to."),
		new("IP", MsspGroup.Generic, MsspValueKind.Text, "IPv4 address players connect to."),
		new("IPV6", MsspGroup.Generic, MsspValueKind.Text, "IPv6 address players connect to."),
		new("CRAWL DELAY", MsspGroup.Generic, MsspValueKind.Number, "Fewest hours between crawls; -1 leaves it to the crawler.", Choices: CrawlDelays, OpenEnded: true),
		new("REFERRAL", MsspGroup.Generic, MsspValueKind.List, "Other MSSP games for crawlers to check, as \"host port\" with a space."),

		new("FAMILY", MsspGroup.Categorization, MsspValueKind.List, "The codebase family.", MsspSource.Server),
		new("GENRE", MsspGroup.Categorization, MsspValueKind.Choice, "The game's genre.", Choices: Genres),
		new("SUBGENRE", MsspGroup.Categorization, MsspValueKind.Text, "A narrower genre. None if it does not apply.", Choices: Subgenres, OpenEnded: true),
		new("GAMEPLAY", MsspGroup.Categorization, MsspValueKind.Choices, "How the game is played.", Choices: Gameplay),
		new("STATUS", MsspGroup.Categorization, MsspValueKind.Choice, "How far along the game is.", Choices: Statuses),
		new("GAMESYSTEM", MsspGroup.Categorization, MsspValueKind.Text, "The rules system, such as World of Darkness; Custom for your own, None if there is none.", Choices: GameSystems, OpenEnded: true),
		new("INTERMUD", MsspGroup.Categorization, MsspValueKind.List, "Intermud protocols the game is on, the most important last.", Choices: Intermud, OpenEnded: true),

		new("AREAS", MsspGroup.World, MsspValueKind.Number, "Number of areas; -1 if not available."),
		new("HELPFILES", MsspGroup.World, MsspValueKind.Number, "Number of help files; -1 if not available."),
		new("MOBILES", MsspGroup.World, MsspValueKind.Number, "Number of unique mobiles; -1 if not available."),
		new("OBJECTS", MsspGroup.World, MsspValueKind.Number, "Number of unique objects; -1 if not available."),
		new("ROOMS", MsspGroup.World, MsspValueKind.Number, "Number of unique rooms; 0 if roomless."),
		new("CLASSES", MsspGroup.World, MsspValueKind.Number, "Number of player classes; 0 if classless."),
		new("LEVELS", MsspGroup.World, MsspValueKind.Number, "Number of player levels; 0 if level-less."),
		new("RACES", MsspGroup.World, MsspValueKind.Number, "Number of player races; 0 if raceless."),
		new("SKILLS", MsspGroup.World, MsspValueKind.Number, "Number of player skills; 0 if skill-less."),

		new("ANSI", MsspGroup.Protocols, MsspValueKind.Flag, "ANSI colour.", MsspSource.Server),
		new("UTF-8", MsspGroup.Protocols, MsspValueKind.Flag, "UTF-8 text.", MsspSource.Server),
		new("XTERM 256 COLORS", MsspGroup.Protocols, MsspValueKind.Flag, "xterm 256 colours.", MsspSource.Server),
		new("XTERM TRUE COLORS", MsspGroup.Protocols, MsspValueKind.Flag, "24-bit colour.", MsspSource.Server),
		new("GMCP", MsspGroup.Protocols, MsspValueKind.Flag, "Generic MUD Communication Protocol.", MsspSource.Server),
		new("MSDP", MsspGroup.Protocols, MsspValueKind.Flag, "MUD Server Data Protocol.", MsspSource.Server),
		new("MCCP", MsspGroup.Protocols, MsspValueKind.Flag, "MUD Client Compression Protocol.", MsspSource.Server),
		new("MXP", MsspGroup.Protocols, MsspValueKind.Flag, "MUD eXtension Protocol.", MsspSource.Option, "mxp"),
		new("PUEBLO", MsspGroup.Protocols, MsspValueKind.Flag, "Pueblo HTML.", MsspSource.Option, "pueblo"),
		new("VT100", MsspGroup.Protocols, MsspValueKind.Flag, "A VT100 interface."),

		new("PAY TO PLAY", MsspGroup.Commercial, MsspValueKind.Flag, "Players pay to play."),
		new("PAY FOR PERKS", MsspGroup.Commercial, MsspValueKind.Flag, "Players can pay for perks."),

		new("HIRING BUILDERS", MsspGroup.Hiring, MsspValueKind.Flag, "The game is looking for builders."),
		new("HIRING CODERS", MsspGroup.Hiring, MsspValueKind.Flag, "The game is looking for coders.")
	];

	private static readonly Dictionary<string, MsspVariable> ByName = All.ToDictionary(variable => variable.Name);

	/// <summary>The variables the server reports itself, which an administrator cannot set.</summary>
	public static IEnumerable<MsspVariable> ServerReported => All.Where(variable => variable.ReportedByServer);

	/// <summary>The catalog's entry for <paramref name="name"/>, in any spelling <see cref="Canonicalize"/> accepts.</summary>
	public static MsspVariable? Find(string name) => ByName.GetValueOrDefault(Canonicalize(name));

	/// <summary>True when the server reports <paramref name="name"/> itself.</summary>
	public static bool IsReportedByServer(string name) => Find(name) is { ReportedByServer: true };

	/// <summary>
	/// The canonical spelling of a variable name: upper case, underscores read as spaces (the
	/// specification's recommended substitution), runs of spaces collapsed, ends trimmed.
	/// </summary>
	public static string Canonicalize(string name)
	{
		var folded = new StringBuilder(name.Length);
		var pendingSpace = false;

		foreach (var c in name.Select(character => character == '_' ? ' ' : character))
		{
			if (char.IsWhiteSpace(c))
			{
				pendingSpace = folded.Length > 0;
				continue;
			}

			if (pendingSpace)
			{
				folded.Append(' ');
				pendingSpace = false;
			}

			folded.Append(char.ToUpperInvariant(c));
		}

		return folded.ToString();
	}

	/// <summary>
	/// <paramref name="settings"/> as they are stored and reported: names canonical, values trimmed,
	/// blank values and variables with none left dropped, the catalog's order first and any other name
	/// after in ordinal order. Each entry that cannot be kept is left out and described in
	/// <paramref name="problems"/>, including two spellings of one name.
	/// </summary>
	public static Dictionary<string, string[]> Normalize(IReadOnlyDictionary<string, string[]> settings, out List<string> problems)
	{
		problems = [];
		var kept = new Dictionary<string, string[]>(StringComparer.Ordinal);

		foreach (var (name, raw) in settings)
		{
			var canonical = Canonicalize(name);
			var values = (raw ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0).ToArray();
			if (values.Length == 0 && !IsReportedByServer(canonical))
			{
				continue;
			}

			if (kept.ContainsKey(canonical))
			{
				problems.Add($"{canonical} is given twice.");
				continue;
			}

			if (Validate(canonical, values) is { } problem)
			{
				problems.Add(problem);
				continue;
			}

			kept[canonical] = values;
		}

		var catalogued = All.Where(variable => kept.ContainsKey(variable.Name)).Select(variable => variable.Name);
		var others = kept.Keys.Where(name => Find(name) is null).Order(StringComparer.Ordinal);
		return catalogued.Concat(others).ToDictionary(name => name, name => kept[name], StringComparer.Ordinal);
	}

	/// <summary>
	/// Why <paramref name="values"/> cannot be set for <paramref name="name"/>, or null when they can.
	/// Names and values must be printable: MSSP's own bytes and the plaintext reply's tab and line
	/// break would split a variable on the wire.
	/// </summary>
	public static string? Validate(string name, IReadOnlyList<string> values)
	{
		var canonical = Canonicalize(name);
		if (canonical.Length == 0)
		{
			return "A variable needs a name.";
		}

		if (!canonical.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-'))
		{
			return $"{canonical}: a variable name is letters, digits, spaces and hyphens.";
		}

		if (Find(canonical) is { ReportedByServer: true } reported)
		{
			return $"{canonical} is reported by the server{(reported.Option is { } option ? $" from {option}" : string.Empty)}, so it cannot be set here.";
		}

		if (values.Count == 0)
		{
			return $"{canonical} needs a value.";
		}

		if (values.Count > MaxValues)
		{
			return $"{canonical} carries more than {MaxValues} values.";
		}

		foreach (var value in values)
		{
			if (value.Length > MaxValueLength)
			{
				return $"{canonical}: a value is longer than {MaxValueLength} characters.";
			}

			if (value.Any(char.IsControl))
			{
				return $"{canonical}: a value cannot hold tabs, line breaks or other control characters.";
			}

			switch (Find(canonical)?.Kind)
			{
				case MsspValueKind.Number when !long.TryParse(value, out _):
					return $"{canonical} is a whole number, not \"{value}\".";
				case MsspValueKind.Flag when value is not ("0" or "1"):
					return $"{canonical} is 1 or 0, not \"{value}\".";
			}
		}

		var kind = Find(canonical)?.Kind;
		if (values.Count > 1 && kind is MsspValueKind.Number or MsspValueKind.Flag or MsspValueKind.Choice)
		{
			return $"{canonical} takes one value.";
		}

		return null;
	}
}
