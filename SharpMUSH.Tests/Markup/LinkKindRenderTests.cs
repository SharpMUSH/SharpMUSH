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
}
