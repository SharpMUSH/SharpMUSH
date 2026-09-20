using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class WebFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// PennMUSH urlencode() uses libcurl's curl_easy_escape: RFC 3986 percent-encoding,
	// space -> %20 (not +), only A-Za-z0-9-._~ left unescaped, hex digits uppercased.
	// (\% in the input keeps a percent literal past the MUSH substitution layer.)
	[Test]
	[Arguments(@"urlencode(test string)", "test%20string")]
	[Arguments(@"urlencode(a/b c)", "a%2Fb%20c")]
	[Arguments(@"urlencode(a+b)", "a%2Bb")]
	[Arguments(@"urlencode(a&b=c)", "a%26b%3Dc")]
	[Arguments(@"urlencode(foo-_.~bar)", "foo-_.~bar")]
	[Arguments(@"urlencode(100\%)", "100%25")]
	public async Task Urlencode(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// PennMUSH urldecode() uses libcurl's curl_easy_unescape: only %XX is decoded; a literal
	// '+' is left untouched (unlike form decoding); non-printable decoded bytes become '?'.
	[Test]
	[Arguments(@"urldecode(test\%20string)", "test string")]
	[Arguments(@"urldecode(a\%2Fb\%20c)", "a/b c")]
	[Arguments(@"urldecode(test+string)", "test+string")]
	[Arguments(@"urldecode(a\%2Fb+c)", "a/b+c")]
	[Arguments(@"urldecode(100\%25)", "100%")]
	[Arguments(@"urldecode(a\%09b)", "a?b")]
	public async Task Urldecode(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>wshtml(&lt;html&gt;)</c> turns an HTML fragment into markup: text nodes become the text, elements
	/// become <c>HtmlMarkup</c> layers over what they enclose. It sends nothing and takes no fallback —
	/// the plain reading of the value is its own text, so a client without HTML gets that.
	/// </summary>
	[Test]
	[Arguments("wshtml(<b>bold</b> and <i>italic</i>)", "bold and italic")]
	[Arguments("wshtml(plain)", "plain")]
	[Arguments("wshtml(<b>x<i>y</i>z</b>)", "xyz")]
	[Arguments("wshtml(&lt;3&gt; fish &amp; chips &#33;)", "<3> fish & chips !")]
	[Arguments("wshtml(one<br>two)", "one\ntwo")]
	[Arguments("wshtml(<!-- note -->kept)", "kept")]
	[Arguments("wshtml()", "")]
	public async Task WshtmlReturnsTheFragmentsTextAsItsPlainReading(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("wshtml(<b>bold</b> and <i>italic</i>)", "<b>bold</b> and <i>italic</i>")]
	// Markup is runs, not a tree: the emitter opens and closes the outer tag around each run it
	// covers, which is equivalent HTML, not minimal HTML.
	[Arguments("wshtml(<b>x<i>y</i>z</b>)", "<b>x</b><b><i>y</i></b><b>z</b>")]
	[Arguments("wshtml(<a href=\"https://sharpmush.com\" title='home'>SharpMUSH</a>)", "<a href=\"https://sharpmush.com\" title=\"home\">SharpMUSH</a>")]
	[Arguments("wshtml(&lt;3&gt; &amp; &quot;)", "&lt;3&gt; &amp; \"")]
	[Arguments("wshtml(<B>upper</B>)", "<b>upper</b>")]
	public async Task WshtmlRendersBackToTheSameHtml(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message!;
		await Assert.That(result.Render(MarkupFormat.Html)).IsEqualTo(expected);
	}

	/// <summary>
	/// The same value on a terminal: the tags the ANSI fold understands become styling, the rest
	/// vanish, and no client that negotiated neither Pueblo nor MXP ever sees a literal <c>&lt;</c>.
	/// </summary>
	[Test]
	public async Task WshtmlDegradesToStylingOrTextOnATerminal()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("wshtml(<b>bold</b> <a href=\"https://x\">link</a>)")))!.Message!;

		// The emitter's own SGR for bold rather than ansi()'s: ansi(h,...) is not bold — h on its own
		// modifies the colour letter after it (hr is bright red) and carries nothing alone.
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo("\u001b[1mbold\u001b[0m link");
		await Assert.That(result.ToPlainText()).IsEqualTo("bold link");
		await Assert.That(result.Render(MarkupFormat.Plain)).IsEqualTo(result.ToPlainText());
	}

	/// <summary>
	/// The one thing the layer model cannot say is an element over nothing, so an empty element is
	/// dropped rather than invented, and <c>&lt;br&gt;</c> — the one void element with a plain reading —
	/// is the line break it means.
	/// </summary>
	[Test]
	[Arguments("wshtml(a<b></b>c)", "ac")]
	[Arguments("wshtml(a<hr>c)", "ac")]
	[Arguments("wshtml(a<br/>c)", "a\nc")]
	public async Task WshtmlDropsWhatHasNothingToCover(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message!;
		await Assert.That(result.Render(MarkupFormat.Html)).IsEqualTo(expected);
	}

	/// <summary>
	/// Malformed HTML is read the way a browser reads it: repaired, and rendered back as the repair.
	/// </summary>
	[Test]
	[Arguments("wshtml(<b>unclosed)", "<b>unclosed</b>")]
	[Arguments("wshtml(<b>x</i>y</b>)", "<b>xy</b>")]
	[Arguments("wshtml(x</b>)", "x")]
	[Arguments("wshtml(a < b)", "a &lt; b")]
	[Arguments("wshtml(<1bad>x)", "&lt;1bad&gt;x")]
	[Arguments("wshtml(<!DOCTYPE html>x)", "x")]
	public async Task WshtmlRepairsMalformedHtmlTheWayABrowserDoes(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message!;
		await Assert.That(result.Render(MarkupFormat.Html)).IsEqualTo(expected);
	}

	/// <summary>
	/// The gate is <c>tagwrap()</c>'s: without Send_OOB (the fixtures run as God, so this drives a
	/// mortal), only PennMUSH's tags and only parameters a browser cannot be made to run, with one
	/// forbidden tag refusing the whole fragment and a forbidden parameter dropping them all.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task WshtmlHoldsAMortalToTheTagwrapPolicy()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var connections = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, mediator, connections, "WsHtmlGate");
		var parser = WebAppFactoryArg.FunctionParserFor(mortal.DbRef);

		// A bare ( ) inside a function argument is a MUSH grouping and would end the call; escaped, it is text.
		var refused = await parser.FunctionParse(MarkupText.Plain(@"wshtml(<b>ok</b><script>alert\(1\)</script>)"));
		await Assert.That(refused!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);

		var stripped = await parser.FunctionParse(MarkupText.Plain(@"wshtml(<a href=""javascript:alert\(1\)"" onclick=""x\(\)"">link</a>)"));
		await Assert.That(stripped!.Message!.Render(MarkupFormat.Html)).IsEqualTo("<a>link</a>");

		var allowed = await parser.FunctionParse(MarkupText.Plain("wshtml(<a href=\"https://sharpmush.com\">link</a>)"));
		await Assert.That(allowed!.Message!.Render(MarkupFormat.Html)).IsEqualTo("<a href=\"https://sharpmush.com\">link</a>");

		// God is a wizard: the gate lifts and the fragment goes through as written.
		var privileged = await Parser.FunctionParse(MarkupText.Plain(@"wshtml(<script>alert\(1\)</script>)"));
		await Assert.That(privileged!.Message!.Render(MarkupFormat.Html)).IsEqualTo("<script>alert(1)</script>");
	}

	/// <summary>
	/// The value is ordinary markup, so it stores, slices and re-evaluates like anything else — which
	/// is the point of building HTML this way rather than as a deferred choice.
	/// </summary>
	[Test]
	public async Task WshtmlOutputIsOrdinaryMarkupThatSurvivesStringFunctions()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("mid(wshtml(<b>bold</b> and <i>italic</i>),0,8)")))!.Message!;
		await Assert.That(result.Render(MarkupFormat.Html)).IsEqualTo("<b>bold</b> and");
	}

	/// <summary>
	/// wsjson() is gone: JSON is data for a program, not text with a plain reading, and <c>oob()</c> is
	/// the function that sends it where a connection can receive it.
	/// </summary>
	[Test]
	public async Task WsjsonIsNotAFunction()
	{
		await Assert.That(RegistryInventory.Functions.Select(f => f.Name))
			.DoesNotContain("wsjson", StringComparer.OrdinalIgnoreCase);
		// An unregistered name is not a call at all; the text passes through as written.
		var result = (await Parser.FunctionParse(MarkupText.Plain("wsjson(x)")))!.Message!.ToPlainText();
		await Assert.That(result).IsEqualTo("wsjson(x)");
	}

	[Test]
	[Arguments("oob(test)", "")]
	public async Task Oob(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("oob(me, room.contents)", "0")]
	public async Task OobNoConnectionReturnsZero(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("pueblo()", "0")]
	public async Task Pueblo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ssl(%#)", "0")]
	public async Task Ssl(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("terminfo(%#)", "")]
	public async Task Terminfo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("width(%#)", "78")]
	public async Task Width(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
