using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>
/// RhostMUSH commands whose arguments SharpMUSH takes in another command, written as that command: <c>join Bob</c>
/// becomes <c>@teleport me=[loc(pmatch(Bob))]</c>, <c>@lfunction double=me/DOUBLE</c> becomes
/// <c>@function/local double=me,DOUBLE</c>.
/// </summary>
/// <remarks>
/// <para>A command is rewritten where a command starts: at the start of the attribute or of a <c>$</c>-command's or
/// <c>^</c>-listen's action, or after <c>;</c> or <c>{</c>; an <c>@</c>-command also after a <c>,</c> or <c>=</c> in a
/// command that runs its arguments, such as <c>@switch</c>. <c>grab</c> and <c>join</c>, words of prose too, are not
/// read at the start of the text. A command runs to the next <c>;</c> (or <c>,</c>, after one) or the <c>}</c> closing
/// it, outside any brackets; one with a <c>&lt;placeholder&gt;</c> in it, as help text writes, is left alone. A form with no SharpMUSH counterpart is left as written, and the note says so.</para>
/// <para>RhostMUSH accepts a switch cut short (<c>@zone/del</c>), so a switch here is the first of the command's
/// whose name starts with what was typed.</para>
/// <para>A local function is called by name in RhostMUSH and through <c>localfun()</c> here, so the import also
/// rewrites the calls (<see cref="RewriteLocalFunctionCalls"/>) of every name an <c>@lfunction</c> defines
/// (<see cref="LocalFunctionNames"/>).</para>
/// </remarks>
public static partial class RhostCommandRewriter
{
	/// <summary>A command's switches, in the order a cut-short switch is matched against them.</summary>
	private static readonly Dictionary<string, string[]> Switches = new(StringComparer.OrdinalIgnoreCase)
	{
		["@zone"] = ["add", "delete", "list", "purge", "replace"],
		["@thaw"] = ["delete"],
		["@lfunction"] = ["delete", "display", "list", "max", "min", "notrace", "preserve", "privilege", "protect"],
		["@dynhelp"] = ["nolabel", "parse", "query", "search"],
		["wmail"] = ["access", "alias", "clean", "cmdlist", "dtime", "fix", "lfix", "load", "lock", "off", "on", "override",
			"restart", "size", "smax", "time", "unload", "wipe"]
	};

	/// <summary>Every command in <paramref name="softcode"/> this rewriter covers, rewritten, with one note per change or doubt.</summary>
	public static string Rewrite(string softcode, List<string> notes)
	{
		if (!Mentions().IsMatch(softcode))
		{
			return softcode;
		}

		var output = new StringBuilder(softcode.Length);
		var at = 0;
		foreach (Match head in CommandHead().Matches(softcode))
		{
			if (head.Index < at)
			{
				continue;
			}

			var name = head.Groups["name"].Value;
			var lead = head.Groups["lead"].Value;
			var after = lead.TrimEnd(' ', '\t') is [.., var last] ? last : '\0';
			if (!StartsACommand(softcode, head.Index, head.Index + lead.TrimEnd(' ', '\t').Length - 1, name, after))
			{
				continue;
			}

			var end = CommandEnd(softcode, head.Index + head.Length, stopAtComma: after == ',');
			var written = softcode[head.Index..end][lead.Length..];
			if (Placeholder().IsMatch(written))
			{
				continue;
			}

			var typed = head.Groups["switches"].Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
			var switches = typed.Select(sw => SwitchName(name, sw)).ToArray();
			var argument = softcode[(head.Index + head.Length)..end].Trim();
			if (name.ToLowerInvariant() is "grab" or "join" && argument.Length > 0 && !PlayerReference().IsMatch(argument))
			{
				continue;
			}

			output.Append(softcode, at, head.Index - at).Append(lead);
			output.Append(switches.Contains(null)
				? Left(notes, written, $"{name} has no switch {string.Join("/", typed)} here")
				: RewriteOne(name.ToLowerInvariant(), switches!, argument, written, notes));
			at = end;
		}

		return output.Append(softcode, at, softcode.Length - at).ToString();
	}

