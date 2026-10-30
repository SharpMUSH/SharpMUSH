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
/// </remarks>
/// <param name="Key">The dialect's name, which is also its attribute flag's (<c>rhost</c>).</param>
/// <param name="FunctionAliases">A name this dialect calls, and the SharpMUSH function it means.</param>
/// <param name="CommandAliases">A command name this dialect types, and the SharpMUSH command it means, with
/// switches when the meaning takes them (<c>@force/inplace</c>).</param>
public sealed record SoftcodeDialect(
	string Key,
	IReadOnlyDictionary<string, string> FunctionAliases,
	IReadOnlyDictionary<string, string> CommandAliases)
{
	/// <summary>The SharpMUSH function <paramref name="name"/> means in this dialect, if it is one of its aliases.</summary>
	public string? FunctionFor(string name) => FunctionAliases.GetValueOrDefault(name);

	/// <summary>The SharpMUSH command, with any switches, <paramref name="name"/> means in this dialect.</summary>
	public string? CommandFor(string name) => CommandAliases.GetValueOrDefault(name);
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
		});

	/// <summary>
	/// TinyMUX. The function names are its 2.12 renames and its <c>alias.conf</c> function aliases; the command names
	/// are its <c>alias.conf</c> entries that SharpMUSH has no command for or reads as another command's abbreviation.
	/// The same rule as <see cref="Rhost"/>: an entry takes the same arguments and gives the same result.
	/// </summary>
	public static readonly SoftcodeDialect Mux = new("mux",
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ansiname"] = "moniker",
			["connsecs"] = "conn",
			["cpad"] = "center",
			["helptext"] = "textfile",
			["iabs"] = "abs",
			["iadd"] = "add",
			["idiv"] = "div",
			["imul"] = "mul",
			["isign"] = "sign",
			["isub"] = "sub",
			["lastmod"] = "mtime",
			["lpad"] = "ljust",
			["mailsubj"] = "mailsubject",
			["modifytime"] = "mtime",
			["nonzero"] = "ifelse",
			["rpad"] = "rjust",
			["secs2time"] = "convsecs",
			["strtrunc"] = "left",
			["time2secs"] = "convtime",
			["who"] = "lwho"
		},
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["@ansiname"] = "@moniker",
			["@ch"] = "@chown",
			["@cho"] = "@chown",
			["@chow"] = "@chown",
			["@cl"] = "@clone",
			["@clo"] = "@clone",
			["@extansi"] = "@moniker",
			["@fi"] = "@find",
			["@file"] = "@mail/file",
			["@li"] = "@link",
			["@lis"] = "@list",
			["@lo"] = "@lock",
			["@loc"] = "@lock",
			["@nameaccent"] = "@moniker",
			["@nfy"] = "@notify",
			["@pe"] = "@pemit",
			["@q"] = "@quota",
			["@qmail"] = "@mail/quick",
			["@qu"] = "@quota",
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
			["u"] = "use"
		});

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
