using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary><c>GET /api/wiki/query</c>: the pages whose facts meet every condition, a page of them at a time.</summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class WikiFactQueryApiTests(ServerWebAppFactory factory)
{
	private record WikiFactDto(string Property, string Value)
	{
		public string? PageTitle { get; init; }
	}

	private record WikiPageDto(string Id, string Slug, string Title);
	private record WikiPageSummaryDto(string Id, string Slug, string Title);
	private record WikiFactPropertyDto(string Id, string Label);

	private record WikiFactQueryResultDto(List<WikiPageSummaryDto> Pages, string? Next)
	{
		public List<WikiFactPropertyDto> Show { get; init; } = [];
		public Dictionary<string, List<WikiFactDto>> Facts { get; init; } = [];
	}
	private record CreatePageRequest(string Title, string Markdown, string? Namespace);
	private record SetFactsRequest(WikiFactDto[] Facts);

	[Before(Test)]
	public Task DefineFactProperties() => WikiFactTestProperties.DefineAsync(factory.Services.GetRequiredService<IWikiService>());

	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static async Task<WikiPageDto> PageAsync(HttpClient http, string prefix, params WikiFactDto[] facts)
	{
		var created = await http.PostAsJsonAsync("api/wiki", new CreatePageRequest($"{prefix} {Guid.NewGuid():N}", "# query", null));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var page = (await created.Content.ReadFromJsonAsync<WikiPageDto>())!;
		var put = await http.PutAsJsonAsync($"api/wiki/{Uri.EscapeDataString(page.Slug)}/facts", new SetFactsRequest(facts));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return page;
	}

	private static string Url(string after, params string[] conditions)
		=> "api/wiki/query?take=1" + string.Concat(conditions.Select(c => "&condition=" + Uri.EscapeDataString(c)))
			+ (after.Length == 0 ? "" : "&after=" + Uri.EscapeDataString(after));

	[Test]
	public async Task AQueryIsReadAPageAtATime()
	{
		await factory.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var http = CreateClient();
		var wizard = await PageAsync(http, "Query api wizard");
		var first = await PageAsync(http, "Query api first", new WikiFactDto("pf2e:class", wizard.Title), new WikiFactDto("pf2e:level", "2"));
		var second = await PageAsync(http, "Query api second", new WikiFactDto("pf2e:class", wizard.Title), new WikiFactDto("pf2e:level", "4"));
		await PageAsync(http, "Query api other", new WikiFactDto("pf2e:level", "2"));

		var found = new List<string>();
		var after = string.Empty;
		do
		{
			var page = (await http.GetFromJsonAsync<WikiFactQueryResultDto>(Url(after, $"pf2e:class={wizard.Title}", "pf2e:level=1..5")))!;
			found.AddRange(page.Pages.Select(p => p.Id));
			after = page.Next ?? string.Empty;
		} while (after.Length > 0);
		await Assert.That(found).IsEquivalentTo([first.Id, second.Id], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ASortedQueryShowsTheValuesItNames()
	{
		await factory.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var http = CreateClient();
		var wizard = await PageAsync(http, "Shown api wizard");
		var high = await PageAsync(http, "Shown api high", new WikiFactDto("pf2e:class", wizard.Title), new WikiFactDto("pf2e:level", "6"),
			new WikiFactDto("pf2e:tradition", "arcane"));
		var low = await PageAsync(http, "Shown api low", new WikiFactDto("pf2e:class", wizard.Title), new WikiFactDto("pf2e:level", "2"));

		var page = (await http.GetFromJsonAsync<WikiFactQueryResultDto>("api/wiki/query?condition=" + Uri.EscapeDataString($"pf2e:class={wizard.Title}")
			+ "&condition=sort%3Dpf2e:level&condition=order%3Ddesc&condition=" + Uri.EscapeDataString("show=pf2e:level|pf2e:tradition|pf2e:class")))!;
		await Assert.That(page.Pages.Select(p => p.Id)).IsEquivalentTo([high.Id, low.Id], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(page.Show.Select(p => p.Id)).IsEquivalentTo(["pf2e:level", "pf2e:tradition", "pf2e:class"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(page.Facts[high.Id].Select(f => (f.Property, f.Value)))
			.IsEquivalentTo([("pf2e:class", wizard.Id), ("pf2e:level", "6"), ("pf2e:tradition", "arcane")]);
		await Assert.That(page.Facts[high.Id].Single(f => f.Property == "pf2e:class").PageTitle).IsEqualTo(wizard.Title);
		await Assert.That(page.Facts[low.Id].Select(f => f.Property)).IsEquivalentTo(["pf2e:class", "pf2e:level"]);
	}

	[Test]
	public async Task AQueryTheWikiCannotAnswerIsABadRequest()
	{
		var http = CreateClient();
		var refused = await http.GetAsync(Url("", "pf2e:level=1..x"));
		await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(await refused.Content.ReadAsStringAsync()).Contains("'1..x' is not a range like '1..3'.");
		await Assert.That((await http.GetAsync(Url("not-a-continuation", "pf2e:level=1"))).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That((await http.GetAsync("api/wiki/query")).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}
}
