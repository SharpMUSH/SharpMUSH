using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>The names a Discord message's mentions read as in the game.</summary>
/// <param name="Users">A mentioned user's speaking identity on the link, or their Discord display name.</param>
/// <param name="Channels">A mentioned channel's name.</param>
/// <param name="Roles">A mentioned role's name.</param>
public sealed record DiscordMentionNames(
	IReadOnlyDictionary<ulong, string> Users,
	IReadOnlyDictionary<ulong, string> Channels,
	IReadOnlyDictionary<ulong, string> Roles)
{
	public static readonly DiscordMentionNames None = new(
		new Dictionary<ulong, string>(), new Dictionary<ulong, string>(), new Dictionary<ulong, string>());
}

/// <summary>
/// How a Discord message reads in the game: its Markdown as markup, its mentions as names, and its custom emoji as
/// <c>:name:</c>. Nothing in it is evaluated; it arrives as typed text does.
/// </summary>
/// <remarks>
/// Bold, italic, underline and strikethrough become the game's styles; masked links become links. Code keeps its
/// text as written. A spoiler (<c>||text||</c>) stays as written, since the game has nothing to hide text with.
/// Headings, quotes and lists keep their marks, which read well enough as text.
/// </remarks>
public static partial class DiscordInboundText
{
	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);
	private static readonly AnsiMarkup Italic = AnsiMarkup.Create(italic: true);
	private static readonly AnsiMarkup Underline = AnsiMarkup.Create(underlined: true);
	private static readonly AnsiMarkup Strike = AnsiMarkup.Create(strikeThrough: true);

	/// <summary>Longest first, so <c>**</c> is not read as two <c>*</c>.</summary>
	private static readonly (string Delimiter, AnsiMarkup Style)[] Emphasis =
	[
		("**", Bold), ("__", Underline), ("~~", Strike), ("*", Italic), ("_", Italic),
	];

	public static MString Parse(string content, DiscordMentionNames names) => Inline(content, names);

	private static MString Inline(string text, DiscordMentionNames names)
	{
		List<MString> pieces = [];
		var plain = new StringBuilder();

		void Flush()
		{
			if (plain.Length > 0)
			{
				pieces.Add(MarkupText.Plain(plain.ToString()));
				plain.Clear();
			}
		}

		void Add(MString piece)
		{
			Flush();
			pieces.Add(piece);
		}

		var i = 0;
		while (i < text.Length)
		{
			var c = text[i];
			if (c == '\\' && i + 1 < text.Length && Escapable(text[i + 1]))
			{
				plain.Append(text[i + 1]);
				i += 2;
			}
			else if (c == '`' && Code(text, i) is (var code, var afterCode))
			{
				Add(MarkupText.Plain(code));
				i = afterCode;
			}
			else if (c == '<' && Entity(text, i, names) is (var entity, var afterEntity))
			{
				plain.Append(entity);
				i = afterEntity;
			}
			else if (c == '[' && MaskedLink().Match(text, i) is { Success: true } link)
			{
				Add(MarkupText.Wrap(AnsiMarkup.Create(linkUrl: link.Groups["url"].Value, linkKind: LinkKind.Url),
					Inline(link.Groups["text"].Value, names)));
				i += link.Length;
			}
			else if (c is '*' or '_' or '~' && Emphasised(text, i) is (var style, var inner, var afterEmphasis))
			{
				Add(MarkupText.Wrap(style, Inline(inner, names)));
				i = afterEmphasis;
			}
			else
			{
				plain.Append(c);
				i++;
			}
		}

		Flush();
		return pieces.Count == 1 ? pieces[0] : MarkupText.Concat(pieces.ToArray());
	}

	/// <summary>What a backslash makes literal: ASCII punctuation, as Markdown has it.</summary>
	private static bool Escapable(char c) => char.IsAscii(c) && (char.IsPunctuation(c) || char.IsSymbol(c));

	/// <summary>
	/// A fenced block (<c>```</c>, its language line dropped) or an inline span of one or two backticks, as written.
	/// </summary>
	private static (string Code, int After)? Code(string text, int at)
	{
		if (string.CompareOrdinal(text, at, "```", 0, 3) == 0
			&& text.IndexOf("```", at + 3, StringComparison.Ordinal) is var fenceEnd and >= 0)
		{
			var block = text[(at + 3)..fenceEnd];
			if (block.IndexOf('\n') is var newline and > 0 && LanguageLine().IsMatch(block[..newline]))
			{
				block = block[(newline + 1)..];
			}

			return (block.Trim('\n'), fenceEnd + 3);
		}

		var ticks = at + 1 < text.Length && text[at + 1] == '`' ? "``" : "`";
		var end = text.IndexOf(ticks, at + ticks.Length, StringComparison.Ordinal);
		return end > at + ticks.Length ? (text[(at + ticks.Length)..end], end + ticks.Length) : null;
	}

	/// <summary>A mention, custom emoji, timestamp, slash command or bracketed address, as the game reads it.</summary>
	private static (string Text, int After)? Entity(string text, int at, DiscordMentionNames names)
	{
		var match = Entities().Match(text, at);
		if (!match.Success)
		{
			return null;
		}

		var groups = match.Groups;
		var read = groups switch
		{
			_ when groups["user"].Success => "@" + Name(names.Users, groups["user"].Value, "unknown-user"),
			_ when groups["role"].Success => "@" + Name(names.Roles, groups["role"].Value, "deleted-role"),
			_ when groups["channel"].Success => "#" + Name(names.Channels, groups["channel"].Value, "unknown-channel"),
			_ when groups["emoji"].Success => $":{groups["emoji"].Value}:",
			_ when groups["command"].Success => "/" + groups["command"].Value,
			_ when groups["time"].Success => Time(groups["time"].Value),
			_ => groups["url"].Value,
		};
		return (read, at + match.Length);
	}

	private static string Name(IReadOnlyDictionary<ulong, string> names, string id, string unknown)
		=> ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var key) && names.TryGetValue(key, out var name)
			? name
			: unknown;

	private static string Time(string seconds)
		=> long.TryParse(seconds, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
			&& value is > -62135596800 and < 253402300800
				? DateTimeOffset.FromUnixTimeSeconds(value).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
				: seconds;

	/// <summary>
	/// Emphasis opened at <paramref name="at"/> and closed later on, with something other than space just inside each
	/// end. A single <c>*</c> or <c>_</c> looks past doubled ones, so <c>*a **b** c*</c> is italic around bold. An
	/// underscore emphasises only between word boundaries, as Discord reads <c>snake_case_names</c>.
	/// </summary>
	private static (AnsiMarkup Style, string Inner, int After)? Emphasised(string text, int at)
	{
		foreach (var (delimiter, style) in Emphasis)
		{
			if (string.CompareOrdinal(text, at, delimiter, 0, delimiter.Length) != 0)
			{
				continue;
			}

			var single = delimiter.Length == 1;
			var start = at + delimiter.Length;
			if (single && delimiter == "_" && at > 0 && char.IsLetterOrDigit(text[at - 1]))
			{
				return null;
			}

			for (var j = start; j < text.Length; j++)
			{
				if (text[j] == '\\')
				{
					j++;
					continue;
				}

				if (single && string.CompareOrdinal(text, j, delimiter + delimiter, 0, 2) == 0)
				{
					j++;
					continue;
				}

				if (string.CompareOrdinal(text, j, delimiter, 0, delimiter.Length) != 0)
				{
					continue;
				}

				var inner = text[start..j];
				var after = j + delimiter.Length;
				if (inner.Length == 0 || char.IsWhiteSpace(inner[0]) || char.IsWhiteSpace(inner[^1])
					|| (delimiter == "_" && after < text.Length && char.IsLetterOrDigit(text[after])))
				{
					continue;
				}

				return (style, inner, after);
			}

			// Only the longest delimiter that opens here is tried: "**" with no closing pair is not two "*".
			return null;
		}

		return null;
	}

	[GeneratedRegex(@"\G<(?:@!?(?<user>\d+)|@&(?<role>\d+)|#(?<channel>\d+)|a?:(?<emoji>\w+):\d+|/(?<command>[\w -]+):\d+|t:(?<time>-?\d+)(?::[tTdDfFR])?|(?<url>https?://[^\s>]+))>")]
	private static partial Regex Entities();

	[GeneratedRegex(@"\G\[(?<text>[^\]\n]+)\]\(<?(?<url>https?://[^\s)>]+)>?\)")]
	private static partial Regex MaskedLink();

	[GeneratedRegex(@"^[\w+#.-]+$")]
	private static partial Regex LanguageLine();
}
