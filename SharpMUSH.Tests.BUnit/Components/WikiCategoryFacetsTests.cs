using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// <c>WikiCategoryFacets</c>: a category's values with their counts, the chosen ones sent as conditions, and the pages
/// they find shown in place of the members.
/// </summary>
public class WikiCategoryFacetsTests : TrackingBunitContext
{
	/// <summary>Answers each facets request with <see cref="Facets"/> and each query with Fireball, recording both.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
		public WikiFacetsDto Facets { get; set; } = new(3, false, [
			new("pf2e:tradition", "Tradition", "keyword", [new("arcane", "arcane", 3), new("occult", "occult", 2)], 0),
			new("pf2e:level", "Level", "number", [new("1", "1", 2), new("3", "3", 1)], 0),
		]);
		public List<string> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
			Requests.Add(uri);
			return Task.FromResult(uri.Contains("/facets")
				? Status == HttpStatusCode.OK
					? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Facets) }
					: new HttpResponseMessage(Status) { Content = new StringContent("No property pf2e:nope is defined.") }
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new WikiFactQueryResultDto([Fireball], null)) });
		}
	}

	private static readonly WikiPageSummaryDto Fireball =
		new("wiki_page/1", "fireball", "Fireball", "main", DateTimeOffset.UnixEpoch, false, 1, [], true, "en", false, null, null);

	private readonly Handler _handler = new();

	public WikiCategoryFacetsTests()
	{
		var http = Track(new HttpClient(_handler) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		Services.AddSingleton(new WikiService(factory, NullLogger<WikiService>.Instance));
		Services.AddEchoLocalizer();
	}

	private IRenderedComponent<WikiCategoryFacets> Facets() => Render<WikiCategoryFacets>(p => p
		.Add(c => c.Category, "spells")
		.AddChildContent("<div class=\"members\">members</div>"));

	private Task ChooseAsync(IRenderedComponent<WikiCategoryFacets> cut, string label)
		=> cut.FindAll(".wiki-facet-value").Single(b => b.TextContent.Trim().StartsWith(label + " ", StringComparison.Ordinal)).ClickAsync();

	[Test]
	public async Task ValuesAreListedWithTheirCountsAboveTheMembers()
	{
		var cut = Facets();

		await Assert.That(cut.FindAll("legend").Select(l => l.TextContent)).IsEquivalentTo(["Tradition", "Level"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll(".wiki-facet-value").Select(b => string.Join(' ', b.TextContent.Split(' ', StringSplitOptions.RemoveEmptyEntries))))
			.IsEquivalentTo(["arcane 3", "occult 2", "1 2", "3 1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll(".members")).Count().IsEqualTo(1);
		await Assert.That(_handler.Requests).IsEquivalentTo(["/api/wiki/category/spells/facets"]);
	}

	[Test]
	public async Task ChosenValuesFindPagesInPlaceOfTheMembers()
	{
		var cut = Facets();

		await ChooseAsync(cut, "arcane");
		await ChooseAsync(cut, "occult");
		await ChooseAsync(cut, "1");
		// A whole number is one range, so another replaces it.
		await ChooseAsync(cut, "3");

		await Assert.That(_handler.Requests.Where(r => r.Contains("/facets")).Last())
			.IsEqualTo("/api/wiki/category/spells/facets?condition=pf2e:tradition=arcane|occult&condition=pf2e:level=3");
		await Assert.That(_handler.Requests.Last(r => r.StartsWith("/api/wiki/query", StringComparison.Ordinal)))
			.IsEqualTo("/api/wiki/query?condition=category=spells&condition=pf2e:tradition=arcane|occult&condition=pf2e:level=3"
				+ "&condition=show=pf2e:tradition|pf2e:level");
		await Assert.That(cut.FindAll(".wiki-facet-value[aria-pressed=true]").Select(b => b.TextContent.Trim()[..1]))
			.IsEquivalentTo(["a", "o", "3"]);
		await Assert.That(cut.FindAll(".members")).IsEmpty();
		await Assert.That(cut.Find(".wiki-fact-results a").TextContent).IsEqualTo("Fireball");

		await cut.Find(".wiki-facets-clear").ClickAsync();
		await Assert.That(cut.FindAll(".members")).Count().IsEqualTo(1);
		await Assert.That(_handler.Requests[^1]).IsEqualTo("/api/wiki/category/spells/facets");
	}

	[Test]
	public async Task ACategoryWithoutFactsShowsOnlyItsMembers()
	{
		_handler.Facets = new(4, false, []);
		var cut = Facets();
		await Assert.That(cut.FindAll(".wiki-facets")).IsEmpty();
		await Assert.That(cut.FindAll(".members")).Count().IsEqualTo(1);
	}

	[Test]
	public async Task FacetsTheServerCannotGiveLeaveTheMembers()
	{
		_handler.Status = HttpStatusCode.InternalServerError;
		var cut = Facets();
		await Assert.That(cut.FindAll(".wiki-facets")).IsEmpty();
		await Assert.That(cut.FindAll(".members")).Count().IsEqualTo(1);
	}
}
