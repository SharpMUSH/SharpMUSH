using MarkupString.Ansi;

public class AnsiRenderTests
{
	private const string Esc = "\u001b";
	private const string Bel = "\u0007";

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi();

	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));
	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);
	private static readonly AnsiMarkup RedBold = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false), bold: true);
	private static readonly AnsiMarkup Underline = AnsiMarkup.Create(underlined: true);

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	// ── ANSI ─────────────────────────────────────────────────────────────────────

	[Test]
	public async Task Ansi_SingleStyledRun_WritesSgrAndTrailingReset()
	{
		var text = MarkupText.Wrap(Red, "a");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[31ma{Esc}[0m");
	}

	[Test]
	public async Task Ansi_NestedLayers_FoldIntoOneSequence()
	{
		var text = MarkupText.Wrap(Underline, MarkupText.Wrap(Red, MarkupText.Wrap(Bold, "hello")));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[1;4;31mhello{Esc}[0m");
	}

	[Test]
	public async Task Ansi_RunsSeparatedByPlainText_ResetAroundTheGap()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "a"), MarkupText.Plain("-"), MarkupText.Wrap(Red, "b")]);
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[31ma{Esc}[0m-{Esc}[31mb{Esc}[0m");
	}

	[Test]
	public async Task Ansi_AdjacentIncompatibleRuns_ResetBetweenThem()
	{
		var text = MarkupText.Concat(MarkupText.Wrap(Red, "a"), MarkupText.Wrap(Bold, "b"));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[31ma{Esc}[0m{Esc}[1mb{Esc}[0m");
	}

	[Test]
	public async Task Ansi_AdjacentAdditiveRuns_WriteOnlyTheDifference()
	{
		var text = MarkupText.Concat(MarkupText.Wrap(Red, "a"), MarkupText.Wrap(RedBold, "b"));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[31ma{Esc}[1mb{Esc}[0m");
	}

	[Test]
	public async Task Ansi_PlainText_PassesThroughUnencoded()
	{
		await Assert.That(Render(MarkupText.Plain("a < b & c"), MarkupFormat.Ansi)).IsEqualTo("a < b & c");
	}

	[Test]
	public async Task Ansi_UrlLink_WritesOsc8()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "http://x"), "a");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}]8;;http://x{Bel}a{Esc}]8;;{Bel}");
	}

	[Test]
	public async Task Ansi_CommandLink_RendersAsPlainText()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "a");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo("a");
	}

	[Test]
	public async Task Ansi_UnsafeUrl_RendersAsPlainText()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "javascript:alert(1)"), "click");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo("click");
	}

	[Test]
	public async Task Ansi_ColouredUrlLink_KeepsBothSgrAndOsc8()
	{
		var text = MarkupText.Wrap(
			AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false), linkUrl: "http://x"), "a");
		await Assert.That(Render(text, MarkupFormat.Ansi))
			.IsEqualTo($"{Esc}[31m{Esc}]8;;http://x{Bel}a{Esc}]8;;{Bel}{Esc}[0m");
	}

	[Test]
	public async Task Ansi_AlternatingRuns_StayLinearInTheNumberOfRuns()
	{
		var parts = new MarkupText[200];
		for (var i = 0; i < parts.Length; i++) parts[i] = MarkupText.Wrap(i % 2 == 0 ? Red : Bold, "x");
		var text = MarkupText.Concat(parts.AsSpan());

		var rendered = Render(text, MarkupFormat.Ansi);

		// Worst case per run: a reset (4) + the longest SGR here (5) + the character itself,
		// plus one trailing reset for the whole text.
		await Assert.That(text.Runs.Length).IsEqualTo(200);
		await Assert.That(rendered.Length).IsLessThanOrEqualTo(200 * 10 + 4);
	}

	[Test]
	public async Task Ansi_UnstyledMarkup_WritesNothingAroundTheBody()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(), "plain");
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo("plain");
	}

	// ── HTML ─────────────────────────────────────────────────────────────────────

	[Test]
	public async Task Html_XtermForeground_IsResolvedToHex()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(200)), "x");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<span style=\"color: #ff00d7\">x</span>");
	}

	[Test]
	public async Task Html_DefaultForeground_CarriesNoStyle()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(foreground: AnsiColor.Default.Instance), "x");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("x");
	}

	[Test]
	public async Task Html_HighlightedRed_IsTheBrightVariant()
	{
		var text = MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "x");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<span style=\"color: #ff5555\">x</span>");
	}

	[Test]
	public async Task Html_Attributes_BecomeClasses()
	{
		var markup = AnsiMarkup.Create(
			bold: true, faint: true, italic: true, underlined: true, strikeThrough: true, overlined: true, blink: true);
		var text = MarkupText.Wrap(markup, "x");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span class=\"ms-bold ms-faint ms-italic ms-underline ms-strike ms-overline ms-blink\">x</span>");
	}

	[Test]
	public async Task Html_ColourAndAttribute_ShareOneSpan()
	{
		var markup = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0), background: new AnsiColor.Rgb(0, 0, 255), bold: true);
		var text = MarkupText.Wrap(markup, "x");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"color: #ff0000; background-color: #0000ff\" class=\"ms-bold\">x</span>");
	}

	[Test]
	public async Task Html_Inverted_SwapsForegroundAndBackground()
	{
		var markup = AnsiMarkup.Create(
			foreground: new AnsiColor.Rgb(255, 0, 0), background: new AnsiColor.Rgb(0, 0, 255), inverted: true);
		var text = MarkupText.Wrap(markup, "x");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"color: #0000ff; background-color: #ff0000\">x</span>");
	}

	[Test]
	public async Task Html_InvertedWithOnlyForeground_MovesItToTheBackground()
	{
		var markup = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0), inverted: true);
		var text = MarkupText.Wrap(markup, "x");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"background-color: #ff0000\">x</span>");
	}

	[Test]
	public async Task Html_UrlLink_UsesAnAnchorWithSafeRel()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "http://x"), "a");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<a href=\"http://x\" target=\"_blank\" rel=\"noopener noreferrer\">a</a>");
	}

	[Test]
	public async Task Html_CommandLink_UsesXchCmd()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "a");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"look\">a</a>");
	}

	[Test]
	public async Task Html_CommandLinkWithHint_EmitsATitle()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who?"), "a");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"+who\" title=\"Who?\">a</a>");
	}

	[Test]
	public async Task Html_LinkAttributes_AreEncoded()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "say \"hi\" & <bye>", linkKind: LinkKind.Command), "x");
		await Assert.That(Render(text, MarkupFormat.Html))
			.Contains("xch_cmd=\"say &quot;hi&quot; &amp; &lt;bye&gt;\"");
	}

	[Test]
	public async Task Html_UnsafeUrl_RendersAsPlainText()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "javascript:alert(1)"), "click");
		var html = Render(text, MarkupFormat.Html);
		await Assert.That(html).IsEqualTo("click");
	}

	[Test]
	public async Task Html_ColouredCommandLink_KeepsBoth()
	{
		var markup = AnsiMarkup.Create(
			foreground: new AnsiColor.Rgb(255, 0, 0), linkUrl: "look", linkKind: LinkKind.Command);
		var text = MarkupText.Wrap(markup, "a");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"color: #ff0000\"><a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"look\">a</a></span>");
	}

	[Test]
	public async Task Html_BodyText_IsEncoded()
	{
		var text = MarkupText.Wrap(Red, "<b>x</b>");
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"color: #aa0000\">&lt;b&gt;x&lt;/b&gt;</span>");
	}

	[Test]
	public async Task Html_UnstyledMarkup_EmitsNoSpan()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(), "x");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("x");
	}

	[Test]
	public async Task Html_LoneInverted_EmitsTheInvertClass()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(inverted: true), "x");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<span class=\"ms-invert\">x</span>");
	}

	[Test]
	public async Task Html_InvertedWithColours_SwapsWithoutTheInvertClass()
	{
		var markup = AnsiMarkup.Create(
			foreground: new AnsiColor.Rgb(255, 0, 0), background: new AnsiColor.Rgb(0, 0, 255), inverted: true);
		var text = MarkupText.Wrap(markup, "x");
		var html = Render(text, MarkupFormat.Html);
		await Assert.That(html).IsEqualTo("<span style=\"color: #0000ff; background-color: #ff0000\">x</span>");
		await Assert.That(html).DoesNotContain("ms-invert");
	}

	// ── Pueblo and MXP ───────────────────────────────────────────────────────────

	[Test]
	public async Task PuebloAndMxp_Colour_IsWrittenAsSgr()
	{
		var text = MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "x");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo($"{Esc}[1;31mx{Esc}[0m");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo($"{Esc}[1;31mx{Esc}[0m");
	}

	[Test]
	public async Task PuebloAndMxp_PlainText_IsHtmlEncoded()
	{
		var text = MarkupText.Plain("Tom & \"Sue\"");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("Tom &amp; &quot;Sue&quot;");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("Tom &amp; &quot;Sue&quot;");
	}

	[Test]
	public async Task Pueblo_CommandLink_UsesXchCmd()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command, linkText: "h"), "a");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("<A XCH_CMD=\"look\" XCH_HINT=\"h\">a</A>");
	}

	[Test]
	public async Task Mxp_CommandLink_UsesSend()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command, linkText: "h"), "a");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("<SEND HREF=\"look\" HINT=\"h\">a</SEND>");
	}

	[Test]
	public async Task Pueblo_CommandLinkWithoutHint_OmitsTheHintAttribute()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "a");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("<A XCH_CMD=\"look\">a</A>");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("<SEND HREF=\"look\">a</SEND>");
	}

	[Test]
	public async Task PuebloAndMxp_UrlLink_UsesAnAnchor()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "http://x"), "a");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("<A HREF=\"http://x\">a</A>");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("<A HREF=\"http://x\">a</A>");
	}

	[Test]
	public async Task PuebloAndMxp_UnsafeUrl_RendersAsPlainText()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "javascript:alert(1)"), "click");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo("click");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo("click");
	}

	[Test]
	public async Task PuebloAndMxp_ClearOnly_WritesExactlyOneReset()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(clear: true), "x");
		await Assert.That(Render(text, MarkupFormat.Pueblo)).IsEqualTo($"{Esc}[0mx");
		await Assert.That(Render(text, MarkupFormat.Mxp)).IsEqualTo($"{Esc}[0mx");
	}

	[Test]
	public async Task Pueblo_ColouredCommandLink_KeepsBoth()
	{
		var markup = AnsiMarkup.Create(
			foreground: new AnsiColor.Standard(1, false), linkUrl: "+who", linkKind: LinkKind.Command);
		var text = MarkupText.Wrap(markup, "who");
		await Assert.That(Render(text, MarkupFormat.Pueblo))
			.IsEqualTo($"{Esc}[31m<A XCH_CMD=\"+who\">who</A>{Esc}[0m");
	}

	// ── BBCode ───────────────────────────────────────────────────────────────────

	[Test]
	public async Task BBCode_ColourAndBold_Nest()
	{
		var text = MarkupText.Wrap(RedBold, "x");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[color=#aa0000][b]x[/b][/color]");
	}

	[Test]
	public async Task BBCode_AllWrappers_NestInnermostFirst()
	{
		var markup = AnsiMarkup.Create(bold: true, italic: true, underlined: true, strikeThrough: true);
		var text = MarkupText.Wrap(markup, "x");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[b][i][u][s]x[/s][/u][/i][/b]");
	}

	[Test]
	public async Task BBCode_UrlLink_UsesTheUrlTag()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "http://x"), "a");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[url=http://x]a[/url]");
	}

	[Test]
	public async Task BBCode_CommandLink_RendersAsPlainText()
	{
		var text = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "a");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("a");
	}

	[Test]
	public async Task BBCode_Inverted_UsesTheBackgroundAsTheColour()
	{
		var markup = AnsiMarkup.Create(background: new AnsiColor.Rgb(0, 0, 255), inverted: true);
		var text = MarkupText.Wrap(markup, "x");
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[color=#0000ff]x[/color]");
	}

	// ── Plain ────────────────────────────────────────────────────────────────────

	[Test]
	public async Task Plain_StripsEveryStyle()
	{
		var text = MarkupText.Concat(MarkupText.Wrap(RedBold, "a"), MarkupText.Wrap(Bold, "b"));
		await Assert.That(Render(text, MarkupFormat.Plain)).IsEqualTo("ab");
	}

	// ── Safety ───────────────────────────────────────────────────────────────────

	[Test]
	[Arguments("https://example.com", true)]
	[Arguments("http://example.com", true)]
	[Arguments("mailto:a@b.com", true)]
	[Arguments("ftp://example.com", true)]
	[Arguments("tel:+15551234", true)]
	[Arguments("/wiki/page", true)]
	[Arguments("javascript:alert(1)", false)]
	[Arguments("JavaScript:alert(1)", false)]
	[Arguments("data:text/html,<script>", false)]
	[Arguments("vbscript:msgbox(1)", false)]
	[Arguments("file:///etc/passwd", false)]
	[Arguments("", false)]
	[Arguments("   ", false)]
	public async Task IsSafeNavigableUrl_AllowsOnlyNavigableSchemes(string url, bool expected)
	{
		await Assert.That(UrlSafety.IsSafeNavigableUrl(url)).IsEqualTo(expected);
	}
}
