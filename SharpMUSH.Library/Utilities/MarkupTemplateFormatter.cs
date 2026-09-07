using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Utilities;

public static partial class MarkupTemplateFormatter
{
	[GeneratedRegex(@"(?<!\{)\{(\d+)(?:,[^}]*)?(?::[^}]*)?\}(?!\})",
		RegexOptions.CultureInvariant)]
	private static partial Regex CompositeFormatPlaceholderRegex();

	public static MString Format(string template, params MString[] args)
	{
		if (args.Length == 0)
		{
			return MarkupText.Plain(UnescapeCompositeFormatLiteral(template));
		}

		var parts = new List<MString>();
		var cursor = 0;

		foreach (Match match in CompositeFormatPlaceholderRegex().Matches(template))
		{
			if (match.Index > cursor)
			{
				parts.Add(MarkupText.Plain(UnescapeCompositeFormatLiteral(template[cursor..match.Index])));
			}

			if (int.TryParse(match.Groups[1].Value, out var index) && index >= 0 && index < args.Length)
			{
				parts.Add(args[index]);
			}
			else
			{
				parts.Add(MarkupText.Plain(UnescapeCompositeFormatLiteral(match.Value)));
			}

			cursor = match.Index + match.Length;
		}

		if (cursor < template.Length)
		{
			parts.Add(MarkupText.Plain(UnescapeCompositeFormatLiteral(template[cursor..])));
		}

		return MarkupText.Concat(CollectionsMarshal.AsSpan(parts));
	}

	private static string UnescapeCompositeFormatLiteral(string text) =>
		text.Replace("{{", "{").Replace("}}", "}");
}
