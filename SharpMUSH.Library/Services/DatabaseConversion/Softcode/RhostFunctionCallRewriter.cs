using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>
/// RhostMUSH function calls whose arguments differ from SharpMUSH's counterpart, written as that counterpart:
/// <c>ljc(s,6)</c> becomes <c>ljust(left(s,6),6)</c>, <c>streq(a,b)</c> becomes <c>not(comp(a,b,I))</c>.
/// </summary>
/// <remarks>
/// A call is rewritten only in a shape whose result is the same; any other shape (an argument count the rule
/// does not cover, an unclosed call) is left as written, and the note says so. Arguments are rewritten first, so
/// calls nest. A name is a call only when <c>(</c> follows it and no letter, digit or <c>_</c> comes before it.
/// A rule that repeats an argument is applied only when that argument holds no call, so nothing is evaluated
/// twice; <c>lit()</c>'s argument is never evaluated, so nothing in it is rewritten.
/// </remarks>
public static class RhostFunctionCallRewriter
{
	private delegate string? Rule(string[] arguments);

	private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
	{
		// RhostMUSH cuts a long string to its first <length> characters, both ways round.
		["ljc"] = args => args.Length is 2 or 3 ? $"ljust(left({args[0]},{args[1]}),{string.Join(',', args[1..])})" : null,
		["rjc"] = args => args.Length is 2 or 3 ? $"rjust(left({args[0]},{args[1]}),{string.Join(',', args[1..])})" : null,
		["streq"] = args => args.Length == 2 ? $"not(comp({args[0]},{args[1]},I))" : null,
		["ncomp"] = args => args.Length == 2 ? $"comp({args[0]},{args[1]},F)" : null,
		["power10"] = args => args.Length == 1 ? $"power(10,{args[0]})" : null,
		["xnor"] = args => args.Length == 2 ? $"not(xor({args[0]},{args[1]}))" : null,
		["nummatch"] = args => args.Length switch
		{
			2 => $"words(matchall({args[0]},{args[1]}))",
			3 => $"words(matchall({args[0]},{args[1]},{args[2]}),{args[2]})",
			_ => null
		},
		// RhostMUSH answers the positions space-separated whatever the delimiter.
		["randmatch"] = args => args.Length switch
		{
			2 => $"shuffle(matchall({args[0]},{args[1]}))",
			3 => $"shuffle(matchall({args[0]},{args[1]},{args[2]},%b))",
			_ => null
		},
		// chomp()'s sides are trim()'s styles: l, r and b.
		["chomp"] = args => args.Length switch
		{
			1 => $"trim({args[0]},%r)",
			2 => $"trim({args[0]},%r,{args[1]})",
			_ => null
		},
		// RhostMUSH's tag() looks a tag up; SharpMUSH's tag() writes an HTML tag.
		["tag"] = args => args.Length == 1 ? $"tagref({args[0]})" : null,
		["lnum2"] = args => args.Length == 1 ? $"lnum({args[0]})" : null,
		// Each flag an argument of its own, !FLAG allowed, as andlflags() and orlflags() take them in a list.
		["andflag"] = args => args.Length >= 2 ? $"andlflags({args[0]},{FlagList(args[1..])})" : null,
		["orflag"] = args => args.Length >= 2 ? $"orlflags({args[0]},{FlagList(args[1..])})" : null,
		// Plural for 0 and for anything past -1 to 1; singular for none given or empty.
		["moneyname"] = args => args switch
		{
			[] or [""] => "config(money_singular)",
			[var value] => $"if(and(strlen({value}),or(eq({value},0),gt(abs({value}),1))),config(money_plural),config(money_singular))",
			_ => null
		},
		// A comparison of bittype() with a number asks whether a role of some rank is held.
		["gte"] = args => BitTypeComparison(args, atLeast: 0, mirrored: "lte"),
		["gt"] = args => BitTypeComparison(args, atLeast: 1, mirrored: "lt"),
		["lt"] = args => BitTypeComparison(args, atLeast: 0, mirrored: "gt", negated: true),
		["lte"] = args => BitTypeComparison(args, atLeast: 1, mirrored: "gte", negated: true)
	};

	/// <summary>Rules that apply to one shape of a function SharpMUSH has: any other call to it is left without a note.</summary>
	private static readonly HashSet<string> ShapeOnly = new(StringComparer.OrdinalIgnoreCase) { "gte", "gt", "lt", "lte" };

	/// <summary>The role each step of RhostMUSH's bittype() scale stands for, from 1 to 7.</summary>
	private static readonly string[] BitTypeRoles = ["player", "guildmaster", "builder", "royalty", "wizard", "immortal", "god"];

	/// <summary>
	/// <c>bittype(&lt;object&gt;)</c> compared with a whole number, as <c>hasrole()</c> asking whether the object's owner
	/// holds the step's role or a higher one: the comparison holds when the step is at least the number plus
	/// <paramref name="atLeast"/>, or, <paramref name="negated"/>, when it is not. A number first is the
	/// <paramref name="mirrored"/> comparison the other way round. Null for any other shape.
	/// </summary>
	private static string? BitTypeComparison(string[] args, int atLeast, string mirrored, bool negated = false)
	{
		switch (args)
		{
			case [var left, var right] when BitTypeObject(left) is { } subject && int.TryParse(right.Trim(), out var number):
				var step = number + atLeast;
				return step <= 0 || step > BitTypeRoles.Length
					? ((step <= 0) != negated ? "1" : "0")
					: (negated ? $"not(hasrole(owner({subject}),{BitTypeRoles[step - 1]},1))" : $"hasrole(owner({subject}),{BitTypeRoles[step - 1]},1)");
			case [var left, var right] when BitTypeObject(right) is not null && int.TryParse(left.Trim(), out _):
				return Rules[mirrored]([right, left]);
			default:
				return null;
		}
	}

