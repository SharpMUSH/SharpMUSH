using Bunit;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Models.Wiki;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

public class WikiMoreCardTests : TrackingBunitContext
{
	[Test]
	public async Task LongRelatedTitlesOptIntoWrappingAndKeepDistinctNamesDestinationsAndJourney()
	{
		var (api, _) = WikiApiFake.Install(this);
		const string prefix = "Workshop maintenance manual — preparing the pressure regulator for ";
		var pages = new[]
		{
			new WikiPageSummary("cold_start", prefix + "cold start verification", "main", DateTimeOffset.UnixEpoch, 1),
			new WikiPageSummary("hot_restart", prefix + "hot restart verification", "main", DateTimeOffset.UnixEpoch, 1)
		};
		var cut = Render<WikiMoreCard>(parameters => parameters
			.Add(p => p.Category, "maintenance").Add(p => p.CategoryLabel, "Maintenance")
			.Add(p => p.Pages, pages).Add(p => p.Total, 11)
			.Add(p => p.BrowseState, new WikiBrowseState("package:workshop", "pressure & seals", "equipment", 100, "fr-CA"))
			.Add(p => p.MembersCollection, "package:workshop").Add(p => p.MembersSkip, 50));

		var rows = cut.FindAll(".wiki-more a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(2);
		for (var i = 0; i < rows.Count; i++)
		{
			await Assert.That(rows[i].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo(pages[i].Title);
			await Assert.That(rows[i].GetAttribute("aria-label") ?? rows[i].TextContent.Trim()).IsEqualTo(pages[i].Title);
			var target = new Uri(new Uri("http://localhost/"), rows[i].GetAttribute("href"));
			await Assert.That(target.AbsolutePath).IsEqualTo("/wiki/main/" + pages[i].Slug);
			var query = System.Web.HttpUtility.ParseQueryString(target.Query);
			await Assert.That(query["browseCollection"]).IsEqualTo("package:workshop");
			await Assert.That(query["browseQ"]).IsEqualTo("pressure & seals");
			await Assert.That(query["browseCategory"]).IsEqualTo("equipment");
			await Assert.That(query["browseSkip"]).IsEqualTo("100");
			await Assert.That(query["membersCollection"]).IsEqualTo("package:workshop");
			await Assert.That(query["membersSkip"]).IsEqualTo("50");
			await Assert.That(query["lang"]).IsEqualTo("fr-CA");
		}
		await Assert.That(api.Requests).IsEmpty();
		await Assert.That(cut.Find(".kit-card-title").TextContent).Contains("Maintenance");
		await Assert.That(cut.Find(".kit-card-title").TextContent).DoesNotContain("Next step");
		foreach (var row in rows)
			await Assert.That(row.ClassList).Contains("kit-row--wrap-label")
				.Because("the More card must opt into full multiline labels instead of the shared compact row");
	}
}
