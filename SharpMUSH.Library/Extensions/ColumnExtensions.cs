using MarkupString;

namespace SharpMUSH.Library.Extensions;

/// <summary>
/// Padding for plain strings that lands on the column a terminal actually draws.
/// </summary>
/// <remarks>
/// <see cref="string.PadRight(int)"/> counts UTF-16 code units, which is not what a listing
/// lines up on: a player name carrying a combining mark or a wide character measures one thing
/// to .NET and another to the person reading it. Everything that measures text for layout in
/// this codebase goes through MarkupString so there is one answer to how wide a string is.
/// </remarks>
public static class ColumnExtensions
{
	/// <summary>
	/// Pads <paramref name="text"/> out to <paramref name="columns"/> display cells. Text already
	/// that wide is returned unchanged, as <see cref="string.PadRight(int)"/> would.
	/// </summary>
	public static string PadToColumns(this string text, int columns) =>
		MarkupText.Plain(text)
			.Pad(MarkupText.Space, columns, PadType.Right, TruncationType.Overflow)
			.ToPlainText();
}
