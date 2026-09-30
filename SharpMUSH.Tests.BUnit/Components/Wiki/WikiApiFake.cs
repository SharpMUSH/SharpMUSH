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
	public static string Page(string id, string slug, string title, string? category, string? image = null, string? editor = null, bool published = true, bool isProtected = false, string ns = "main") =>
		$$"""
		{"id":"{{id}}","slug":"{{slug}}","title":"{{title}}","namespace":"{{ns}}","markdownSource":"","renderedHtml":"","plainText":"",
		 "createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-0{{id}}T00:00:00+00:00","isProtected":{{(isProtected ? "true" : "false")}},"revisionNumber":1,
		 "category":{{(category is null ? "null" : $"\"{category}\"")}},"tags":[],"published":{{(published ? "true" : "false")}},
		 "image":{{(image is null ? "null" : $"\"{image}\"")}},"lastEditedBy":{{(editor is null ? "null" : $"\"{editor}\"")}}}
		""";

	public static readonly string[] AllPages =
	[
		Page("1", "intro", "Getting Started", "guides", "/api/wiki-assets/a/intro.jpg", "Ilsa Varn"),
		Page("2", "combat", "Combat Basics", "guides", null, "Tomas Reyes", published: false),
		Page("3", "harbour", "Harbour Ward", "lore", "/api/wiki-assets/b/harbour.jpg", "Wren", isProtected: true),
		Page("4", "notes", "Loose Notes", null, null, null),
	];

	public bool Refuse { get; set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		if (Refuse) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
		var path = request.RequestUri!.AbsolutePath;
		string? body = path switch
		{
			"/api/wiki/pages" => "[" + string.Join(",", AllPages) + "]",
			"/api/wiki/recent" => "[" + string.Join(",", AllPages.Reverse()) + "]",
			"/api/wiki/category/guides" => "[" + AllPages[0] + "," + AllPages[1] + "]",
			"/api/wiki/category/lore" => "[" + AllPages[2] + "]",
			"/api/wiki/category/empty" => "[]",
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
