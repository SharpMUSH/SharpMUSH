using System.Text;
using System.Text.RegularExpressions;
using MarkupString;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// A deliberately bounded template: fixed speech command or custom +command, with data only in
/// top-level text. Opaque slots pass through parsing; their values are bound afterwards, never parsed.
/// Placeholders within expressions, escapes, command names and deferred command bodies are refused.
/// </summary>
public sealed partial class PortalCommandTemplate
{
	private readonly Dictionary<string, string> _values;
	private readonly Regex _slots;
	private int _claimed;
	private const int MaxLength = 5 * 1024 * 1024;
	public string Source { get; }
	public string Template { get; }
	public string Command { get; }
	public bool IsCustom => Command.StartsWith('+');
	public bool NoEval => Command.EndsWith("/noeval", StringComparison.OrdinalIgnoreCase);

	private PortalCommandTemplate(string template, string source, string command, Dictionary<string, string> values, string prefix)
	{
		Source = source;
		Template = template;
		Command = command;
		_values = values;
		_slots = new Regex(Regex.Escape(prefix) + @"[0-9]+X", RegexOptions.CultureInvariant);
	}

	/// <summary>Bindings belong only to the first dispatch, not nested or later softcode.</summary>
	public bool Claim() => Interlocked.Exchange(ref _claimed, 1) == 0;

	public static bool TryCreate(string template, IReadOnlyDictionary<string, string> data,
		out PortalCommandTemplate? compiled, out string error)
	{
		compiled = null;
		error = "Unsupported command template. Use top-level data in think/say/pose/semipose (optionally /noeval), or a custom +command.";
		if (string.IsNullOrWhiteSpace(template) || template.Length > 5 * 1024 * 1024
			|| template.AsSpan().ContainsAny('\r', '\n') || data.Count > 128
			|| data.Values.Any(v => v is null) || data.Values.Sum(v => (long)v.Length) > 5 * 1024 * 1024) return false;
		template = template.Trim();
		var space = template.IndexOf(' ');
		var command = space < 0 ? template : template[..space];
		if (!CommandName().IsMatch(command)) return false;

		var prefix = "SMData" + Guid.NewGuid().ToString("N") + "X";
		// A slot is never supplied by either source or data, even in the event of a collision.
		if (template.Contains(prefix, StringComparison.Ordinal) || data.Values.Any(v => v.Contains(prefix, StringComparison.Ordinal))) return false;
		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		var source = new StringBuilder();
		long expandedLength = template.Length;
		var stack = new Stack<char>();
		var escaped = false;
		var matches = Placeholder().Matches(template).ToDictionary(m => m.Index);
		for (var i = 0; i < template.Length; i++)
		{
			if (matches.TryGetValue(i, out var match))
			{
				if (i <= space || space < 0 || escaped || stack.Count != 0
					|| !Boundary(template[i - 1])
					|| i + match.Length < template.Length && !Boundary(template[i + match.Length])
					|| !data.TryGetValue(match.Groups[1].Value, out var value) || string.IsNullOrEmpty(value)) return false;
				var slot = prefix + values.Count + "X";
				expandedLength += value.Length;
				if (expandedLength > MaxLength || values.Count >= 128) return false;
				values.Add(slot, value.Replace("\r\n", "\n").Replace('\r', '\n'));
				source.Append(slot);
				i += match.Length - 1;
				continue;
			}
			var c = template[i];
			source.Append(c);
			if (escaped) { escaped = false; continue; }
			if (c == '\\') { escaped = true; continue; }
			if (c == '%' && stack.Count == 0) return false;
			if (c is '[' or '(' or '{') stack.Push(c);
			if (c is ']' or ')' or '}')
			{
				if (!stack.TryPop(out var open) || (open, c) is not (('[', ']') or ('(', ')') or ('{', '}'))) return false;
			}
		}
		if (stack.Count != 0 || escaped || source.Length > MaxLength) return false;
		compiled = new PortalCommandTemplate(template, source.ToString(), command, values, prefix);
		error = string.Empty;
		return true;
	}

	private static bool Boundary(char c) => char.IsWhiteSpace(c) || c is '=' or ',' or ':' or '/' or '-' or '.' or ';';

	/// <summary>One pass, preserving template markup and never scanning inserted values for further slots.</summary>
	public bool TryBind(MarkupText text, int outputLimit, out MarkupText bound)
	{
		bound = MarkupText.Empty;
		var limit = Math.Min(MaxLength, outputLimit);
		var parts = new List<MarkupText>();
		var start = 0;
		long length = text.Length;
		foreach (Match match in _slots.Matches(text.Text))
		{
			length += _values[match.Value].Length - match.Length;
			if (length > limit) return false;
			parts.Add(text.Substring(start, match.Index - start));
			parts.Add(MarkupText.Plain(_values[match.Value]));
			start = match.Index + match.Length;
		}
		parts.Add(text.Substring(start));
		if (length > limit) return false;
		bound = MarkupText.Concat(parts);
		return true;
	}

	[GeneratedRegex(@"\A(?:(?:think|say|pose|semipose)(?:/noeval)?|\+[a-z0-9_/-]+)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex CommandName();
	[GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
	private static partial Regex Placeholder();
}
