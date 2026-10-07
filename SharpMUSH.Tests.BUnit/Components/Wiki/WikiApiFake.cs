using System.Net;
using System.Text;
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
		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
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
