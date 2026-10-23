using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// <c>PUT /api/wiki/{slug}/facts</c>, the facts on a page's DTO, and <c>GET /api/wiki/fact-properties</c>: a page's
/// facts set and read from the portal.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class WikiFactApiTests(ServerWebAppFactory factory)
{
	private record WikiFactDto(string Property, string Value)
	{
		public string? PageTitle { get; init; }
		public string? PageSlug { get; init; }
		public string? PageNamespace { get; init; }
		public string? Label { get; init; }
	}

	private record WikiPageLinkDto(string Title, string Slug, string Namespace);
	private record WikiFactNamedByDto(string Property, string Label, List<WikiPageLinkDto> Pages, bool More);

	private record WikiPageDto(string Id, string Slug, string Title, int RevisionNumber, List<WikiFactDto> Facts)
	{
		public List<WikiFactNamedByDto> NamedBy { get; init; } = [];
	}
	private record WikiFactPropertyDto(string Id, string Type, bool Multiple, List<string> Keywords, string Label = "", string Description = "");
	private record SetFactPropertyRequest(string Type, bool Multiple, string[]? Keywords, string? Label, string? Description);
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

	private static async Task<WikiPageDto> CreatePageAsync(HttpClient http, string prefix)
	{
		var response = await http.PostAsJsonAsync("api/wiki", new CreatePageRequest($"{prefix} {Guid.NewGuid():N}", "# facts", null));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<WikiPageDto>())!;
	}

	[Test]
	public async Task FactsAreSetByTitleAndReadBackWithTheirPage()
	{
		var http = CreateClient();
		var spell = await CreatePageAsync(http, "Fact api spell");
		var wizard = await CreatePageAsync(http, "Fact api wizard");

		var put = await http.PutAsJsonAsync($"api/wiki/{Uri.EscapeDataString(spell.Slug)}/facts",
			new SetFactsRequest([new("pf2e:level", "3"), new("pf2e:class", wizard.Title), new("pf2e:tradition", "arcane")]));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var updated = (await put.Content.ReadFromJsonAsync<WikiPageDto>())!;
		await Assert.That(updated.RevisionNumber).IsEqualTo(spell.RevisionNumber);

		var fetched = (await http.GetFromJsonAsync<WikiPageDto>($"api/wiki/ns/main/{Uri.EscapeDataString(spell.Slug)}"))!;
		await Assert.That(fetched.Facts.Select(f => (f.Property, f.Value)))
			.IsEquivalentTo([("pf2e:class", wizard.Id), ("pf2e:level", "3"), ("pf2e:tradition", "arcane")]);
		var byClass = fetched.Facts.Single(f => f.Property == "pf2e:class");
		await Assert.That((byClass.PageTitle, byClass.PageSlug, byClass.PageNamespace)).IsEqualTo((wizard.Title, wizard.Slug, "main"));
		await Assert.That(fetched.Facts.Single(f => f.Property == "pf2e:level").PageTitle).IsNull();
	}

	[Test]
	public async Task APageListsThePagesWhoseFactsNameIt()
	{
		await factory.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
		var http = CreateClient();
		var spell = await CreatePageAsync(http, "Named api spell");
		var wizard = await CreatePageAsync(http, "Named api wizard");
		var put = await http.PutAsJsonAsync($"api/wiki/{Uri.EscapeDataString(spell.Slug)}/facts",
			new SetFactsRequest([new("pf2e:class", wizard.Title), new("pf2e:level", "1")]));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var fetched = (await http.GetFromJsonAsync<WikiPageDto>($"api/wiki/ns/main/{Uri.EscapeDataString(wizard.Slug)}"))!;
		var named = fetched.NamedBy.Single();
		await Assert.That((named.Property, named.Label, named.More)).IsEqualTo(("pf2e:class", "pf2e:class", false));
		await Assert.That(named.Pages).IsEquivalentTo([new WikiPageLinkDto(spell.Title, spell.Slug, "main")]);
		// A property without a label is shown as itself.
		var stated = (await http.GetFromJsonAsync<WikiPageDto>($"api/wiki/ns/main/{Uri.EscapeDataString(spell.Slug)}"))!;
		await Assert.That(stated.Facts.Select(f => f.Label ?? "")).IsEquivalentTo(["pf2e:class", "pf2e:level"]);
		await Assert.That(stated.NamedBy).IsEmpty();
	}

	[Test]
	public async Task AFactTheSchemaRefusesIsABadRequestAndChangesNothing()
	{
		var http = CreateClient();
		var spell = await CreatePageAsync(http, "Fact api refused");
		var url = $"api/wiki/{Uri.EscapeDataString(spell.Slug)}/facts";
		await Assert.That((await http.PutAsJsonAsync(url, new SetFactsRequest([new("pf2e:level", "2")]))).StatusCode).IsEqualTo(HttpStatusCode.OK);

		var refused = await http.PutAsJsonAsync(url, new SetFactsRequest([new("pf2e:level", "two")]));
		await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(await refused.Content.ReadAsStringAsync()).Contains("pf2e:level is a whole number, not 'two'.");
		var missing = await http.PutAsJsonAsync(url, new SetFactsRequest([new("pf2e:class", "No Such Fact Page")]));
		await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		// A body without a list is not a request to clear them; an empty list is.
		foreach (var body in new[] { "{}", """{"facts":null}""", """{"facts":[null]}""" })
		{
			using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
			await Assert.That((await http.PutAsync(url, content)).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		}

		var fetched = (await http.GetFromJsonAsync<WikiPageDto>($"api/wiki/ns/main/{Uri.EscapeDataString(spell.Slug)}"))!;
		await Assert.That(fetched.Facts.Select(f => (f.Property, f.Value))).IsEquivalentTo([("pf2e:level", "2")]);
	}

	[Test]
	public async Task ThePropertiesNameTheirTypes()
	{
		var properties = (await CreateClient().GetFromJsonAsync<List<WikiFactPropertyDto>>("api/wiki/fact-properties"))!;
		var level = properties.Single(p => p.Id == "pf2e:level");
		await Assert.That((level.Type, level.Multiple)).IsEqualTo(("number", false));
		var tradition = properties.Single(p => p.Id == "pf2e:tradition");
		await Assert.That((tradition.Type, tradition.Multiple)).IsEqualTo(("keyword", true));
		await Assert.That(tradition.Keywords).IsEquivalentTo(["arcane", "divine", "occult", "primal"]);
		await Assert.That(properties.Single(p => p.Id == "pf2e:class").Type).IsEqualTo("page");
	}

	[Test]
	public async Task StaffDefineAndRemoveAProperty()
	{
		var http = CreateClient();
		var url = $"api/wiki/fact-properties/{Uri.EscapeDataString($"test:size-{Guid.NewGuid():N}")}";

		var put = await http.PutAsJsonAsync(url, new SetFactPropertyRequest("keyword", false, ["Small", "Large"], "Size", "How big it is."));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var stored = (await put.Content.ReadFromJsonAsync<WikiFactPropertyDto>())!;
		await Assert.That((stored.Type, stored.Label, stored.Description)).IsEqualTo(("keyword", "Size", "How big it is."));
		await Assert.That(stored.Keywords).IsEquivalentTo(["small", "large"]);
		var listed = (await http.GetFromJsonAsync<List<WikiFactPropertyDto>>("api/wiki/fact-properties"))!.Single(p => p.Id == stored.Id);
		await Assert.That(listed.Label).IsEqualTo("Size");

		var badType = await http.PutAsJsonAsync(url, new SetFactPropertyRequest("colour", false, null, null, null));
		await Assert.That(badType.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		var badValues = await http.PutAsJsonAsync(url, new SetFactPropertyRequest("number", false, ["one"], null, null));
		await Assert.That(badValues.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

		await Assert.That((await http.DeleteAsync(url)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await http.DeleteAsync(url)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That((await http.GetFromJsonAsync<List<WikiFactPropertyDto>>("api/wiki/fact-properties"))!.Any(p => p.Id == stored.Id)).IsFalse();
	}
}
