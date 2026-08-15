using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Serves the wiki page list the way the REST API does.</summary>
file sealed class WikiListHandler : HttpMessageHandler
{
	private const string Pages = """
	[
	  {"id":"1","slug":"intro","title":"Getting Started","namespace":"wiki","markdownSource":"","renderedHtml":"","plainText":"","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00","isProtected":false,"revisionNumber":1,"category":"guides","tags":[],"published":true},
	  {"id":"2","slug":"lore","title":"World Lore","namespace":"wiki","markdownSource":"","renderedHtml":"","plainText":"","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00","isProtected":false,"revisionNumber":1,"category":null,"published":true}
	]
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var body = request.RequestUri!.AbsolutePath == "/api/wiki/pages" ? Pages : null;
		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// Confirms the Wiki Index widget renders the category grid from the REST page list: a named category,
/// the "General" fallback for an uncategorized page, and the page titles.
/// </summary>
public class WikiIndexWidgetTests : TrackingBunitContext
{
	public WikiIndexWidgetTests()
	{
		var apiClient = Track(new HttpClient(new WikiListHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task RendersCategoriesAndPages()
	{
		var cut = Render<WikiIndexWidget>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Getting Started"))
			{
				throw new InvalidOperationException("pages not loaded yet");
			}
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Getting Started");
		await Assert.That(markup).Contains("World Lore");
		await Assert.That(markup).Contains("Guides");
		await Assert.That(markup).Contains("General");
	}
}
