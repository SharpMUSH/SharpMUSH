using System.Net;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.API;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// Serves the wiki page lists the REST API does, with the D1 fields (<c>image</c>, <c>lastEditedBy</c>):
/// three categories (guides ×2, lore ×1, one uncategorised) so a sidebar, a category page and the
/// recent list all have something to show.
/// </summary>
internal sealed class WikiApiFake : HttpMessageHandler
{
	public static string Page(string id, string slug, string title, string? category, string? image = null, string? editor = null, bool published = true, bool isRestricted = false, string ns = "main") =>
		$$"""
		{"id":"{{id}}","slug":"{{slug}}","title":"{{title}}","namespace":"{{ns}}","markdownSource":"","renderedHtml":"","plainText":"",
		 "createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-0{{id}}T00:00:00+00:00","isRestricted":{{(isRestricted ? "true" : "false")}},"revisionNumber":1,"access":{"read":true,"edit":true,"delete":true,"manage":false},
		 "categories":{{(category is null ? "[]" : $"[\"{category}\"]")}},"published":{{(published ? "true" : "false")}},
		 "image":{{(image is null ? "null" : $"\"{image}\"")}},"lastEditedBy":{{(editor is null ? "null" : $"\"{editor}\"")}}}
		""";

	public static readonly string[] AllPages =
	[
		Page("1", "intro", "Getting Started", "guides", "/api/wiki-assets/a/intro.jpg", "Ilsa Varn"),
		Page("2", "combat", "Combat Basics", "guides", null, "Tomas Reyes", published: false),
		Page("3", "harbour", "Harbour Ward", "lore", "/api/wiki-assets/b/harbour.jpg", "Wren", isRestricted: true),
		Page("4", "notes", "Loose Notes", null, null, null),
	];

	/// <summary>A category of 250 pages, more than any one listing request returns.</summary>
	public static readonly string[] BigCategory =
		[.. Enumerable.Range(1, 250).Select(i => Page("1", $"big_{i}", $"Big {i}", "big"))];

	public bool Refuse { get; set; }
	public int? NavigationTotal { get; set; }
	public string? PageCollection { get; set; }
	public WikiNavigationDto? NavigationResult { get; set; }
	public List<WikiCollectionSummaryDto> Collections { get; } = [];
	public string[]? CollectionRows { get; set; }

	/// <summary>
	/// When set, a collection page answers with cursor tokens as the server does: <c>at-N</c> names the row at index N,
	/// <c>after</c> reads from the row after it and <c>before</c> up to the row before it.
	/// </summary>
	public bool Cursors { get; set; }

	/// <summary>The rows a collection page returns, and its tokens either side.</summary>
	private (string[] Rows, string? Previous, string? Next) CollectionPage(HttpRequestMessage request)
	{
		var all = CollectionResults(request);
		var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
		var take = int.TryParse(query["take"], out var t) ? t : 50;
		var start = int.TryParse(query["skip"], out var s) ? s : 0;
		if (Cursors && query["after"] is { } after && after.StartsWith("at-", StringComparison.Ordinal)) start = int.Parse(after[3..]) + 1;
		else if (Cursors && query["before"] is { } before && before.StartsWith("at-", StringComparison.Ordinal))
			start = Math.Max(0, int.Parse(before[3..]) - take);
		var rows = all.Skip(start).Take(take).ToArray();
		if (!Cursors || rows.Length == 0) return (rows, null, null);
		return (rows, start > 0 ? $"at-{start}" : null, start + rows.Length < all.Length ? $"at-{start + rows.Length - 1}" : null);
	}

