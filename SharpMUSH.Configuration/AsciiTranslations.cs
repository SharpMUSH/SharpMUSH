using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Configuration;

/// <summary>
/// The <c>ascii_translations</c> option: space separated <c>character=text</c> pairs, the stand-in a client
/// without Unicode is sent for each character, before the built-in ones. The text is printable ASCII, in
/// double quotes when it holds a space (<c>·=" - "</c>), and may be empty to leave the character out.
/// </summary>
public static partial class AsciiTranslations
{
	/// <summary>The option's shape, for the configuration page; <see cref="TryParse"/> checks each pair too.</summary>
	/// <remarks>A double quote is written <c>\x22</c>: the pattern is copied into generated code and the portal as it is.</remarks>
	public const string Pattern = @"^\s*(?:[^\s=\x22]+=(?:\x22[^\x22]*\x22|[^\s\x22]*)(?:\s+|$))*$";

	/// <summary>The pairs <paramref name="text"/> holds, or why it does not read.</summary>
	/// <param name="text">The option's value.</param>
	/// <param name="pairs">Each character and its stand-in, in order; empty when there are none.</param>
	/// <param name="error">What is wrong, or null when it reads.</param>
	public static bool TryParse(string? text, out IReadOnlyList<KeyValuePair<string, string>> pairs, out string? error)
	{
		pairs = [];
		error = null;
		if (string.IsNullOrWhiteSpace(text)) return true;
		if (!ShapeRegex().IsMatch(text))
		{
			error = "Write each as character=text, space separated, the text in double quotes when it holds a space.";
			return false;
		}

		var read = new List<KeyValuePair<string, string>>();
		foreach (Match pair in PairRegex().Matches(text))
		{
			var key = pair.Groups["Key"].Value;
			var value = pair.Groups["Quoted"].Success ? pair.Groups["Quoted"].Value : pair.Groups["Bare"].Value;
			if (new StringInfo(key).LengthInTextElements != 1 || key.All(char.IsAscii))
			{
				error = $"'{key}' is not one character outside ASCII.";
				return false;
			}

			if (value.Any(c => c is < ' ' or > '~'))
			{
				error = $"The text for '{key}' is not plain ASCII.";
				return false;
			}

			read.Add(new KeyValuePair<string, string>(key, value));
		}

		pairs = read;
		return true;
	}

	[GeneratedRegex(Pattern)]
	private static partial Regex ShapeRegex();

	[GeneratedRegex(@"(?<Key>[^\s=""]+)=(?:""(?<Quoted>[^""]*)""|(?<Bare>[^\s""]*))")]
	private static partial Regex PairRegex();
}
