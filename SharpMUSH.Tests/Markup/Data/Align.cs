
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
		yield return () => new(
			"30 30",
			[MarkupText.Plain("a"), MarkupText.Plain("b")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a                              b                             ")
		);

		yield return () => new(
			"5 5",
			[MarkupText.Plain("a1\na2"), MarkupText.Plain("b1")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a1    b1   \na2         ")
		);

		yield return () => new(
			"5 5",
			[MarkupText.Plain("a1\na2"), MarkupText.Plain("b1\nb2\nb3")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a1    b1   \na2    b2   \n      b3   ")
		);

		yield return () => new(
			"1. 5 1.",
			[MarkupText.Plain("|"), MarkupText.Plain("this is a test"), MarkupText.Plain("|")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("| this  |\n| is a  |\n| test  |")
		);

		yield return () => new(
			"5 >5",
			[MarkupText.Plain("a1\na2"), MarkupText.Plain("b1\nb2\nb3")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a1       b1\na2       b2\n         b3")
		);

		yield return () => new(
			"5. >5",
			[MarkupText.Plain("a1"), MarkupText.Plain("b1\nb2\nb3")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a1       b1\na1       b2\na1       b3")
		);

		yield return () => new(
			"5 >5.",
			[MarkupText.Plain("a1\na2\na3"), MarkupText.Plain("b1")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a1       b1\na2       b1\na3       b1")
		);

		yield return () => new(
			">30 30",
			[MarkupText.Plain("a"), MarkupText.Plain("b")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("                             a b                             ")
		);

		yield return () => new(
			">30 >30",
			[MarkupText.Plain("a"), MarkupText.Plain("b")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("                             a                              b")
		);

		// Center justification (- prefix means center)
		yield return () => new(
			"-10",
			[MarkupText.Plain("test")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("   test   ")
		);

		// NoFill option ($)
		yield return () => new(
			">15 60$",
			[MarkupText.Plain("Walker"), MarkupText.Plain("Staff & Developer")],
			MarkupText.Plain("x"),
			MarkupText.Plain("x"),
			MarkupText.Plain("\n"),
			MarkupText.Plain("xxxxxxxxxWalkerxStaff & Developer")
		);

		yield return () => new(
			"10 10",
			[MarkupText.Plain("abc"), MarkupText.Plain("def")],
			MarkupText.Plain("-"),
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("abc------- def-------")
		);

		yield return () => new(
			"5 5",
			[MarkupText.Plain("aa"), MarkupText.Plain("bb")],
			MarkupText.Space,
			MarkupText.Plain("|"),
			MarkupText.Plain("\n"),
			MarkupText.Plain("aa   |bb   ")
		);

		yield return () => new(
			"5 5",
			[MarkupText.Plain("a1\na2"), MarkupText.Plain("b1\nb2")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain(" / "),
			MarkupText.Plain("a1    b1    / a2    b2   ")
		);

		// Truncate option (x) - truncates each row
		yield return () => new(
			"5x 5x",
			[MarkupText.Plain("This is a very long text"), MarkupText.Plain("Another long text here")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("This  Anoth")
		);

		// TruncateV2 option (X) - truncates entire column after first row
		yield return () => new(
			"10X 10X",
			[MarkupText.Plain("This is a very long text that wraps"), MarkupText.Plain("Another very long text")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("This is a  Another ve")
		);

		// NoColSep option (#) - no separator after column
		yield return () => new(
			"5# 5",
			[MarkupText.Plain("abc"), MarkupText.Plain("def")],
			MarkupText.Space,
			MarkupText.Plain("|"),
			MarkupText.Plain("\n"),
			MarkupText.Plain("abc  def  ")
		);

		// Full justification (_)
		yield return () => new(
			"_20",
			[MarkupText.Plain("hello world test")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("hello   world   test")
		);

		// MergeToLeft option (`) - empty column merges left, adding its width to left column
		// The empty column still appears in output as spaces
		/* TODO: Failing Test
		yield return () => new(
			"5 5` 10",
			[MarkupText.Plain("aaa"), MarkupText.Empty, MarkupText.Plain("bbb")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("aaa            bbb       ")
		);
		*/

		// MergeToRight option (') - empty column merges right, adding its width to right column
		// The empty column still appears in output as spaces
		/* TODO: FAILING TEST
		yield return () => new(
			"10 5' 5",
			[MarkupText.Plain("aaa"), MarkupText.Empty, MarkupText.Plain("bbb")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("aaa              bbb       ")
		);
		*/

		yield return () => new(
			"1.$ 8 1.$",
			[MarkupText.Plain("+"), MarkupText.Plain("Header"), MarkupText.Plain("+")],
			MarkupText.Plain("-"),
			MarkupText.Empty,
			MarkupText.Plain("\n"),
			MarkupText.Plain("+Header--+")
		);

		yield return () => new(
			"10",
			[MarkupText.Plain("This is a test of word wrapping")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("This is a \ntest of   \nword      \nwrapping  ")
		);

		yield return () => new(
			"10 10",
			[MarkupText.Empty, MarkupText.Plain("text")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("           text      ")
		);

		yield return () => new(
			"1 1 1",
			[MarkupText.Plain("a"), MarkupText.Plain("b"), MarkupText.Plain("c")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("a b c")
		);

		yield return () => new(
			"2. 12 2.",
			[MarkupText.Plain(">>"), MarkupText.Plain("The quick brown fox jumps"), MarkupText.Plain("<<")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain(">> The quick    <<\n>> brown fox    <<\n>> jumps        <<")
		);

		yield return () => new(
			"<10 -10 >10",
			[MarkupText.Plain("left"), MarkupText.Plain("center"), MarkupText.Plain("right")],
			MarkupText.Space,
			MarkupText.Plain("|"),
			MarkupText.Plain("\n"),
			MarkupText.Plain("left      |  center  |     right")
		);

		// Paragraph justification against the two things it is not. Every line is stretched to the
		// column except the one that ends the paragraph, which keeps its natural spacing — so it
		// differs from full justification on the last line, and from right justification on all of
		// them. A single-line case cannot tell these apart, because one line is its own last.
		yield return () => new(
			"=20",
			[MarkupText.Plain("alpha beta gamma delta epsilon zeta")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("alpha   beta   gamma\ndelta epsilon zeta  ")
		);

		yield return () => new(
			"_20",
			[MarkupText.Plain("alpha beta gamma delta epsilon zeta")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("alpha   beta   gamma\ndelta  epsilon  zeta")
		);

		yield return () => new(
			">20",
			[MarkupText.Plain("alpha beta gamma delta epsilon zeta")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("    alpha beta gamma\n  delta epsilon zeta")
		);

		// Paragraph justification across a hard break: each paragraph's own last line is the one
		// left unstretched, not merely the last line of the column.
		yield return () => new(
			"=20",
			[MarkupText.Plain("alpha beta gamma delta\nsecond para here now")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("alpha   beta   gamma\ndelta               \nsecond para here now")
		);

		// Paragraph justification. A line that ends its paragraph keeps its natural spacing rather
		// than being stretched, so a single line is left-aligned. The previous implementation
		// treated '=' as plain right-justification, which is not what PennMUSH means by it.
		yield return () => new(
			"=15",
			[MarkupText.Plain("text")],
			MarkupText.Space,
			MarkupText.Space,
			MarkupText.Plain("\n"),
			MarkupText.Plain("text           ")
		);
	}
}
