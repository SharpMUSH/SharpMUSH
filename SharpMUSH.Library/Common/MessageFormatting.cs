using MarkupString;
using SharpMUSH.Library.Extensions;
using static MarkupString.MStringInterpolation;

namespace SharpMUSH.Library.Common;

public static class MessageFormatting
{
	/// <summary>
	/// Formats an object name with its dbref and flag symbols as a markup-preserving MString.
	/// The name portion is hilighted (white foreground); the dbref and flag symbols are plain text.
	/// Example: an MString with "Chest" hilighted followed by plain "(#5Tn)".
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <returns>A task yielding the formatted MString with the name hilighted</returns>
	public static async ValueTask<MString> FormatObjectWithDbrefMString(Models.SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToArrayAsync();
		var flagSymbols = string.Join(string.Empty, flags.Select(x => x.Symbol));
		return Format($"{obj.Name.Hilight()}(#{obj.DBRef.Number}{flagSymbols})");
	}

	/// <summary>
	/// Formats an object name with its dbref and flag symbols, matching PennMUSH display format.
	/// Example: "Chest(#5Tn)"
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <returns>A task yielding the formatted string</returns>
	public static async ValueTask<string> FormatObjectWithDbref(Models.SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToArrayAsync();
		var flagSymbols = string.Join(string.Empty, flags.Select(x => x.Symbol));
		return $"{obj.Name}(#{obj.DBRef.Number}{flagSymbols})";
	}

	/// <summary>
	/// Formats a list of strings using Oxford comma style.
	/// Examples: ["East"] → "East"; ["East", "West"] → "East and West";
	/// ["East", "West", "North"] → "East, West, and North"
	/// </summary>
	/// <param name="items">The items to format</param>
	/// <returns>The formatted string</returns>
	public static string FormatWithOxfordComma(IReadOnlyList<string> items) => items.Count switch
	{
		0 => string.Empty,
		1 => items[0],
		2 => $"{items[0]} and {items[1]}",
		_ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1]
	};

	/// <summary>
	/// Formats a list of MStrings using Oxford comma style, preserving markup.
	/// </summary>
	public static MString FormatMStringsWithOxfordComma(IReadOnlyList<MString> items) => items.Count switch
	{
		0 => MarkupText.Empty,
		1 => items[0],
		2 => MarkupText.Concat([items[0], MarkupText.Plain(" and "), items[1]]),
		_ => MarkupText.Concat(items.SelectMany<MString, MString>((item, i) => i == 0
				? [item]
				: i < items.Count - 1
					? [MarkupText.Plain(", "), item]
					: [MarkupText.Plain(", and "), item]).ToArray())
	};
}
