using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Softcode;

/// <summary>
/// The names another server's softcode uses for something SharpMUSH has under its own name: RhostMUSH's
/// <c>wildmatch()</c> is <c>match()</c>, its <c>@sudo</c> is <c>@force/inplace</c>. An attribute written in
/// that dialect (an imported one) reads them; code written here does not.
/// </summary>
/// <remarks>
/// <para>An alias only fills a gap: a built-in, an <c>@function</c> or a command of the same name always
/// comes first, so a dialect never changes what SharpMUSH code means and a game's own <c>@function strip</c>
/// still wins over a dialect's.</para>
/// <para>An attribute's dialect is its attribute flag named for the dialect's <see cref="Key"/>; an
/// attribute with none, and typed input, read the game's <c>dialect</c> option. A Rhost import sets both.</para>
/// <para>An attribute alias is the name the source server has SharpMUSH's attribute under, for a read and for a
/// write. It fills a gap too: an object's own attribute of that name comes first. No alias is a name SharpMUSH has a
/// standard attribute by. <see cref="SoftcodeDialectScope"/> carries the dialect to the attribute reads and writes a
/// built-in makes.</para>
/// </remarks>
/// <param name="Key">The dialect's name, which is also its attribute flag's (<c>rhost</c>).</param>
/// <param name="FunctionAliases">A name this dialect calls, and the SharpMUSH function it means.</param>
/// <param name="CommandAliases">A command name this dialect types, and the SharpMUSH command it means, with
/// switches when the meaning takes them (<c>@force/inplace</c>), or the <c>@attribute</c> command that sets the
/// attribute it means (<c>@haven</c>).</param>
public sealed record SoftcodeDialect(
	string Key,
	IReadOnlyDictionary<string, string> FunctionAliases,
	IReadOnlyDictionary<string, string> CommandAliases)
{
	/// <summary>
	/// An attribute name this dialect reads or sets, and the SharpMUSH attribute it means: the name its import
	/// stores that attribute under (TinyMUX's <c>KILL</c> is <c>DEATH</c>).
	/// </summary>
	public IReadOnlyDictionary<string, string> AttributeAliases { get; init; } = new Dictionary<string, string>();

	/// <summary>
	/// Commands this dialect has and SharpMUSH does not, under a name SharpMUSH would read as something else:
	/// TinyMUX's <c>@fe</c> is its <c>@femit</c>, not SharpMUSH's <c>@feed</c>, and its <c>@last</c> lists logins
	/// rather than setting LAST. Such a line goes to <c>$</c>-commands and then <c>Huh?</c>.
	/// </summary>
	public IReadOnlySet<string> UnsupportedCommands { get; init; } = new HashSet<string>();

	/// <summary>The SharpMUSH function <paramref name="name"/> means in this dialect, if it is one of its aliases.</summary>
	public string? FunctionFor(string name) => FunctionAliases.GetValueOrDefault(name);

	/// <summary>The SharpMUSH command, with any switches, <paramref name="name"/> means in this dialect.</summary>
	public string? CommandFor(string name) => CommandAliases.GetValueOrDefault(name);

	/// <summary>Whether <paramref name="name"/> is a command of this dialect's that SharpMUSH lacks.</summary>
	public bool Lacks(string name) => UnsupportedCommands.Contains(name);

	/// <summary>The SharpMUSH attribute <paramref name="name"/> means in this dialect, if it is one of its aliases.</summary>
	public string? AttributeFor(string name) => AttributeAliases.GetValueOrDefault(name);
}

