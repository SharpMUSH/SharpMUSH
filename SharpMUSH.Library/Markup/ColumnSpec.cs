using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Markup;

/// <summary>How a column's text sits inside its width.</summary>
public enum Justification { Left, Center, Right, Full, Paragraph }

/// <summary>Per-column behaviour flags parsed out of an <c>align()</c> width token.</summary>
[Flags]
public enum ColumnOptions
{
	Default = 0,
	Repeat = 1,
	MergeToLeft = 2,
	MergeToRight = 4,
	NoFill = 8,
	Truncate = 16,
	TruncateV2 = 32,
	NoColSep = 64,
}

/// <summary>One column of an <c>align()</c> layout.</summary>
public record ColumnSpec(int Width, Justification Justification, ColumnOptions Options, string Ansi);

/// <summary>
/// Parses PennMUSH <c>align()</c> column specifications: an optional justification character, a
/// width, any number of option characters, and an optional parenthesised ANSI string.
/// </summary>
public static partial class ColumnSpecParser
{
	[GeneratedRegex(@"^([<>=_\-])?(\d+)([\.`'$xX#]*)(?:\((.+)\))?$")]
	private static partial Regex WidthPattern();

	public static ColumnSpec Parse(string spec)
	{
		var m = WidthPattern().Match(spec);
		if (!m.Success)
			throw new ArgumentException($"Invalid column specification: {spec}");

		var justification = m.Groups[1].Success
			? m.Groups[1].Value switch
			{
				"<" => Justification.Left,
				"=" => Justification.Paragraph,
				">" => Justification.Right,
				"_" => Justification.Full,
				"-" => Justification.Center,
				_ => Justification.Left,
			}
			: Justification.Left;

		var options = ColumnOptions.Default;
		if (m.Groups[3].Success)
		{
			foreach (var c in m.Groups[3].Value)
			{
				options |= c switch
				{
					'.' => ColumnOptions.Repeat,
					'`' => ColumnOptions.MergeToLeft,
					'\'' => ColumnOptions.MergeToRight,
					'$' => ColumnOptions.NoFill,
					'x' => ColumnOptions.Truncate,
					'X' => ColumnOptions.TruncateV2,
					'#' => ColumnOptions.NoColSep,
					_ => ColumnOptions.Default,
				};
			}
		}

		var width = int.Parse(m.Groups[2].Value);
		var ansi = m.Groups[4].Success ? m.Groups[4].Value : string.Empty;

		return new ColumnSpec(width, justification, options, ansi);
	}

	public static List<ColumnSpec> ParseList(string spec) => spec.Split(' ').Select(Parse).ToList();
}
