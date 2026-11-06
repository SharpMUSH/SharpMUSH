using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// String functions TinyMUX games call that PennMUSH has no name for: <c>sha1()</c>, <c>translate()</c> and
/// <c>wrapcolumns()</c>. Behaviour follows TinyMUX's documentation.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// TinyMUX's <c>sha1()</c>: the SHA-1 digest of the text, without its markup and read as UTF-8, as 40
	/// upper-case hexadecimal digits. <c>digest(sha1,...)</c> gives the same digest in lower case.
	/// </summary>
	[SharpFunction(Name = "sha1", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> Sha1(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? string.Empty;
		return ValueTask.FromResult(new CallState(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))));
	}

	/// <summary>
	/// TinyMUX's <c>translate()</c>. A type of <c>1</c> or <c>p</c> writes the text as the softcode that gives it
	/// back, colour included, as <c>decompose()</c> does: TinyMUX writes <c>%c</c> codes there, which SharpMUSH
	/// reads only at import, as <c>ansi()</c>. Any other type, <c>0</c> or <c>s</c> among them, gives the plain
	/// text with each line break and tab as a space and each carriage return left out.
	/// </summary>
	[SharpFunction(Name = "translate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "type"])]
	public ValueTask<CallState> Translate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty;
		var type = parser.CurrentState.Arguments["1"].Message?.ToPlainText() ?? string.Empty;
		if (type is ['1' or 'p', ..]) return ValueTask.FromResult(new CallState(SoftcodeDecomposer.Decompose(text, HouseTheme())));

		var plain = new StringBuilder();
		foreach (var character in text.ToPlainText())
		{
			switch (character)
			{
				case '\r':
					break;
				case '\n' or '\t':
					plain.Append(' ');
					break;
				default:
					plain.Append(character);
					break;
			}
		}

		return ValueTask.FromResult(new CallState(plain.ToString()));
	}

	/// <summary>
	/// TinyMUX's <c>wrapcolumns()</c>: the text word-wrapped into lines of at most <c>width</c> characters, set out
	/// in <c>columns</c> columns, down each column first (order 0, the default) or across each row (order 1).
	/// Each cell is padded to the width, justified <c>L</c>, <c>R</c> or <c>C</c>, and each row is framed by the
	/// left, middle and right borders. A word longer than the width is cut at it. A line break in the text
	/// starts a new line.
	/// </summary>
	[SharpFunction(Name = "wrapcolumns", MinArgs = 3, MaxArgs = 8, Flags = FunctionFlags.Regular,
		ParameterNames = ["text", "width", "columns", "justify", "lborder", "mborder", "rborder", "order"])]
	public ValueTask<CallState> WrapColumns(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		MString Argument(int index) => args.Count > index ? args[index.ToString()].Message ?? MarkupText.Empty : MarkupText.Empty;

		var limit = Math.Max(1, parser.CurrentState.OutputLimit);
		var width = Math.Clamp(ArgHelpers.ParseInteger(Argument(1).ToPlainText()), 1, limit);
		var columnCount = Math.Clamp(ArgHelpers.ParseInteger(Argument(2).ToPlainText()), 1, Math.Max(1, limit / 2));
		var justify = Argument(3).ToPlainText() is [var first, ..] ? char.ToUpperInvariant(first) : 'L';
		var (left, middle, right) = (Argument(4), Argument(5), Argument(6));
		var acrossFirst = ArgHelpers.ParseInteger(Argument(7).ToPlainText()) != 0;

		var lines = WrapToWidth(Argument(0), width);
		var rowCount = (lines.Count + columnCount - 1) / columnCount;
		// Each row is its borders and cells; rows are joined by line breaks. A row is measured first, so the
		// product cannot overflow.
		var rowLength = left.Length + right.Length + (long)columnCount * width + (columnCount - 1L) * middle.Length;
		if (rowCount > 0 && FunctionLimits.ExceedsOutput(parser.CurrentState, rowLength)
			|| FunctionLimits.ExceedsOutput(parser.CurrentState, rowCount * rowLength + Math.Max(0, rowCount - 1)))
		{
			return ValueTask.FromResult(FunctionLimits.RejectOutput(parser.CurrentState));
		}

		var rows = Enumerable.Range(0, rowCount).Select(row => MarkupText.Concat(
		[
			left,
			MarkupText.Join(middle, Enumerable.Range(0, columnCount).Select(column =>
			{
				var index = acrossFirst ? row * columnCount + column : column * rowCount + row;
				return JustifyCell(index < lines.Count ? lines[index] : MarkupText.Empty, width, justify);
			})),
			right
		]));
		return ValueTask.FromResult(new CallState(MarkupText.Join(MarkupText.Plain("\n"), rows)));
	}

	/// <summary>
	/// The text cut into lines of at most <paramref name="width"/> characters: at the last space that fits, which
	/// the break uses up, or at the width when no space does. Spaces that would open a line are dropped. The
	/// positions are found in the plain text and each line is cut from the text once, so a point stays with the
	/// line it was in: among the dropped spaces on that line, after the last one on the next.
	/// </summary>
	private static List<MString> WrapToWidth(MString text, int width)
	{
		var lines = new List<MString>();
		foreach (var paragraph in MushText.SplitList(MarkupText.Plain("\n"), text))
		{
			var plain = paragraph.ToPlainText();
			if (plain.Length == 0)
			{
				if (paragraph.Length > 0) lines.Add(paragraph);
				continue;
			}

			var offsets = VisibleOffsets(paragraph, plain);
			var (start, textStart) = (0, 0);
			while (start < plain.Length)
			{
				var (length, advance) = plain.Length - start <= width
					? (plain.Length - start, plain.Length - start)
					: plain.LastIndexOf(' ', start + width, width + 1) is var space && space > start
						? (space - start, space - start + 1)
						: (width, width);

				var skip = start + advance;
				while (skip < plain.Length && plain[skip] == ' ') skip++;

				var end = offsets[start + length];
				var next = skip > start + length ? offsets[skip - 1] + 1 : end;
				lines.Add(MarkupText.Concat([paragraph.Substring(textStart, end - textStart), .. Carriers(paragraph, end, next)]));
				textStart = next;
				start = skip;
			}

			// Points after the last visible character, past spaces the last break dropped, end the last line.
			if (textStart < paragraph.Length) lines[^1] = MarkupText.Concat([lines[^1], .. Carriers(paragraph, textStart, paragraph.Length)]);
		}

		return lines;
	}

	/// <summary>
	/// The carriers of the points among the dropped spaces from <paramref name="from"/> to <paramref name="to"/>
	/// in the text: every character there that is not one of those spaces.
	/// </summary>
	private static IEnumerable<MString> Carriers(MString text, int from, int to)
	{
		for (var index = from; index < to; index++)
		{
			if (text.Text[index] != ' ') yield return text.Substring(index, 1);
		}
	}

	private static MString JustifyCell(MString cell, int width, char justify)
	{
		var padding = Math.Max(0, width - cell.DisplayWidth);
		var (before, after) = justify switch
		{
			'R' => (padding, 0),
			'C' => (padding / 2, padding - padding / 2),
			_ => (0, padding)
		};

		return MarkupText.Concat(
			[MarkupText.Plain(new string(' ', before)), cell, MarkupText.Plain(new string(' ', after))]);
	}
}
