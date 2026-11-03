using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class HelpStartTests : TrackingBunitContext
{
	private IRenderedComponent<Help> RenderIndex(params string[] topics)
	{
		var help = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["help"] = "# help\nThe index." };
		foreach (var topic in topics) help[topic] = $"# {topic}\nText.";
		HelpApi.Install(this, false, new Dictionary<string, Dictionary<string, string>> { ["help"] = help });
		Services.AddEchoLocalizer();
		this.AddAuthorization();
		var cut = Render<Help>();
		cut.WaitForAssertion(() => cut.Find(".help-entry"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private static string[] Hrefs(IRenderedComponent<Help> cut) =>
		cut.FindAll(".help-start a").Select(a => a.GetAttribute("href") ?? "").ToArray();

	[Test]
	public async Task TheIndexOpensWithTheGuideTheWalkthroughAndHowToReadHelp()
	{
		var cut = RenderIndex("getting started", "newbie");
		await Assert.That(Hrefs(cut)).IsEquivalentTo(new[] { "/welcome", "/help/getting%20started", "/help/newbie" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll(".help-start .help-start-name").Select(e => e.TextContent).ToArray())
			.IsEquivalentTo(new[] { "GuideHelpIndexEntry", "NavHelpGettingStarted", "NavHelpNewbie" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AGameWithoutThoseTopicsKeepsOnlyTheGuide()
	{
		var cut = RenderIndex();
		await Assert.That(Hrefs(cut)).IsEquivalentTo(new[] { "/welcome" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
