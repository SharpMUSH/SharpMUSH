using System.Text;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// A palette line's <c>&lt;slots&gt;</c>: what is asked for before it runs. <c>\&lt;</c> is a literal
/// <c>&lt;</c>. Values go in as typed and are never evaluated again.
/// </summary>
public static class PaletteLines
{
	/// <summary>A piece of a line: literal text, or a slot by name.</summary>
	public readonly record struct Part(string Text, bool IsSlot);

	/// <summary><paramref name="line"/> split into text and slots. An unclosed <c>&lt;</c> is text.</summary>
	public static IReadOnlyList<Part> Parse(string line)
	{
		var parts = new List<Part>();
		var text = new StringBuilder();
		for (var i = 0; i < line.Length; i++)
		{
			var c = line[i];
			if (c == '\\' && i + 1 < line.Length && line[i + 1] == '<')
			{
				text.Append('<');
				i++;
				continue;
			}

			var close = c == '<' ? line.IndexOf('>', i + 1) : -1;
			if (close <= i + 1 || line.AsSpan(i + 1, close - i - 1).IndexOf('<') >= 0)
			{
				text.Append(c);
				continue;
			}

			if (text.Length > 0) parts.Add(new Part(text.ToString(), false));
			text.Clear();
			parts.Add(new Part(line[(i + 1)..close].Trim(), true));
			i = close;
		}

		if (text.Length > 0) parts.Add(new Part(text.ToString(), false));
		return parts;
	}

	/// <summary>The slot names in <paramref name="line"/>, in order.</summary>
	public static IReadOnlyList<string> Slots(string line)
		=> [.. Parse(line).Where(part => part.IsSlot).Select(part => part.Text)];

	/// <summary>
	/// <paramref name="line"/> with each slot <paramref name="values"/> names filled in, and whether none
	/// is left. A slot still open stays written as <c>&lt;name&gt;</c>.
	/// </summary>
	public static (string Line, bool Ready) Fill(string line, IReadOnlyDictionary<string, string> values)
	{
		var result = new StringBuilder();
		var ready = true;
		foreach (var part in Parse(line))
		{
			if (!part.IsSlot)
			{
				result.Append(part.Text.Replace("<", "\\<"));
				continue;
			}

			if (Lookup(values, part.Text) is { } value)
			{
				result.Append(value);
				continue;
			}

			ready = false;
			result.Append('<').Append(part.Text).Append('>');
		}

		return ready ? (Plain(line, values), true) : (result.ToString(), false);
	}

	/// <summary>The line to send: slots filled from <paramref name="values"/>, <c>\&lt;</c> as <c>&lt;</c>.</summary>
	public static string Plain(string line, IReadOnlyDictionary<string, string> values)
		=> string.Concat(Parse(line).Select(part => part.IsSlot ? Lookup(values, part.Text) ?? string.Empty : part.Text));

	/// <summary>
	/// What makes two lines the same command: lower case, runs of spaces as one, slot names left out,
	/// so <c>+bbread &lt;board&gt;/&lt;post&gt;</c> and <c>+BBREAD &lt;b&gt;/&lt;n&gt;</c> are one key.
	/// </summary>
	public static string Key(string line)
	{
		var key = new StringBuilder();
		foreach (var part in Parse(line))
		{
			key.Append(part.IsSlot ? "<>" : part.Text.ToLowerInvariant());
		}

		return string.Join(' ', key.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
	}

	private static string? Lookup(IReadOnlyDictionary<string, string> values, string name)
		=> values.TryGetValue(name, out var exact)
			? exact
			: values.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
