using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// <see cref="SoftcodeDecomposer"/> is <c>decompose()</c> without a parser, which the portal's pose editor
/// calls too. The expected values are PennMUSH's (the same as <c>FunctionFamilyConformanceTests</c> record).
/// </summary>
public class SoftcodeDecomposerTests
{
	private static MarkupText Red(string text) => MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), text);

	private static MarkupText Green(string text) => MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false)), text);

	[Test]
	public async Task AColouredStretchIsAnAnsiCallInBrackets()
		=> await Assert.That(SoftcodeDecomposer.Decompose(Red("red"))).IsEqualTo("[ansi(r,red)]");

	[Test]
	public async Task TheTextIsEscapedInsideTheCallAndOut()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Concat(Red("a,b[c]"), MarkupText.Plain(";d%e"))))
			.IsEqualTo(@"[ansi(r,a\,b\[c\])]\;d\%e");

	[Test]
	public async Task ASpaceBesideAColourIsProtected()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Concat(MarkupText.Plain("a "), MarkupText.Concat(Red("b"), MarkupText.Plain(" c")))))
			.IsEqualTo("a%b[ansi(r,b)]%bc");

	[Test]
	public async Task ASpaceBetweenTwoColoursIsProtected()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Concat(Red("alpha"), MarkupText.Concat(MarkupText.Plain(" "), Green("apex")))))
			.IsEqualTo("[ansi(r,alpha)]%b[ansi(g,apex)]");

	[Test]
	[Arguments("one two", "one two")]
	[Arguments(" lead", "%blead")]
	[Arguments("trail ", "trail%b")]
	[Arguments("a  b", "a %bb")]
	[Arguments("a     b", "a[space(5)]b")]
	[Arguments("line\nnext\tcol", "line%rnext%tcol")]
	public async Task PlainTextFollowsPennsSpaceAndLineRules(string text, string expected)
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Plain(text))).IsEqualTo(expected);

	[Test]
	public async Task AnUnderlineAndAColourShareOneCode()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(foreground: new AnsiColor.Standard(4, false), underlined: true), "x")))
			.IsEqualTo("[ansi(ub,x)]");
}
