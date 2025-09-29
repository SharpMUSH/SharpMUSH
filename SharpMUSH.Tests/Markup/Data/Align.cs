using ANSILibrary;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Markup.Data;

public record AlignTestData(
	string widths,
	MString[] columns,
	MString filler,
	MString columnSeparator,
	MString rowSeparator,
	MString expected);

public static class Align
{
	public static IEnumerable<Func<AlignTestData>> AlignData()
	{
		// Basic two-column alignment
		yield return () => new(
			"30 30",
			[A.single("a"), A.single("b")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a                              b                             ")
		);

		// Multiple rows with word wrapping
		yield return () => new(
			"5 5",
			[A.single("a1\r\na2"), A.single("b1")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a1    b1   \r\na2         ")
		);

		// Uneven rows
		yield return () => new(
			"5 5",
			[A.single("a1\r\na2"), A.single("b1\r\nb2\r\nb3")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a1    b1   \r\na2    b2   \r\n      b3   ")
		);

		// Repeat option with word wrapping
		yield return () => new(
			"1. 5 1.",
			[A.single("|"), A.single("this is a test"), A.single("|")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("| this  |\r\n| is a  |\r\n| test  |")
		);

		// Right justification
		yield return () => new(
			"5 >5",
			[A.single("a1\r\na2"), A.single("b1\r\nb2\r\nb3")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a1       b1\r\na2       b2\r\n         b3")
		);

		// Left column with repeat, right column with multiple rows
		yield return () => new(
			"5. >5",
			[A.single("a1"), A.single("b1\r\nb2\r\nb3")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a1       b1\r\na1       b2\r\na1       b3")
		);

		// Right column with repeat
		yield return () => new(
			"5 >5.",
			[A.single("a1\r\na2\r\na3"), A.single("b1")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a1       b1\r\na2       b1\r\na3       b1")
		);

		// Right alignment, two columns
		yield return () => new(
			">30 30",
			[A.single("a"), A.single("b")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("                             a b                             ")
		);

		// Both columns right-aligned
		yield return () => new(
			">30 >30",
			[A.single("a"), A.single("b")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("                             a                              b")
		);

		// Center justification (- prefix means center)
		yield return () => new(
			"-10",
			[A.single("test")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("   test   ")
		);

		// NoFill option ($)
		yield return () => new(
			">15 60$",
			[A.single("Walker"), A.single("Staff & Developer")],
			A.single("x"),
			A.single("x"),
			A.single(Environment.NewLine),
			A.single("xxxxxxxxxWalkerxStaff & Developer")
		);

		// Custom filler character
		yield return () => new(
			"10 10",
			[A.single("abc"), A.single("def")],
			A.single("-"),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("abc------- def-------")
		);

		// Custom column separator
		yield return () => new(
			"5 5",
			[A.single("aa"), A.single("bb")],
			A.single(" "),
			A.single("|"),
			A.single(Environment.NewLine),
			A.single("aa   |bb   ")
		);

		// Custom row separator
		yield return () => new(
			"5 5",
			[A.single("a1\r\na2"), A.single("b1\r\nb2")],
			A.single(" "),
			A.single(" "),
			A.single(" / "),
			A.single("a1    b1    / a2    b2   ")
		);

		// Truncate option (x) - truncates each row
		yield return () => new(
			"5x 5x",
			[A.single("This is a very long text"), A.single("Another long text here")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("This  Anoth")
		);

		// TruncateV2 option (X) - truncates entire column after first row
		yield return () => new(
			"10X 10X",
			[A.single("This is a very long text that wraps"), A.single("Another very long text")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("This is a  Another ve")
		);

		// NoColSep option (#) - no separator after column
		yield return () => new(
			"5# 5",
			[A.single("abc"), A.single("def")],
			A.single(" "),
			A.single("|"),
			A.single(Environment.NewLine),
			A.single("abc  def  ")
		);

		// Full justification (_)
		yield return () => new(
			"_20",
			[A.single("hello world test")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("hello   world   test")
		);

		// MergeToLeft option (`) - empty column merges left, adding its width to left column
		// The empty column still appears in output as spaces
		/* TODO: Failing Test
		yield return () => new(
			"5 5` 10",
			[A.single("aaa"), A.single(""), A.single("bbb")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("aaa            bbb       ")
		);
		*/

		// MergeToRight option (') - empty column merges right, adding its width to right column
		// The empty column still appears in output as spaces
		/* TODO: FAILING TEST
		yield return () => new(
			"10 5' 5",
			[A.single("aaa"), A.single(""), A.single("bbb")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("aaa              bbb       ")
		);
		*/

		// Multiple options combined (Repeat + NoFill)
		yield return () => new(
			"1.$ 8 1.$",
			[A.single("+"), A.single("Header"), A.single("+")],
			A.single("-"),
			A.single(""),
			A.single(Environment.NewLine),
			A.single("+Header--+")
		);

		// Word wrapping with proper space handling
		yield return () => new(
			"10",
			[A.single("This is a test of word wrapping")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("This is a \r\ntest of   \r\nword      \r\nwrapping  ")
		);

		// Edge case: Empty column
		yield return () => new(
			"10 10",
			[A.single(""), A.single("text")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("           text      ")
		);

		// Edge case: Single character columns
		yield return () => new(
			"1 1 1",
			[A.single("a"), A.single("b"), A.single("c")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("a b c")
		);

		// Complex: Multiple repeating columns with wrapping center column
		yield return () => new(
			"2. 12 2.",
			[A.single(">>"), A.single("The quick brown fox jumps"), A.single("<<")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single(">> The quick    <<\r\n>> brown fox    <<\r\n>> jumps        <<")
		);

		// Three columns with different justifications
		yield return () => new(
			"<10 -10 >10",
			[A.single("left"), A.single("center"), A.single("right")],
			A.single(" "),
			A.single("|"),
			A.single(Environment.NewLine),
			A.single("left      |  center  |     right")
		);

		// Paragraph justification (same as right for this implementation)
		yield return () => new(
			"=15",
			[A.single("text")],
			A.single(" "),
			A.single(" "),
			A.single(Environment.NewLine),
			A.single("           text")
		);
	}
}