/// <summary>The dialects SharpMUSH reads, and which one is in force where.</summary>
public static class SoftcodeDialects
{
	/// <summary>
	/// RhostMUSH. Every entry takes the same arguments and gives the same result as its SharpMUSH name;
	/// a Rhost name that differs in either is a function of its own or is rewritten by the import.
	/// </summary>
	public static readonly SoftcodeDialect Rhost = new("rhost",
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["asc"] = "ord",
			["codepoint"] = "chr",
			["charin"] = "recv",
			["charout"] = "sent",
			["createtime"] = "ctime",
			["dice"] = "die",
			["elementsmux"] = "elements",
			["extractword"] = "extract",
			["fbound"] = "bound",
			["freelist"] = "nextdbref",
			["ishidden"] = "hidden",
			["listfunctions"] = "functions",
			["lpowers"] = "powers",
			["modifytime"] = "mtime",
			["mwords"] = "words",
			["nostr"] = "before",
			["npemit"] = "pemit",
			["pfind"] = "pmatch",
			["port"] = "ports",
			["reboottime"] = "restarttime",
			["setq_old"] = "setq",
			["setr_old"] = "setr",
			["strlenvis"] = "strlen",
			["totmatch"] = "matchall",
			["totwildmatch"] = "matchall",
			["visiblemux"] = "visible",
			["wildmatch"] = "match",
			["xdec"] = "dec",
			["xinc"] = "inc"
		},
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Renamed commands.
			["@sudo"] = "@force/inplace",
			["@admin"] = "@config/set",
			["@extansi"] = "@moniker",
			["@reboot"] = "@restart",
			["@logrotate"] = "@logwipe/rotate",
			["@selfboot"] = "@boot/me",
			["@reclist"] = "@recover",
			// Its own command, reached only here: a global @site would make @sit ambiguous.
			["@site"] = "@rhost_site",
			["folder"] = "@mail/folder",
			["rpage"] = "page/reply",
			["rp"] = "page/reply",
			["train"] = "teach",
			["wizhelp"] = "ahelp",
			["+uptime"] = "@uptime",
			// RhostMUSH's alias.conf and compat.conf, where SharpMUSH has no such name or reads it as another
			// command's abbreviation.
			["@brief"] = "examine/brief",
			["@cho"] = "@chown",
			["@chow"] = "@chown",
			["@cl"] = "@clone",
			["@clo"] = "@clone",
			["@lo"] = "@lock",
			["@loc"] = "@lock",
			["@nfy"] = "@notify",
			["@pe"] = "@pemit",
			["@q"] = "@quota",
			["@qu"] = "@quota",
			["@st"] = "@stats",
			["@ver"] = "@version",
			["@walle"] = "@wall/emit",
			["@wallem"] = "@wall/emit",
			["@wallemi"] = "@wall/emit",
			["@wallemit"] = "@wall/emit",
			["@wize"] = "@wizwall/emit",
			["@wizem"] = "@wizwall/emit",
			["@wizemi"] = "@wizwall/emit",
			["@wizemit"] = "@wizwall/emit",
			["d"] = "drop",
			["e"] = "examine",
			["h"] = "help",
			["in"] = "inventory",
			["lo"] = "look",
			["m"] = "goto",
			["mo"] = "goto",
			["mov"] = "goto",
			["r"] = "look",
			["re"] = "look",
			["rea"] = "look",
			["read"] = "look",
			["sc"] = "score",
			["thr"] = "drop",
			["thro"] = "drop",
			["throw"] = "drop",
			["u"] = "use",
			["wh"] = "whisper"
		})
	{
		// The built-in attributes its import stores under SharpMUSH's names (RhostNameTables.RenamedAttributes).
		AttributeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ADESC"] = "ADESCRIBE",
			["AFAIL"] = "AFAILURE",
			["ANSINAME"] = "MONIKER",
			["APAY"] = "APAYMENT",
			["ASUCC"] = "ASUCCESS",
			["DESC"] = "DESCRIBE",
			["EMAIL"] = "REGISTERED_EMAIL",
			["FAIL"] = "FAILURE",
			["IDESC"] = "IDESCRIBE",
			["LASTPAGE"] = "LASTPAGED",
			["MAILSIG"] = "MAILSIGNATURE",
			["ODESC"] = "ODESCRIBE",
			["OFAIL"] = "OFAILURE",
			["OPAY"] = "OPAYMENT",
			["OSUCC"] = "OSUCCESS",
			["PAY"] = "PAYMENT",
			["SUCC"] = "SUCCESS",
			// The failed-lock messages, under the names a failed lock reads (LockMessages).
			["ADFAIL"] = "DROP_LOCK`AFAILURE",
			["AGFAIL"] = "GIVE_LOCK`AFAILURE",
			["ARFAIL"] = "RECEIVE_LOCK`AFAILURE",
			["ATFAIL"] = "TELEPORT_LOCK`AFAILURE",
			["DFAIL"] = "DROP_LOCK`FAILURE",
			["GFAIL"] = "GIVE_LOCK`FAILURE",
			["MFAIL"] = "MAIL_LOCK`FAILURE",
			["ODFAIL"] = "DROP_LOCK`OFAILURE",
			["OGFAIL"] = "GIVE_LOCK`OFAILURE",
			["ORFAIL"] = "RECEIVE_LOCK`OFAILURE",
			["OTFAIL"] = "TELEPORT_LOCK`OFAILURE",
			["RFAIL"] = "RECEIVE_LOCK`FAILURE",
			["TFAIL"] = "TELEPORT_LOCK`FAILURE"
		}
	};

	/// <summary>
	/// TinyMUX. The function names are its 2.12 renames, its <c>alias.conf</c> function aliases and its own names for
	/// functions SharpMUSH has; the command names are its commands and <c>alias.conf</c> entries that SharpMUSH has
	/// under another name, has no command for, or reads as another command's abbreviation.
	/// The same rule as <see cref="Rhost"/>: an entry takes the same arguments and gives the same result.
	/// </summary>
	public static readonly SoftcodeDialect Mux = new("mux",
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ansiname"] = "moniker",
			["connsecs"] = "conn",
			["helptext"] = "textfile",
			["iabs"] = "abs",
			["iadd"] = "add",
			["idiv"] = "div",
			["imul"] = "mul",
			["isign"] = "sign",
			["isub"] = "sub",
			["lastmod"] = "mtime",
			["mailsubj"] = "mailsubject",
			["modifytime"] = "mtime",
			["nonzero"] = "ifelse",
			["parse"] = "iter",
			["ptimefmt"] = "timefmt",
			["secs2time"] = "convsecs",
			["strtrunc"] = "left",
			["time2secs"] = "convtime",
			["url_escape"] = "urlencode",
			["who"] = "lwho"
		},
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["@admin"] = "@config/set",
			["@ansiname"] = "@moniker",
			["@ch"] = "@chown",
			["@cho"] = "@chown",
			["@chow"] = "@chown",
			["@cl"] = "@clone",
			["@clo"] = "@clone",
			["@extansi"] = "@moniker",
			["@fi"] = "@find",
			["@file"] = "@mail/file",
			// Its own command, reached only here: a global @folder would make @fo ambiguous.
			["@folder"] = "@mux_folder",
			["@li"] = "@link",
			["@lis"] = "@list",
			["@lo"] = "@lock",
			["@loc"] = "@lock",
			["@nameaccent"] = "@moniker",
			["@nemit"] = "@emit/noeval",
			["@nfy"] = "@notify",
			["@npemit"] = "@pemit/noeval",
			["@pe"] = "@pemit",
			["@q"] = "@quota",
			["@qmail"] = "@mail/quick",
			["@qu"] = "@quota",
			["@reject"] = "@haven",
			["@send"] = "@mail/send",
			["@st"] = "@stats",
			["b"] = "examine/brief",
			["d"] = "drop",
			["e"] = "examine",
			["h"] = "help",
			["m"] = "goto",
			["npose"] = "pose/noeval",
			["nsay"] = "say/noeval",
			["r"] = "look",
			["re"] = "look",
			["rea"] = "look",
			["read"] = "look",
			["thr"] = "drop",
			["thro"] = "drop",
			["throw"] = "drop",
			["train"] = "teach",
			["u"] = "use"
		})
	{
		UnsupportedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "@fe", "@last" },
		// The built-in attributes its import stores under SharpMUSH's names (TinyMuxNames.AttributeRenames), and the
		// attr_alias entries of its alias.conf.
		AttributeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ADESC"] = "ADESCRIBE",
			["AFAIL"] = "AFAILURE",
			["AKILL"] = "ADEATH",
			["APAY"] = "APAYMENT",
			["ASUCC"] = "ASUCCESS",
			["DESC"] = "DESCRIBE",
			["FAIL"] = "FAILURE",
			["FORWARD"] = "FORWARDLIST",
			["IDESC"] = "IDESCRIBE",
			["KILL"] = "DEATH",
			["LASTPAGE"] = "LASTPAGED",
			["ODESC"] = "ODESCRIBE",
			["OFAIL"] = "OFAILURE",
			["OKILL"] = "ODEATH",
			["OPAY"] = "OPAYMENT",
			["OSUCC"] = "OSUCCESS",
			["PAY"] = "PAYMENT",
			["REJECT"] = "HAVEN",
			["SIGNATURE"] = "MAILSIGNATURE",
			["SPEECHFORMAT"] = "SPEECHMOD",
			["SUCC"] = "SUCCESS",
			// The failed-lock messages, under the names a failed lock reads (LockMessages).
			["ADFAIL"] = "DROP_LOCK`AFAILURE",
			["AGFAIL"] = "GIVE_LOCK`AFAILURE",
			["ARFAIL"] = "RECEIVE_LOCK`AFAILURE",
			["ATFAIL"] = "TELEPORT_LOCK`AFAILURE",
			["DFAIL"] = "DROP_LOCK`FAILURE",
			["GFAIL"] = "GIVE_LOCK`FAILURE",
			["MFAIL"] = "MAIL_LOCK`FAILURE",
			["ODFAIL"] = "DROP_LOCK`OFAILURE",
			["OGFAIL"] = "GIVE_LOCK`OFAILURE",
			["ORFAIL"] = "RECEIVE_LOCK`OFAILURE",
			["OTFAIL"] = "TELEPORT_LOCK`OFAILURE",
			["PFAIL"] = "PAGE_LOCK`FAILURE",
			["RFAIL"] = "RECEIVE_LOCK`FAILURE",
			["TFAIL"] = "TELEPORT_LOCK`FAILURE"
		}
	};

	/// <summary>Every dialect, by key.</summary>
	public static readonly IReadOnlyDictionary<string, SoftcodeDialect> All =
		new Dictionary<string, SoftcodeDialect>(StringComparer.OrdinalIgnoreCase)
		{
			[Rhost.Key] = Rhost,
			[Mux.Key] = Mux
		};

	/// <summary>The dialect <paramref name="attribute"/> is written in, by its flag, or <c>null</c> for none.</summary>
	public static string? Of(SharpAttribute attribute)
		=> attribute.Flags.Select(flag => flag.Name).FirstOrDefault(All.ContainsKey);

	/// <summary>
	/// The dialect in force for <paramref name="evaluation"/>: the attribute's own (a <c>#lambda</c>'s is the
	/// attribute around it), else the game's <paramref name="gameDialect"/>; <c>null</c> when neither names one.
	/// </summary>
	public static SoftcodeDialect? InForce(DBAttribute? evaluation, string? gameDialect)
		=> (DBAttribute.Scope(evaluation)?.Dialect ?? gameDialect) is { Length: > 0 } key
			? All.GetValueOrDefault(key)
			: null;
}
