using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// <see cref="SoftcodeDecomposer"/> is <c>decompose()</c> without a parser, which the portal's pose editor
/// calls too. Escapes, spaces and code letters are PennMUSH's (<c>escape_marked_str</c>,
/// <c>write_ansi_letters</c>); colour inside colour is written nested, where PennMUSH writes it flat.
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

	[Test]
	public async Task ColourInsideColourIsNested()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)),
				MarkupText.Concat(MarkupText.Plain("a"), MarkupText.Concat(Green("b"), MarkupText.Plain("c"))))))
			.IsEqualTo("[ansi(r,a[ansi(g,b)]c)]");

	[Test]
	public async Task AdjacentColoursAreSideBySide()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Concat(Red("a"), Green("b"))))
			.IsEqualTo("[ansi(r,a)][ansi(g,b)]");

	[Test]
	public async Task CodesAreWrittenInPennsOrder()
	{
		var style = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, true), background: new AnsiColor.Standard(4, false),
			underlined: true, blink: true);
		await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(style, "x"))).IsEqualTo("[ansi(fhuBr,x)]")
			.Because("write_ansi_letters: f h i u, the background letter, then the foreground");
	}

	[Test]
	public async Task AHexBackgroundFollowsABang()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0), background: new AnsiColor.Rgb(0, 0, 255)), "x")))
			.IsEqualTo("[ansi(#ff0000!#0000ff,x)]");

	[Test]
	public async Task ATagIsATagwrap()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(HtmlMarkup.Create("b", null), "bold")))
			.IsEqualTo("[tagwrap(b,bold)]");

	[Test]
	public async Task ATagKeepsItsAttributes()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(HtmlMarkup.Create("a", "href=\"https://x.y/z?a=1,b\""), "go")))
			.IsEqualTo(@"[tagwrap(a,href=""https://x.y/z?a=1\,b"",go)]");

	[Test]
	public async Task ACommandLinkIsACmdlink()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command, linkText: "look"), "here")))
			.IsEqualTo("[cmdlink(here,look)]");

	[Test]
	public async Task ACommandLinkKeepsItsHint_AndItsCommandIsEscaped()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(linkUrl: "say hi, all", linkKind: LinkKind.Command, linkText: "Greet"), "wave")))
			.IsEqualTo(@"[cmdlink(wave,say hi\, all,Greet)]");

	[Test]
	public async Task AColouredCommandLinkIsACmdlinkInsideAnsi()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(linkUrl: "help", linkKind: LinkKind.Command, linkText: "help", underlined: true), "help")))
			.IsEqualTo("[ansi(u,[cmdlink(help,help)])]");

	[Test]
	public async Task AnAddressLinkIsAnAnchor()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(linkUrl: "https://x.y", linkKind: LinkKind.Url, linkText: "https://x.y"), "site")))
			.IsEqualTo(@"[tagwrap(a,href=""https://x.y"",site)]");

	[Test]
	public async Task ATagInsideAColourNests()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Concat(
				MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)),
					MarkupText.Concat(MarkupText.Plain("a"), MarkupText.Wrap(HtmlMarkup.Create("b", null), "b"))),
				MarkupText.Plain("c"))))
			.IsEqualTo("[ansi(r,a[tagwrap(b,b)])]c");

	[Test]
	public async Task AQuoteInAnAddressCannotEndTheAttribute()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(
				AnsiMarkup.Create(linkUrl: "https://x.y/a\"b", linkKind: LinkKind.Url, linkText: "x"), "site")))
			.IsEqualTo(@"[tagwrap(a,href=""https://x.y/a\%22b"",site)]");

	[Test]
	public async Task AnAttributeTurnedOffIsItsCapital()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(AnsiCodeParser.Parse("uU"), "x")))
			.IsEqualTo("[ansi(U,x)]");

	[Test]
	public async Task AnOffCodeInsideItsAttributeNests()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(AnsiCodeParser.Parse("u"),
				MarkupText.Concat(MarkupText.Plain("a"), MarkupText.Wrap(AnsiCodeParser.Parse("U"), "b")))))
			.IsEqualTo("[ansi(u,a[ansi(U,b)])]");

	[Test]
	public async Task HiliteTurnedOffAfterAColourIsTheCapitalAndThePlainColour()
		=> await Assert.That(SoftcodeDecomposer.Decompose(MarkupText.Wrap(AnsiCodeParser.Parse("hrH"), "x")))
			.IsEqualTo("[ansi(Hr,x)]");
}
