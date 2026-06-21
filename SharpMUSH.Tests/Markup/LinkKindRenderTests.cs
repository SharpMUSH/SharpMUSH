using MarkupString;
using MarkupString.MarkupImplementation;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Tests for LinkKind (command vs navigation links) across the data model,
/// serialization, and every output renderer.
/// </summary>
public class LinkKindRenderTests
{
	[Test]
	public async Task Create_DefaultLinkKind_IsUrl()
	{
		var markup = AnsiMarkup.Create(linkUrl: "https://example.com");
		await Assert.That(markup.Details.LinkKind).IsEqualTo(LinkKind.Url);
	}

	[Test]
	public async Task Create_CommandLinkKind_IsPreserved()
	{
		var markup = AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command);
		await Assert.That(markup.Details.LinkKind).IsEqualTo(LinkKind.Command);
	}

	[Test]
	public async Task Serialization_LinkKind_RoundTripsJson()
	{
		var cmd = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var json = MModule.serialize(cmd);

		await Assert.That(json).Contains("LinkKind");

		// Deserialise then re-serialise: the LinkKind survives the round-trip unchanged.
		var back = MModule.deserialize(json);
		await Assert.That(MModule.serialize(back)).IsEqualTo(json);
	}

	[Test]
	public async Task Html_CommandLink_UsesXchCmdNotHref()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var html = ms.Render("html");

		await Assert.That(html).Contains("xch_cmd=\"help topic\"");
		await Assert.That(html).Contains("ms-cmd-link");
		await Assert.That(html).Contains(">topic</a>");
		await Assert.That(html).DoesNotContain("href=");
	}

	[Test]
	public async Task Html_UrlLink_UsesHrefWithNewTab()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var html = ms.Render("html");

		await Assert.That(html).Contains("href=\"https://example.com\"");
		await Assert.That(html).Contains("target=\"_blank\"");
		await Assert.That(html).Contains("rel=\"noopener noreferrer\"");
		await Assert.That(html).DoesNotContain("xch_cmd");
	}

	[Test]
	public async Task Html_CommandLink_WithHint_EmitsTitle()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who is online?"),
			"who");
		var html = ms.Render("html");

		await Assert.That(html).Contains("xch_cmd=\"+who\"");
		await Assert.That(html).Contains("title=\"Who is online?\"");
	}

	[Test]
	public async Task Html_LinkAttributes_AreHtmlEncoded()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "say \"hi\" & <bye>", linkKind: LinkKind.Command), "x");
		var html = ms.Render("html");

		await Assert.That(html).Contains("xch_cmd=\"say &quot;hi&quot; &amp; &lt;bye&gt;\"");
	}

	[Test]
	public async Task Serialization_CommandLink_SurvivesAndRendersXchCmd()
	{
		var cmd = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var json = MModule.serialize(cmd);

		var back = MModule.deserialize(json);

		// Command kind survives the round-trip: HTML render emits xch_cmd, not href.
		await Assert.That(back.Render("html")).Contains("xch_cmd=\"help topic\"");
		await Assert.That(back.Render("html")).DoesNotContain("href=");
	}

	[Test]
	public async Task Serialization_LegacyPayloadWithoutLinkKind_DefaultsToUrl()
	{
		var cmd = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var json = MModule.serialize(cmd);

		// Simulate a pre-LinkKind payload by stripping the property entirely.
		var legacy = System.Text.RegularExpressions.Regex.Replace(json, "\"LinkKind\":\\d+,?", "");
		legacy = legacy.Replace(",}", "}");

		var back = MModule.deserialize(legacy);

		// Missing field => default(LinkKind) == Url => navigation rendering.
		await Assert.That(back.Render("html")).Contains("href=\"help topic\"");
		await Assert.That(back.Render("html")).DoesNotContain("xch_cmd");
	}

	[Test]
	public async Task Pueblo_CommandLink_UsesXchCmd()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who?"), "who");
		var pueblo = ms.Render("pueblo");

		await Assert.That(pueblo).Contains("<A XCH_CMD=\"+who\"");
		await Assert.That(pueblo).Contains("XCH_HINT=\"Who?\"");
		await Assert.That(pueblo).DoesNotContain("HREF=");
	}

	[Test]
	public async Task Pueblo_UrlLink_UsesHref()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var pueblo = ms.Render("pueblo");

		await Assert.That(pueblo).Contains("<A HREF=\"https://example.com\">");
		await Assert.That(pueblo).DoesNotContain("XCH_CMD");
	}

	[Test]
	public async Task Mxp_CommandLink_UsesSend()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who?"), "who");
		var mxp = ms.Render("mxp");

		await Assert.That(mxp).Contains("<SEND HREF=\"+who\"");
		await Assert.That(mxp).Contains("HINT=\"Who?\"");
	}

	[Test]
	public async Task Mxp_UrlLink_UsesAnchorNotSend()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var mxp = ms.Render("mxp");

		await Assert.That(mxp).Contains("<A HREF=\"https://example.com\">");
		await Assert.That(mxp).DoesNotContain("<SEND");
	}

	[Test]
	public async Task Ansi_CommandLink_RendersPlainTextNoOsc8()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var ansi = ms.Render("ansi");

		await Assert.That(ansi).DoesNotContain("]8;;");
		await Assert.That(ansi.Contains("topic")).IsTrue();
	}

	[Test]
	public async Task Ansi_UrlLink_StillRendersOsc8()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var ansi = ms.Render("ansi");

		await Assert.That(ansi).Contains("]8;;https://example.com");
	}

	[Test]
	public async Task BBCode_UrlLink_UsesUrlTag()
	{
		// NOTE: Render("bbcode") is intentionally NOT a wired render route — BBCode has no
		// production consumer (the string Render switch falls through to ANSI, and the only
		// "bbcode" usage is a test-local custom strategy). Wiring a full BBCode strategy/cache
		// for an unused format would be YAGNI, so we test the public static WrapAsBBCode directly.
		var details = AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url).Details;
		var bbcode = AnsiMarkup.WrapAsBBCode(details, "site");

		await Assert.That(bbcode).Contains("[url=https://example.com]");
	}

	[Test]
	public async Task BBCode_CommandLink_RendersPlainText()
	{
		var details = AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command).Details;
		var bbcode = AnsiMarkup.WrapAsBBCode(details, "topic");

		await Assert.That(bbcode).DoesNotContain("[url=");
		await Assert.That(bbcode.Contains("topic")).IsTrue();
	}
}
