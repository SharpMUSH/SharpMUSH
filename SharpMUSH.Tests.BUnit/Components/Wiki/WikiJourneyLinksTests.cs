using Microsoft.AspNetCore.WebUtilities;
using SharpMUSH.Client.Models.Wiki;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>Entrance constraints stay on their article while normal destinations keep independent identities.</summary>
public class WikiJourneyLinksTests
{
	private static Uri Address(string href) => new(new Uri("https://client.example/"), href);

	[Test]
	[Arguments("main")]
	[Arguments("help")]
	[Arguments("category")]
	public async Task LandingKeepsCanonicalArticleBrowseMemberAndRequestedLocaleState(string ns)
	{
		var landing = new WikiLandingPageDto("wiki_page/42", ns, "guide", "Live translated title", "en", true);
		var state = new WikiBrowseState(" package:rules ", " Règle & C++ ", " Actions & costs ", 77, " fr-CA ");

		var href = WikiJourneyLinks.Landing(landing, state);

		var address = Address(href);
		await Assert.That(address.Scheme).IsEqualTo("https");
		await Assert.That(address.Authority).IsEqualTo("client.example");
		await Assert.That(address.PathAndQuery).IsEqualTo(href);
		await Assert.That(address.AbsolutePath).IsEqualTo("/wiki/" + ns + "/guide");
		var query = QueryHelpers.ParseQuery(address.Query);
		await Assert.That(query["expectedPageId"].Single()).IsEqualTo("wiki_page/42");
		await Assert.That(query["browseCollection"].Single()).IsEqualTo("package:rules");
		await Assert.That(query["browseQ"].Single()).IsEqualTo("Règle & C++");
		await Assert.That(query["browseCategory"].Single()).IsEqualTo("Actions & costs");
		await Assert.That(query["browseSkip"].Single()).IsEqualTo("50");
		await Assert.That(query["membersCollection"].Single()).IsEqualTo("package:rules");
		// The reader requested French; the descriptor's served-English fallback does not rewrite that request.
		await Assert.That(query["lang"].Single()).IsEqualTo("fr-CA");
		await Assert.That(query.ContainsKey("membersSkip")).IsFalse();
	}

	[Test]
	[Arguments("de")]
	[Arguments("fr-CA")]
	public async Task ExplicitSameArticleLanguageAndFragmentLinksRetainOnlyTheSuppliedIdentity(string locale)
	{
		var href = WikiJourneyLinks.SameArticle(
			"/wiki/help/guide?expectedPageId=forged&lang=en&extra=a%26b#costs", "wiki_page/42",
			new WikiBrowseState("package:rules", "Règle", "actions", 100, "fr-CA"), "game", 150, locale);

		var address = Address(href);
		await Assert.That(address.AbsolutePath).IsEqualTo("/wiki/help/guide");
		await Assert.That(address.Fragment).IsEqualTo("#costs");
		var query = QueryHelpers.ParseQuery(address.Query);
		await Assert.That(query["expectedPageId"].Single()).IsEqualTo("wiki_page/42");
		await Assert.That(query["lang"].Single()).IsEqualTo(locale);
		await Assert.That(query["browseCollection"].Single()).IsEqualTo("package:rules");
		await Assert.That(query["browseSkip"].Single()).IsEqualTo("100");
		await Assert.That(query["membersCollection"].Single()).IsEqualTo("game");
		await Assert.That(query["membersSkip"].Single()).IsEqualTo("150");
		await Assert.That(query["extra"].Single()).IsEqualTo("a&b");
	}

	[Test]
	[Arguments("/wiki/main/other")]
	[Arguments("/wiki/main/other/edit")]
	[Arguments("/wiki/main/other/history")]
	public async Task GenericDestinationsDiscardDirectIdentityEvenWhenIncomingQueryCarriesOne(string destination)
	{
		var state = WikiBrowseState.FromUri(
			"https://client.example/wiki/collections/package%3Arules?q=R%C3%A8gle&category=actions&skip=100&lang=fr-CA&expectedPageId=wiki_page%2Fsource");
		var href = WikiJourneyLinks.Article(destination + "?expectedPageId=wiki_page%2Fold&EXPECTEDPAGEID=wiki_page%2Fnew&extra=a%26b#costs",
			state, "game", 50);

		var address = Address(href);
		await Assert.That(address.AbsolutePath).IsEqualTo(destination);
		await Assert.That(address.Fragment).IsEqualTo("#costs");
		var query = QueryHelpers.ParseQuery(address.Query);
		await Assert.That(query.Keys.Any(key => key.Equals("expectedPageId", StringComparison.OrdinalIgnoreCase))).IsFalse();
		await Assert.That(query["lang"].Single()).IsEqualTo("fr-CA");
		await Assert.That(query["browseCollection"].Single()).IsEqualTo("package:rules");
		await Assert.That(query["browseQ"].Single()).IsEqualTo("Règle");
		await Assert.That(query["browseCategory"].Single()).IsEqualTo("actions");
		await Assert.That(query["browseSkip"].Single()).IsEqualTo("100");
		await Assert.That(query["membersCollection"].Single()).IsEqualTo("game");
		await Assert.That(query["membersSkip"].Single()).IsEqualTo("50");
		await Assert.That(query["extra"].Single()).IsEqualTo("a&b");
	}
}