	/// <summary>The object of an argument that is just a <c>bittype()</c> call, bracketed or not: <c>me</c> when it names none.</summary>
	private static string? BitTypeObject(string argument)
	{
		var call = argument.Trim();
		if (call.StartsWith('[') && call.EndsWith(']')) call = call[1..^1];
		if (!call.StartsWith("bittype(", StringComparison.OrdinalIgnoreCase)
			|| Arguments(call, "bittype(".Length) is not ({ } arguments, var close) || close != call.Length - 1)
		{
			return null;
		}

		return arguments switch
		{
			[var subject] when subject.Trim().Length > 0 => subject.Trim(),
			[_] => "me",
			_ => null
		};
	}

	/// <summary>The arguments each rule writes more than once, by position.</summary>
	private static readonly Dictionary<string, int[]> Repeated = new(StringComparer.OrdinalIgnoreCase)
	{
		["ljc"] = [1],
		["rjc"] = [1],
		["moneyname"] = [0]
	};

	/// <summary>Rewritten functions whose name SharpMUSH also has, for something else.</summary>
	private static readonly HashSet<string> SharedNames = new(StringComparer.OrdinalIgnoreCase) { "tag" };

	/// <summary>Every call a rule covers, rewritten, with one note per function rewritten or left.</summary>
	public static string Rewrite(string softcode, List<string> notes)
		=> RewriteSpan(softcode, notes, new HashSet<string>(StringComparer.Ordinal));

	private static string RewriteSpan(string text, List<string> notes, HashSet<string> said)
	{
		var output = new StringBuilder(text.Length);
		var i = 0;
		while (i < text.Length)
		{
			var ch = text[i];
			if (ch is '\\' or '%' && i + 1 < text.Length)
			{
				output.Append(text, i, 2);
				i += 2;
				continue;
			}

			if (!char.IsAsciiLetter(ch) || (i > 0 && IsNameCharacter(text[i - 1])))
			{
				output.Append(ch);
				i++;
				continue;
			}

			var end = i;
			while (end < text.Length && IsNameCharacter(text[end])) end++;
			var name = text[i..end];
			if (end < text.Length && text[end] == '(' && name.Equals("lit", StringComparison.OrdinalIgnoreCase)
				&& Arguments(text, end + 1) is ({ }, var literalClose))
			{
				output.Append(text, i, literalClose + 1 - i);
				i = literalClose + 1;
				continue;
			}

			if (end >= text.Length || text[end] != '(' || !Rules.TryGetValue(name, out var rule)
					|| Arguments(text, end + 1) is not ({ } arguments, var close))
			{
				output.Append(name);
				i = end;
				continue;
			}

			var rewritten = arguments.Select(argument => RewriteSpan(argument, notes, said)).ToArray();
			var lower = name.ToLowerInvariant();
			if (Repeated.TryGetValue(name, out var repeated)
				&& repeated.FirstOrDefault(index => index < rewritten.Length && HoldsCall(rewritten[index]), -1) is var computed and >= 0)
			{
				output.Append(name).Append('(').Append(string.Join(',', rewritten)).Append(')');
				Say(notes, said, $"{lower}() with a call in argument {computed + 1} left as written: its rewrite would evaluate that argument more than once");
			}
			else if (rule(rewritten) is { } replacement)
			{
				output.Append(replacement);
				Say(notes, said, ShapeOnly.Contains(lower)
					? $"{lower}() comparing bittype() written with hasrole()"
					: $"{lower}() written as {replacement[..replacement.IndexOf('(')]}()");
			}
			else if (ShapeOnly.Contains(lower))
			{
				output.Append(name).Append('(').Append(string.Join(',', rewritten)).Append(')');
			}
			else
			{
				output.Append(name).Append('(').Append(string.Join(',', rewritten)).Append(')');
				Say(notes, said, $"{lower}() with {rewritten.Length} argument(s) left as written" +
					(SharedNames.Contains(lower) ? $": SharpMUSH's {lower}() is another function" : $": SharpMUSH has no {lower}()"));
			}

			i = close + 1;
		}

		return output.ToString();
	}

	private static void Say(List<string> notes, HashSet<string> said, string note)
	{
		if (said.Add(note)) notes.Add(note);
	}

	private static bool HoldsCall(string argument) => argument.AsSpan().IndexOfAny('(', '[') >= 0;

	/// <summary>
	/// Arguments joined as one space-separated list. An argument holding a call goes in brackets: only a call that
	/// starts an argument is evaluated without them.
	/// </summary>
	private static string FlagList(string[] arguments)
		=> string.Join(' ', arguments.Select(argument => HoldsCall(argument) ? $"[{argument}]" : argument));

	private static bool IsNameCharacter(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';

	/// <summary>The arguments of a call whose list starts at <paramref name="from"/>, and its closing <c>)</c>.</summary>
	private static (string[]? Arguments, int Close) Arguments(string text, int from)
	{
		var arguments = new List<string>();
		var depth = 0;
		var start = from;
		for (var i = from; i < text.Length; i++)
		{
			switch (text[i])
			{
				case '\\' or '%':
					i++;
					break;
				case '(' or '[' or '{':
					depth++;
					break;
				case ')' when depth == 0:
					arguments.Add(text[start..i]);
					return (arguments.ToArray(), i);
				case ')' or ']' or '}' when depth > 0:
					depth--;
					break;
				case ',' when depth == 0:
					arguments.Add(text[start..i]);
					start = i + 1;
					break;
			}
		}

		return (null, text.Length);
	}
}
