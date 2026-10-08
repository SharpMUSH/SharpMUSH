using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// How a feed is named in <c>@feed</c> and the feed functions: <c>&lt;kind&gt;</c> or <c>&lt;kind&gt;/&lt;key&gt;</c>,
/// both lower case. A kind is a word (<c>radio</c>, <c>text</c>); a key is anything without a slash, a space or
/// a control character (<c>101.5</c>, <c>#3:1700000000+#5:1700000001</c>).
/// </summary>
public static partial class FeedNames
{
	/// <summary>What a tap names to hear every kind.</summary>
	public const string Every = "*";

	/// <summary>
	/// The engine's own kind for channel recall (<see cref="Services.FeedChannelBufferService"/>). It has no kind
	/// row, so <c>@feed</c> and the feed functions never reach it, and no softcode kind may take the name.
	/// </summary>
	public const string Channel = "channel";

	public const int MaxKindLength = 32;
	public const int MaxKeyLength = 200;

	[GeneratedRegex("^[a-z][a-z0-9_.-]*$")]
	private static partial Regex KindPattern();

	public static bool IsKind(string kind) => kind.Length <= MaxKindLength && KindPattern().IsMatch(kind);

	public static bool IsKey(string key)
		=> key.Length is > 0 and <= MaxKeyLength && !key.Any(c => c == '/' || char.IsWhiteSpace(c) || char.IsControl(c));

	/// <summary>
	/// Splits <c>&lt;kind&gt;[/&lt;key&gt;]</c>, lower-cased. <paramref name="key"/> is null for a kind alone. False
	/// when either part is not a valid name.
	/// </summary>
	public static bool TryParse(string text, out string kind, out string? key)
	{
		var parts = text.Trim().ToLowerInvariant().Split('/', 2);
		kind = parts[0];
		key = parts.Length > 1 ? parts[1] : null;
		return IsKind(kind) && (key is null || IsKey(key));
	}

	/// <summary>
	/// Splits <c>&lt;kind&gt;[/&lt;key&gt;]/&lt;option&gt;</c>, as <c>@feed/set</c> and <c>@feed/lock</c> take it: the
	/// last part is the option, the rest the target.
	/// </summary>
	public static bool TryParseOption(string text, out string kind, out string? key, out string option)
	{
		var parts = text.Trim().ToLowerInvariant().Split('/');
		kind = parts[0];
		key = parts.Length == 3 ? parts[1] : null;
		option = parts.Length > 1 ? parts[^1] : "";
		return parts.Length is 2 or 3 && IsKind(kind) && (key is null || IsKey(key)) && option.Length > 0;
	}
}
