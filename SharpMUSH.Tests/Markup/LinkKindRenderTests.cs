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
}
