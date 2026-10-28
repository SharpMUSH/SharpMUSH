using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>External links in the actual help-page/API renderer, not a separate preview pipeline.</summary>
public class JourneyHelpLinkTests : TrackingBunitContext
{
	[Test]
	public async Task TheHelpPageLinksIntendedExternalUrlsWithoutLinkingCodeOrChangingTopicRefs()
	{
		var corpora = new Dictionary<string, Dictionary<string, string>>
		{
			["help"] = new(StringComparer.OrdinalIgnoreCase)
			{
				["help"] = "# help\nSee https://example.org/guide and [manual](https://example.org/manual){target=_self rel=opener}.\n\nUse `https://example.org/literal` as text.\n\nSee [newbie].",
				["newbie"] = "# newbie\nWelcome."
			}
		};
		HelpApi.Install(this, false, corpora);
		Services.AddEchoLocalizer();
		this.AddAuthorization();
		var cut = Render<Help>();
		cut.WaitForAssertion(() => cut.Find("a[href='https://example.org/guide']"), TimeSpan.FromSeconds(5));
		foreach (var url in new[] { "https://example.org/guide", "https://example.org/manual" })
		{
			var link = cut.Find($"a[href='{url}']");
			await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
			await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
		}
		await Assert.That(cut.FindAll("a[href='https://example.org/literal']")).IsEmpty();
		await Assert.That(cut.FindAll("a[href='/help/newbie']")).IsNotEmpty();
	}
}