	private string Navigation(HttpRequestMessage request)
	{
		var collection = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["collection"] ?? PageCollection ?? "game";
		if (NavigationResult is { } navigation) return JsonSerializer.Serialize(navigation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		return JsonSerializer.Serialize(new WikiNavigationDto(collection, NavigationTotal ?? AllPages.Length, Collections,
			[new WikiCategorySummaryDto("guides", "Guides", 2, true, Pinned.Contains("guides")) { Image = "/api/wiki-assets/a/intro.jpg" },
			 new WikiCategorySummaryDto("lore", "Lore", 1, true, Pinned.Contains("lore")) { Image = "/api/wiki-assets/b/harbour.jpg" }]),
			new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}

	private string[] CollectionResults(HttpRequestMessage request)
	{
		var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
		var rows = CollectionRows ?? query["category"] switch
		{
			"big" => BigCategory,
			"realms" => [Page("5", "harbour_lore", "Harbour lore", "realms", ns: "category"), Page("3", "harbour", "Harbour Ward", "realms")],
			_ => AllPages
		};
		return rows.Where(json =>
{
	using var doc = JsonDocument.Parse(json);
	var categories = doc.RootElement.GetProperty("categories").EnumerateArray().Select(row => row.GetString() ?? "").ToArray();
	return (string.IsNullOrWhiteSpace(query["category"]) || categories.Contains(query["category"]))
		&& (string.IsNullOrWhiteSpace(query["search"]) || doc.RootElement.GetProperty("title").GetString()!.Contains(query["search"]!, StringComparison.OrdinalIgnoreCase)
			|| categories.Any(row => row.Contains(query["search"]!, StringComparison.OrdinalIgnoreCase)));
}).OrderBy(json =>
		{
			using var doc = JsonDocument.Parse(json);
			return doc.RootElement.GetProperty("title").GetString();
		}, StringComparer.Ordinal).ToArray();
	}
	public List<string> Requests { get; } = [];

	/// <summary>The categories pinned to the wiki home; a pin request changes them as the API does.</summary>
	public List<string> Pinned { get; } = ["guides", "lore"];

	/// <summary>When set, the pinned-categories read fails as an unreachable server would.</summary>
	public bool FailPins { get; set; }

	/// <summary>Pages a category listing the way the API does: <c>skip</c> and <c>take</c> from the query.</summary>
	public static string Paged(HttpRequestMessage request, string[] pages)
	{
		var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
		var skip = int.TryParse(query["skip"], out var s) ? s : 0;
		var take = int.TryParse(query["take"], out var t) ? t : 50;
		return "[" + string.Join(",", pages.Skip(skip).Take(take)) + "]";
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		Requests.Add(request.RequestUri!.PathAndQuery);
		if (Refuse) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
		var path = request.RequestUri!.AbsolutePath;
		if (request.Method == HttpMethod.Put && path.StartsWith("/api/wiki/categories/", StringComparison.Ordinal) && path.EndsWith("/pin", StringComparison.Ordinal))
		{
			var key = Uri.UnescapeDataString(path["/api/wiki/categories/".Length..^"/pin".Length]);
			var pin = request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult().Contains("true", StringComparison.Ordinal);
			Pinned.Remove(key);
			if (pin) Pinned.Add(key);
		}

		string? body = path switch
		{
			"/api/wiki/navigation" when !FailPins => Navigation(request),
			_ when path.StartsWith("/api/wiki/collections/", StringComparison.Ordinal) && path.EndsWith("/pages", StringComparison.Ordinal)
				=> "[" + string.Join(",", CollectionPage(request).Rows) + "]",
			"/api/wiki/pinned-categories" when !FailPins => System.Text.Json.JsonSerializer.Serialize(Pinned),
			_ when request.Method == HttpMethod.Put && path.EndsWith("/pin", StringComparison.Ordinal) => System.Text.Json.JsonSerializer.Serialize(Pinned),
			"/api/wiki/pages" => "[" + string.Join(",", AllPages) + "]",
			"/api/wiki/recent" => "[" + string.Join(",", AllPages.Reverse()) + "]",
			"/api/wiki/category/guides" => Paged(request, [AllPages[0], AllPages[1]]),
			"/api/wiki/category/lore" => Paged(request, [AllPages[2]]),
			"/api/wiki/category/empty" => "[]",
			"/api/wiki/category/big" => Paged(request, BigCategory),
			"/api/wiki/category/realms" => Paged(request, [Page("5", "harbour_lore", "Harbour lore", "realms", ns: "category"), AllPages[2]]),
			_ => null,
		};
		var result = body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		if (path.StartsWith("/api/wiki/collections/", StringComparison.Ordinal))
		{
			result.Headers.Add("X-Total-Count", CollectionResults(request).Length.ToString());
			var (_, previous, next) = CollectionPage(request);
			if (previous is not null) result.Headers.Add("X-Previous-Page", previous);
			if (next is not null) result.Headers.Add("X-Next-Page", next);
		}
		return Task.FromResult(result);
	}

	/// <summary>Registers the fake API, a real WikiService, MudBlazor, localization and authorization on a bUnit context.</summary>
	public static (WikiApiFake Fake, BunitAuthorizationContext Auth) Install(TrackingBunitContext ctx)
	{
		var fake = new WikiApiFake();
		var client = ctx.Track(new HttpClient(fake) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		ctx.Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<SidebarCollapseService>()
			.AddLocalization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		var auth = ctx.AddAuthorization();
		auth.SetAuthorized("reader");
		return (fake, auth);
	}
}