	/// <summary>
	/// Whether the command at <paramref name="index"/>, after <paramref name="after"/> (at <paramref name="leadAt"/>),
	/// is one: after <c>;</c>, <c>{</c> or a <c>$</c>-command's or <c>^</c>-listen's pattern; after a <c>,</c>, or the
	/// first <c>=</c>, of a command that runs its arguments, such as <c>@switch</c>; and, but for <c>grab</c> and
	/// <c>join</c>, which are words of prose too, at the start of the text.
	/// </summary>
	private static bool StartsACommand(string text, int index, int leadAt, string name, char after)
	{
		switch (after)
		{
			case ';' or '{' or ':':
				return true;
			case ',' or '=':
				var start = StatementStart(text, index);
				var statement = text[start..index];
				if (!RunsActions().IsMatch(statement))
				{
					return false;
				}

				var pattern = Pattern().Match(statement);
				return after == ',' || FirstEquals(text, start + (pattern.Success ? pattern.Length : 0), leadAt + 1) == leadAt;
			default:
				return !name.Equals("grab", StringComparison.OrdinalIgnoreCase) && !name.Equals("join", StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>The first <c>=</c> outside brackets from <paramref name="start"/> up to <paramref name="end"/>, or -1.</summary>
	private static int FirstEquals(string text, int start, int end)
	{
		var depth = 0;
		for (var i = start; i < end; i++)
		{
			switch (text[i])
			{
				case '\\' or '%':
					i++;
					break;
				case '[' or '(' or '{':
					depth++;
					break;
				case ']' or ')' or '}':
					depth = Math.Max(0, depth - 1);
					break;
				case '=' when depth == 0:
					return i;
			}
		}

		return -1;
	}

	/// <summary>Where the statement holding <paramref name="index"/> starts: after the <c>;</c> or unclosed <c>{</c> before it.</summary>
	private static int StatementStart(string text, int index)
	{
		var depth = 0;
		for (var i = index - 1; i >= 0; i--)
		{
			switch (text[i])
			{
				case ']' or ')' or '}':
					depth++;
					break;
				case '[' or '(' when depth > 0:
				case '{' when depth > 0:
					depth--;
					break;
				case '{':
				case ';' when depth == 0:
				case '\n':
					return i + 1;
			}
		}

		return 0;
	}

	private static string RewriteOne(string name, string?[] switches, string argument, string written, List<string> notes)
	{
		var (left, right) = SplitAtEquals(argument);
		return name switch
		{
			"@freeze" or "@thaw" or "join" or "grab" when argument.Length == 0 => Left(notes, written, $"{name} names no job or player"),
			"join" => Written(notes, written, $"@teleport me=[loc(pmatch({argument}))]",
				"join written as @teleport, which asks for control of the destination or JUMP_OK there"),
			"grab" => Written(notes, written, $"@teleport [pmatch({argument})]=here",
				"grab written as @teleport, which asks for control of the player and the destination"),
			"@freeze" => Written(notes, written, $"@queue/pause {argument}", "@freeze written as @queue/pause"),
			"@thaw" when switches is ["delete"] => Written(notes, written, $"@halt/pid {argument}",
				"@thaw/delete written as @halt/pid; check by hand: RhostMUSH names a frozen job by when it was frozen, SharpMUSH by its pid"),
			"@thaw" when switches is [] => Written(notes, written, $"@queue/resume {argument}",
				"@thaw written as @queue/resume; check by hand: RhostMUSH names a frozen job by when it was frozen, SharpMUSH by its pid"),
			"@dynhelp" => DynamicHelp(switches, left, right, written, notes),
			"@lfunction" => LocalFunction(switches, left, right, written, notes),
			"@zone" => Left(notes, written, "an object's zones are set with @chzone"),
			"wmail" => WizardMail(switches, argument, written, notes),
			"@totemdef" => Left(notes, written,
				"@totemdef: totems are roles here, and who may assign one is set on the role (@role)"),
			_ => Left(notes, written, $"{name} with these switches has no SharpMUSH form")
		};
	}

	/// <summary><c>@dynhelp &lt;file&gt;[/&lt;topic&gt;][=&lt;player&gt;]</c>, as <c>textfile()</c> sent to the player.</summary>
	private static string DynamicHelp(string?[] switches, string left, string? right, string written, List<string> notes)
	{
		if (switches.Length > 0 || left.Contains('^'))
		{
			return Left(notes, written, "@dynhelp with a switch or a subdirectory has no SharpMUSH form");
		}

		if (left.IndexOfAny([',', '[', '(']) >= 0)
		{
			return Left(notes, written, "@dynhelp naming its file or topic with code has no single SharpMUSH form");
		}

		var slash = left.IndexOf('/');
		var file = slash < 0 ? left : left[..slash];
		var topic = slash < 0 || slash == left.Length - 1 ? "help" : left[(slash + 1)..];
		var target = string.IsNullOrWhiteSpace(right) ? "%#"
			: right.Trim() is var named && PlayerName().IsMatch(named) && !named.Equals("me", StringComparison.OrdinalIgnoreCase) ? $"*{named}"
			: right.Trim();
		return Written(notes, written, $"@pemit {target}=[textfile({file},{topic})]",
			"@dynhelp written as textfile(); check by hand that the text file is one of the game's text file categories");
	}

	/// <summary><c>@lfunction</c>, as <c>@function/local</c>.</summary>
	private static string LocalFunction(string?[] switches, string left, string? right, string written, List<string> notes)
	{
		var name = left.Trim();
		if (name.Length > 0 && !name.StartsWith('_') && !FunctionName().IsMatch(name))
		{
			return Left(notes, written, $"{name} is not a name a SharpMUSH function may take");
		}

		switch (switches)
		{
			case ["list"]:
				return Written(notes, written, "@function/local", "@lfunction/list written as @function/local");
			case ["display"]:
				return Written(notes, written, $"@function/local {name}", "@lfunction/display written as @function/local");
			case ["delete"]:
				return Written(notes, written, $"@function/local/delete {name}", "@lfunction/delete written as @function/local/delete");
			case [] when name.Length == 0:
				return Written(notes, written, "@function/local", "@lfunction written as @function/local");
		}

		var slash = right?.IndexOf('/') ?? -1;
		if (switches.Any(sw => sw is "min" or "max" or "list" or "display" or "delete")
			|| right is null || slash < 1 || name.Length == 0 || name.StartsWith('_'))
		{
			return Left(notes, written, name.StartsWith('_')
				? "@lfunction naming a built-in to override in sandbox() has no SharpMUSH form"
				: "@lfunction in this form has no SharpMUSH form");
		}

		var extra = (switches.Contains("privilege")
			? ""
			: "; check by hand: a local function runs as the object holding it here, as /privilege made it in RhostMUSH")
			+ (switches.Any(sw => sw is "preserve" or "protect" or "notrace")
				? "; check by hand: /preserve, /protect and /notrace have no counterpart, so wrap the body in localize() where registers must be kept"
				: "");
		return Written(notes, written, $"@function/local {name}={right[..slash].Trim()},{right[(slash + 1)..].Trim()}",
			$"@lfunction {name} written as @function/local, called with localfun(){extra}; check by hand: if {name} is a SharpMUSH function, rename it, as a local function may not take a built-in's name");
	}

	/// <summary>The <c>wmail</c> forms the mail debugger has; the rest have no SharpMUSH form.</summary>
	private static string WizardMail(string?[] switches, string argument, string written, List<string> notes)
	{
		if (switches is [] && argument.Split(' ', 2) is [var word, ..] && word.Length > 0 && SwitchName("wmail", word) is { } spelled)
		{
			// RhostMUSH also takes the subcommand as a word: wmail wipe Jerk.
			return WizardMail([spelled], argument.Split(' ', 2) is [_, var rest] ? rest.Trim() : "", written, notes);
		}

		return switches switch
		{
			["wipe"] when argument.Length > 0 => Written(notes, written, $"@mail/debug clear={argument}", "wmail/wipe written as @mail/debug clear"),
			["fix"] => Written(notes, written, "@mail/debug fix", "wmail/fix written as @mail/debug fix"),
			_ => Left(notes, written, "wmail in this form has no SharpMUSH form")
		};
	}

	/// <summary>
	/// The local functions <paramref name="softcode"/> defines with <c>@lfunction &lt;name&gt;=&lt;object&gt;/&lt;attribute&gt;</c>.
	/// </summary>
	public static IEnumerable<string> LocalFunctionNames(string softcode)
		=> LocalFunctionDefinition().Matches(softcode).Select(match => match.Groups["name"].Value);

	/// <summary>
	/// The global functions <paramref name="softcode"/> defines with <c>@function &lt;name&gt;=…</c>. RhostMUSH
	/// tries these before a local function of the same name, so their calls stay as written.
	/// </summary>
	public static IEnumerable<string> GlobalFunctionNames(string softcode)
		=> GlobalFunctionDefinition().Matches(softcode).Select(match => match.Groups["name"].Value);

	/// <summary>
	/// Each call of one of <paramref name="names"/> written through <c>localfun()</c>: <c>double(7)</c> becomes
	/// <c>localfun(double,7)</c>. A name is a call when <c>(</c> follows it and no letter, digit or <c>_</c> comes
	/// before it and it stands where RhostMUSH would call it (<see cref="Evaluated"/>); a <c>$</c>-command's or
	/// <c>^</c>-listen's pattern, <c>lit()</c>'s argument and escaped text are left alone.
	/// </summary>
	public static string RewriteLocalFunctionCalls(string softcode, IReadOnlySet<string> names, List<string> notes)
	{
		if (names.Count == 0 || softcode.IndexOf('(') < 0)
		{
			return softcode;
		}

		var output = new StringBuilder(softcode.Length);
		var said = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pattern = Pattern().Match(softcode);
		var body = pattern.Success ? pattern.Length : 0;
		output.Append(softcode, 0, body);
		var i = body;
		while (i < softcode.Length)
		{
			var ch = softcode[i];
			if (ch is '\\' or '%' && i + 1 < softcode.Length)
			{
				output.Append(softcode, i, 2);
				i += 2;
				continue;
			}

			if (!char.IsAsciiLetter(ch) || (i > 0 && IsNameCharacter(softcode[i - 1])))
			{
				output.Append(ch);
				i++;
				continue;
			}

			var end = i;
			while (end < softcode.Length && IsNameCharacter(softcode[end])) end++;
			var name = softcode[i..end];
			if (end < softcode.Length && softcode[end] == '(' && name.Equals("lit", StringComparison.OrdinalIgnoreCase))
			{
				var close = CommandEnd(softcode, end + 1, stopAtSemicolon: false, closing: ')');
				output.Append(softcode, i, Math.Min(close + 1, softcode.Length) - i);
				i = close + 1;
				continue;
			}

			if (end < softcode.Length && softcode[end] == '(' && names.Contains(name) && Evaluated(softcode, i, body))
			{
				var empty = end + 1 < softcode.Length && softcode[end + 1] == ')';
				output.Append("localfun(").Append(name).Append(empty ? "" : ",");
				if (said.Add(name))
				{
					notes.Add($"{name}() called through localfun()");
				}

				i = end + 1;
				continue;
			}

			output.Append(name);
			i = end;
		}

		return output.ToString();
	}

	private static bool IsNameCharacter(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';

	/// <summary>
	/// Whether a function named at <paramref name="index"/> is called: at the start of the text or of a statement,
	/// right after <c>[</c>, <c>(</c>, <c>,</c>, <c>=</c> or <c>{</c>, or as the argument of a command such as
	/// <c>think</c>; not inside prose, where RhostMUSH leaves it as text.
	/// </summary>
	private static bool Evaluated(string text, int index, int body)
	{
		var before = index - 1;
		while (before >= body && text[before] is ' ' or '\t') before--;
		if (before < body || text[before] is '[' or '(' or ',' or ';' or '=' or '{')
		{
			return true;
		}

		var head = text[Math.Max(StatementStart(text, index), body)..index].Trim();
		return head.Length > 0 && !head.Any(char.IsWhiteSpace)
			&& (head.StartsWith('@') || head.Equals("think", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>The command's switch <paramref name="typed"/> stands for, written in full; <see langword="null"/> for none.</summary>
	private static string? SwitchName(string command, string typed)
		=> Switches.TryGetValue(command, out var known)
			? known.FirstOrDefault(name => name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
			: null;

	/// <summary>The text before the first <c>=</c> outside brackets, and after it, or <see langword="null"/> when there is none.</summary>
	private static (string Left, string? Right) SplitAtEquals(string argument)
	{
		var depth = 0;
		for (var i = 0; i < argument.Length; i++)
		{
			switch (argument[i])
			{
				case '\\' or '%':
					i++;
					break;
				case '[' or '(' or '{':
					depth++;
					break;
				case ']' or ')' or '}':
					depth = Math.Max(0, depth - 1);
					break;
				case '=' when depth == 0:
					return (argument[..i].Trim(), argument[(i + 1)..].Trim());
			}
		}

		return (argument.Trim(), null);
	}

	/// <summary>
	/// Where the command starting at <paramref name="start"/> ends: the next <c>;</c> outside brackets, or the
	/// <paramref name="closing"/> character with no opener after <paramref name="start"/>, or the end of the text.
	/// </summary>
	private static int CommandEnd(string text, int start, bool stopAtSemicolon = true, char closing = '}', bool stopAtComma = false)
	{
		var depth = 0;
		for (var i = start; i < text.Length; i++)
		{
			switch (text[i])
			{
				case '\\' or '%':
					i++;
					break;
				case '[' or '(' or '{':
					depth++;
					break;
				case ']' or ')' or '}' when depth > 0:
					depth--;
					break;
				case var ch when ch == closing && depth == 0:
					return i;
				case ';' when stopAtSemicolon && depth == 0:
				case ',' when stopAtComma && depth == 0:
					return i;
			}
		}

		return text.Length;
	}

	private static string Written(List<string> notes, string written, string replacement, string note)
	{
		notes.Add(note);
		return replacement + Trailing(written);
	}

	private static string Left(List<string> notes, string written, string note)
	{
		notes.Add($"check by hand: {written.Trim()} left as written: {note}");
		return written;
	}

	/// <summary>The whitespace the command ended with, kept after its rewrite.</summary>
	private static string Trailing(string written) => written[written.TrimEnd().Length..];

	[GeneratedRegex(@"(?:grab|join|@zone|@dynhelp|@lfunction|@freeze|@thaw|wmail|@totemdef)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex Mentions();

	/// <summary>
	/// A covered command where a command starts, with its switches. A command spelled <c>&lt;cmd&gt;</c>, as help
	/// text writes it, does not match.
	/// </summary>
	[GeneratedRegex(@"(?<lead>(?:^[$^][^:\r\n]*:|^|[;{,=])[ \t]*)(?<name>grab|join|@zone|@dynhelp|@lfunction|@freeze|@thaw|wmail|@totemdef)(?<switches>(?:/[A-Za-z]+)*)(?=[ \t]|$|[;}])",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex CommandHead();

	/// <summary>A statement whose arguments are commands, as with <c>@switch</c> or <c>@dolist</c>, after any <c>$</c>/<c>^</c> pattern.</summary>
	[GeneratedRegex(@"^\s*(?:[$^][^:\r\n]*:)?\s*@(?:switch|select|wait|dolist|force|break|assert|skip|include)\b",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex RunsActions();

	/// <summary>A <c>$</c>-command's or <c>^</c>-listen's pattern and its <c>:</c>.</summary>
	[GeneratedRegex(@"^\s*[$^](?:\\.|[^:\\\r\n])*:", RegexOptions.CultureInvariant)]
	private static partial Regex Pattern();

	/// <summary>One player, as <c>grab</c> and <c>join</c> take: a substitution, a dbref, a name, or a function's result.</summary>
	[GeneratedRegex(@"^(?:##|%[#0-9nN]|%[qQ][0-9a-zA-Z]|%[vVwWxXyYzZ][a-zA-Z]|#\d+|\*?[A-Za-z0-9_'-]+|\[[^\]]*\])$", RegexOptions.CultureInvariant)]
	private static partial Regex PlayerReference();

	/// <summary>A player's name, which <c>@pemit</c> finds anywhere when written <c>*name</c>.</summary>
	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_'-]*$", RegexOptions.CultureInvariant)]
	private static partial Regex PlayerName();

	[GeneratedRegex(@"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
	private static partial Regex FunctionName();

	/// <summary>A word in angle brackets, as help text writes an argument to fill in.</summary>
	[GeneratedRegex(@"<[A-Za-z][A-Za-z -]*>", RegexOptions.CultureInvariant)]
	private static partial Regex Placeholder();

	[GeneratedRegex(@"(?:^[$^][^:\r\n]*:|^|[;{,=])[ \t]*@function(?:/(?:pri|pre|pro|n)[A-Za-z]*)*[ \t]+(?<name>[A-Za-z][A-Za-z0-9_]*)[ \t]*=",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex GlobalFunctionDefinition();

	[GeneratedRegex(@"(?:^[$^][^:\r\n]*:|^|[;{,=])[ \t]*@lfunction(?:/(?:pri|pre|pro|n)[A-Za-z]*)*[ \t]+(?<name>[A-Za-z][A-Za-z0-9_]*)[ \t]*=[^;}/]+/",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex LocalFunctionDefinition();
}
