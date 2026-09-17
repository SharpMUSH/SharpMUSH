using System.Text.RegularExpressions;

namespace SharpMUSH.Tests;

/// <summary>
/// Reads the argument counts out of a helpfile's signature line, so a topic can be checked against
/// the <c>MinArgs</c>/<c>MaxArgs</c> the engine actually enforces.
///
/// <para>The notation the helpfiles use is PennMUSH's: arguments separated by commas, a bracketed
/// group holding the optional tail (<c>fn(&lt;a&gt;[, &lt;b&gt;[, &lt;c&gt;]])</c>), and an ellipsis
/// for "and so on". Brackets do double duty — <c>[&lt;object&gt;/]&lt;attribute&gt;</c> and
/// <c>&lt;victim&gt;[/&lt;attribute&gt;]</c> mark an optional part *within* one argument — so a
/// bracket only opens a new argument slot when its contents hold a comma.</para>
/// </summary>
public static partial class HelpSignature
{
	/// <param name="Required">Arguments that appear outside every bracket.</param>
	/// <param name="Maximum">Argument slots in all, ignoring any ellipsis.</param>
	/// <param name="Variadic">Whether an ellipsis says the tail repeats.</param>
	public readonly record struct Arity(int Required, int Maximum, bool Variadic);

	/// <summary>
	/// The arity <paramref name="signature"/> describes, or null when it is not a signature for
	/// <paramref name="functionName"/> or its brackets do not balance.
	/// </summary>
	public static Arity? Parse(string functionName, string signature)
	{
		if (!signature.StartsWith(functionName, StringComparison.OrdinalIgnoreCase)) return null;

		var arguments = signature[functionName.Length..].Trim();
		if (arguments.Length < 2 || arguments[0] != '(' || arguments[^1] != ')') return null;

		var slots = new List<(int Depth, string Text)>();
		if (!Split(arguments[1..^1], 0, slots)) return null;

		var kept = slots.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();

		return new Arity(
			Required: kept.Count(s => s.Depth == 0),
			Maximum: kept.Count(s => !s.Text.Contains("...", StringComparison.Ordinal)),
			Variadic: kept.Any(s => s.Text.Contains("...", StringComparison.Ordinal)));
	}

	/// <summary>
	/// Every signature line a helpfile gives, keyed by the function it names. A signature line is a
	/// wholly backticked line in the block that opens a topic: `fn(&lt;a&gt;)`, optionally several
	/// joined by <c>&lt;br&gt;</c>. Prose that happens to mention a call is not one, which is why the
	/// line has to hold nothing but backticked spans.
	/// </summary>
	public static Dictionary<string, List<(string File, int Line, string Signature)>> ReadAll(DirectoryInfo helpfiles)
	{
		var found = new Dictionary<string, List<(string, int, string)>>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in helpfiles.EnumerateFiles("*.md", SearchOption.AllDirectories).OrderBy(f => f.Name))
		{
			var lines = File.ReadAllLines(file.FullName);
			var index = 0;

			while (index < lines.Length)
			{
				if (!lines[index].StartsWith("# ", StringComparison.Ordinal)) { index++; continue; }

				while (index < lines.Length && lines[index].StartsWith("# ", StringComparison.Ordinal)) index++;
				while (index < lines.Length && lines[index].Trim().Length == 0) index++;

				while (index < lines.Length && IsSignatureLine(lines[index]))
				{
					foreach (Match match in CallSpan().Matches(lines[index].Trim()))
					{
						var name = match.Groups["Name"].Value;
						if (!found.TryGetValue(name, out var list)) found[name] = list = [];
						list.Add((file.Name, index + 1, match.Value.Trim('`')));
					}
					index++;
				}
			}
		}

		return found;
	}

	private static bool IsSignatureLine(string line)
	{
		var trimmed = line.Trim();
		if (!trimmed.StartsWith('`')) return false;

		// Whatever is left once the backticked spans and a trailing <br> are removed is prose.
		return BacktickedSpan().Replace(trimmed, string.Empty).Replace("<br>", string.Empty).Trim().Length == 0;
	}

	/// <summary>
	/// Walks one signature's argument text, recording the bracket depth each argument sits at.
	/// Returns false when the brackets do not balance, which is a defect in the helpfile rather than
	/// a signature with some other arity.
	/// </summary>
	private static bool Split(string text, int depth, List<(int Depth, string Text)> slots)
	{
		var current = string.Empty;
		var index = 0;

		while (index < text.Length)
		{
			var character = text[index];

			if (character == '[')
			{
				var close = MatchingBracket(text, index);
				if (close < 0) return false;

				var inner = text[(index + 1)..close];
				var rest = text[(close + 1)..];

				if (HoldsSeparator(inner) || inner.TrimStart().StartsWith(',')) // an optional group of arguments
				{
					slots.Add((depth, current));
					current = string.Empty;
					var body = inner.TrimStart();
					if (!Split(body.StartsWith(',') ? body[1..] : body, depth + 1, slots)) return false;
					index = close + 1;
					continue;
				}

				if (current.Trim().Length == 0 && (rest.Trim().Length == 0 || rest.TrimStart()[0] is ',' or '['))
				{
					// A whole argument that happens to be optional: fn(<a>[, <b>]) has already been
					// handled above, but fn([<a>][, <b>]) reaches here with <a> alone in the brackets.
					if (!Split(inner, depth + 1, slots)) return false;
					index = close + 1;
					var after = text[index..];
					if (after.TrimStart().StartsWith(',')) index = text.IndexOf(',', index) + 1;
					continue;
				}

				current += inner; // an optional part inside one argument, such as [<object>/]<attribute>
				index = close + 1;
				continue;
			}

			if (character == ',')
			{
				slots.Add((depth, current));
				current = string.Empty;
				index++;
				continue;
			}

			current += character;
			index++;
		}

		slots.Add((depth, current));
		return true;
	}

	private static bool HoldsSeparator(string text)
	{
		var depth = 0;
		foreach (var character in text)
		{
			if (character == '[') depth++;
			else if (character == ']') depth--;
			else if (character == ',' && depth == 0) return true;
		}
		return false;
	}

	private static int MatchingBracket(string text, int open)
	{
		var depth = 0;
		for (var index = open; index < text.Length; index++)
		{
			if (text[index] == '[') depth++;
			else if (text[index] == ']' && --depth == 0) return index;
		}
		return -1;
	}

	[GeneratedRegex(@"`(?<Name>[a-zA-Z_][a-zA-Z0-9_#]*)\(.*?\)`")]
	private static partial Regex CallSpan();

	[GeneratedRegex("`[^`]*`")]
	private static partial Regex BacktickedSpan();
}
