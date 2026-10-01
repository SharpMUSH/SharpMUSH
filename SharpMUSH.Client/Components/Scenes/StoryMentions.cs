using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// Marks the other participants' names in a rendered pose as mentions (README §4.8, §5.4). The pose
/// body is HTML from <c>SceneMarkupRenderer</c>, so only its text is searched: a name inside a tag's
/// attributes or inside an existing link is left alone. A name matches as a whole word and in its own
/// case; a participant's first name matches too, unless two participants share it.
/// </summary>
public static partial class StoryMentions
{
	/// <summary>A participant: their full name, their colour (<c>#rrggbb</c> or null) and whether they are the viewer.</summary>
	public sealed record Target(string Name, string? Color, bool Self);

	/// <summary>Returns <paramref name="html"/> with each participant's name wrapped in a mention link.</summary>
	public static string Mark(string html, IReadOnlyList<Target> targets)
	{
		if (targets.Count == 0 || string.IsNullOrEmpty(html)) return html;

		var byText = Aliases(targets);
		if (byText.Count == 0) return html;

		// Longest first, so a full name wins over its own first name.
		var pattern = string.Join('|', byText.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
		var names = new Regex($@"(?<![\w&])(?:{pattern})(?![\w])", RegexOptions.CultureInvariant);

		var output = new StringBuilder(html.Length + 64);
		var linkDepth = 0;
		foreach (var part in Tag().Split(html))
		{
			if (part.StartsWith('<'))
			{
				if (OpensLink().IsMatch(part)) linkDepth++;
				else if (ClosesLink().IsMatch(part)) linkDepth = Math.Max(0, linkDepth - 1);
				output.Append(part);
			}
			else if (linkDepth > 0)
			{
				output.Append(part);
			}
			else
			{
				output.Append(names.Replace(part, m => Link(m.Value, byText[m.Value])));
			}
		}

		return output.ToString();
	}

	/// <summary>The encoded text each target is found by: the full name, and the first name when only one target has it.</summary>
	private static Dictionary<string, Target> Aliases(IReadOnlyList<Target> targets)
	{
		var map = new Dictionary<string, Target>(StringComparer.Ordinal);
		foreach (var target in targets.Where(t => !string.IsNullOrWhiteSpace(t.Name)))
		{
			map.TryAdd(WebUtility.HtmlEncode(target.Name.Trim()), target);
		}

		var firstNames = targets
			.Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.Name.Trim().Contains(' '))
			.GroupBy(t => t.Name.Trim().Split(' ')[0], StringComparer.Ordinal)
			.Where(g => g.Key.Length >= 3 && g.Count() == 1 && !targets.Any(t => t.Name.Trim() == g.Key));
		foreach (var group in firstNames)
		{
			map.TryAdd(WebUtility.HtmlEncode(group.Key), group.Single());
		}

		return map;
	}

	private static string Link(string text, Target target)
	{
		var name = target.Name.Trim();
		var style = target.Self ? " style=\"--name:var(--accent)\""
			: target.Color is { } c && Hex().IsMatch(c) ? $" style=\"--name:{c}\"" : string.Empty;
		return $"<a class=\"mention\" href=\"/character/{Uri.EscapeDataString(name)}\" data-name=\"{WebUtility.HtmlEncode(name)}\"{style}>{text}</a>";
	}

	[GeneratedRegex("(<[^>]*>)")]
	private static partial Regex Tag();

	[GeneratedRegex(@"^<a[\s>]", RegexOptions.IgnoreCase)]
	private static partial Regex OpensLink();

	[GeneratedRegex(@"^</a\s*>", RegexOptions.IgnoreCase)]
	private static partial Regex ClosesLink();

	[GeneratedRegex("^#[0-9a-fA-F]{6}$")]
	private static partial Regex Hex();
}
