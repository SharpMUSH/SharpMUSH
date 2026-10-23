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
/// <c>WikiFactSearch</c>: the typed conditions sent as typed, an empty page read past, more read on request, and the
/// server's refusal shown as it gave it.
/// </summary>
public class WikiFactSearchTests : TrackingBunitContext
{
	/// <summary>Answers fact-properties, then each query request with the next queued response, recording its URI.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public List<WikiFactPropertyDto> Properties { get; init; } = [new("pf2e:level", "number", false, [])];
		public Queue<HttpResponseMessage> Answers { get; } = new();
		public List<string> Queries { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!.PathAndQuery;
			if (uri == "/api/wiki/fact-properties") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Properties) });
			Queries.Add(Uri.UnescapeDataString(uri));
			return Task.FromResult(Answers.Dequeue());
		}
	}

	private readonly Handler _handler = new();

	public WikiFactSearchTests()
	{
		var http = Track(new HttpClient(_handler) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		Services.AddSingleton(new WikiService(factory, NullLogger<WikiService>.Instance));
		Services.AddEchoLocalizer();
	}

	private static WikiPageSummaryDto Row(string slug) =>
		new($"wiki_page/{slug}", slug, slug.ToUpperInvariant(), "main", DateTimeOffset.UnixEpoch, false, 1, [], true, "en", false, null, null);

	private static HttpResponseMessage Found(string? next, params string[] slugs) =>
		new(HttpStatusCode.OK) { Content = JsonContent.Create(new WikiFactQueryResultDto([.. slugs.Select(Row)], next)) };

	private async Task<IRenderedComponent<WikiFactSearch>> SearchAsync(string typed, string? locale = null)
	{
		var cut = Render<WikiFactSearch>(p => p.Add(c => c.Locale, locale));
		await cut.Find("input").ChangeAsync(new ChangeEventArgs { Value = typed });
		await cut.Find("form").SubmitAsync();
		return cut;
	}

	[Test]
	public async Task TheTypedConditionsAreAskedAndMoreIsReadOnRequest()
	{
		_handler.Answers.Enqueue(Found("first"));
		_handler.Answers.Enqueue(Found("second", "shield"));
		_handler.Answers.Enqueue(Found(null, "fireball"));

		var cut = await SearchAsync(" pf2e:level=1..3 , pf2e:class=Smith, John ");
		// The first answer read its budget and found nothing, so the box read on by itself.
		await Assert.That(_handler.Queries).IsEquivalentTo([
			"/api/wiki/query?condition=pf2e:level=1..3&condition=pf2e:class=Smith, John",
			"/api/wiki/query?condition=pf2e:level=1..3&condition=pf2e:class=Smith, John&after=first"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll(".wiki-fact-results a").Select(a => (a.TextContent, a.GetAttribute("href"))))
			.IsEquivalentTo([("SHIELD", "/wiki/main/shield")]);

		await cut.Find(".wiki-fact-results-more").ClickAsync();
		await Assert.That(_handler.Queries[^1]).EndsWith("&after=second");
		await Assert.That(cut.FindAll(".wiki-fact-results a").Select(a => a.TextContent)).IsEquivalentTo(["SHIELD", "FIREBALL"]);
		await Assert.That(cut.FindAll(".wiki-fact-results-more")).IsEmpty();
	}

	[Test]
	public async Task AnExplicitLocaleIsAskedForAndKeptOnTheResultLinks()
	{
		_handler.Answers.Enqueue(Found(null, "shield"));
		var cut = await SearchAsync("pf2e:level=1", "fr");
		await Assert.That(_handler.Queries).IsEquivalentTo(["/api/wiki/query?condition=pf2e:level=1&lang=fr"]);
		await Assert.That(cut.Find(".wiki-fact-results a").GetAttribute("href")).IsEqualTo("/wiki/main/shield?lang=fr");
	}

	[Test]
	public async Task ARefusalIsShownAsTheServerGaveIt()
	{
		_handler.Answers.Enqueue(Track(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("No property pf2e:nope is defined.") }));
		var cut = await SearchAsync("pf2e:nope=1");
		await Assert.That(cut.Find("[role=alert]").TextContent).IsEqualTo("No property pf2e:nope is defined.");

		_handler.Answers.Enqueue(Track(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("The fact index is building: 1 of 2 pages.") }));
		await cut.Find("form").SubmitAsync();
		await Assert.That(cut.Find("[role=alert]").TextContent).IsEqualTo("WkFactSearchBuilding");
	}

	[Test]
	public async Task AQueryWithoutACategoryIsSearchedAtOnce()
	{
		_handler.Answers.Enqueue(Found(null, "fireball"));
		var cut = Render<WikiFactSearch>(p => p.Add(c => c.Query, "pf2e:tradition=arcane"));

		cut.WaitForAssertion(() => cut.Find(".wiki-fact-results a"));
		await Assert.That(_handler.Queries).IsEquivalentTo(["/api/wiki/query?condition=pf2e:tradition=arcane"]);
		await Assert.That(cut.Find("input").GetAttribute("value")).IsEqualTo("pf2e:tradition=arcane");
	}

	[Test]
	public async Task ShownValuesAreColumnsBesideEachPage()
	{
		var result = new WikiFactQueryResultDto([Row("fireball"), Row("shield")], null)
		{
			Show = [new("pf2e:level", "number", false, [], "Level"), new("pf2e:class", "page", true, [])],
			Facts = new Dictionary<string, IReadOnlyList<WikiFactDto>>
			{
				["wiki_page/fireball"] = [new("pf2e:class", "wiki_page/7") { PageTitle = "Wizard" }, new("pf2e:level", "3")],
				["wiki_page/shield"] = [new("pf2e:level", "1")],
			},
		};
		_handler.Answers.Enqueue(new(HttpStatusCode.OK) { Content = JsonContent.Create(result) });
		var cut = Render<WikiFactSearch>(p => p.Add(c => c.Query, "pf2e:level=*, show=pf2e:level|pf2e:class"));

		cut.WaitForAssertion(() => cut.Find(".wiki-fact-results-table"));
		await Assert.That(cut.FindAll("thead th").Select(th => th.TextContent)).IsEquivalentTo(["WkFactSearchPage", "Level", "pf2e:class"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindAll("tbody tr").Select(tr => string.Join("|", tr.Children.Select(c => c.TextContent.Trim()))))
			.IsEquivalentTo(["FIREBALL|3|Wizard", "SHIELD|1|"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
