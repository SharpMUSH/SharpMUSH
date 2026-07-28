using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Records every wiki request's URI and answers the revision and translation routes.
/// </summary>
/// <remarks>
/// Recording the wire URI rather than stubbing <c>WikiService</c> is deliberate: the thing that can be
/// wrong here is whether <c>lang</c> survives URL construction, and a service-level double would answer
/// that question by assuming it.
/// </remarks>
internal sealed class RecordingWikiHandler : HttpMessageHandler
{
	public List<Uri> Requests { get; } = [];

	public List<WikiController.WikiRevisionDto> Revisions { get; set; } =
	[
		new(2, "#1", DateTimeOffset.UnixEpoch, null, "v2"),
		new(1, "#1", DateTimeOffset.UnixEpoch, null, "v1"),
	];

	public List<WikiController.WikiTranslationSummaryDto> Translations { get; set; } =
		[new("fr", "Dragons (fr)", true, DateTimeOffset.UnixEpoch, 1)];

	public Uri? LastRequestTo(string pathSuffix) =>
		Requests.LastOrDefault(u => u.AbsolutePath.EndsWith(pathSuffix, StringComparison.Ordinal));

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var uri = request.RequestUri!;
		Requests.Add(uri);
		var path = uri.AbsolutePath;

		if (path.EndsWith("/revisions", StringComparison.Ordinal))
			return Task.FromResult(Json(Revisions));

		if (path.EndsWith("/translations", StringComparison.Ordinal))
			return Task.FromResult(Json(Translations));

		if (path.Contains("/revisions/", StringComparison.Ordinal))
			return Task.FromResult(Json(Revisions[0]));

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

	private static HttpResponseMessage Json<T>(T value) =>
		new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

/// <summary>
/// The history and diff pages must stay on one locale's revision stream. Numbering restarts at 1 per
/// locale, so a page that lists the French stream and then fetches revision numbers without
/// <c>?lang=</c> diffs French against English and renders it as a plausible rewrite rather than an error.
/// </summary>
public class WikiHistoryLocaleTests : BunitContext
{
	private readonly RecordingWikiHandler _handler = new();

	public WikiHistoryLocaleTests()
	{
		var apiClient = new HttpClient(_handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services.AddMudServices();
		Services.AddSingleton(apiClient);
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		AddAuthorization().SetAuthorized("reader");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	// bUnit refuses to set a [SupplyParameterFromQuery] parameter directly, which is the right call: the
	// query string is the thing under test here, so it has to arrive through the NavigationManager the way
	// a real navigation delivers it.
	private void NavigateTo(string uri) =>
		Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

	// The history rows use MudTooltip, which needs a MudPopoverProvider in the render tree, so the page is
	// hosted inside the shared MudHarness. The provider contributes only an empty container.
	private IRenderedComponent<SharpMUSH.Client.Pages.WikiPageHistory> RenderHistory(string? lang)
	{
		var query = lang is null ? string.Empty : $"?lang={lang}";
		NavigateTo($"/wiki/main/general/dragons/history{query}");

		var host = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.WikiPageHistory>(cp => cp
				.Add(c => c.Ns, "main")
				.Add(c => c.Category, "general")
				.Add(c => c.Slug, "dragons")));

		return host.FindComponent<SharpMUSH.Client.Pages.WikiPageHistory>();
	}

	[Test]
	public async Task History_page_requests_the_revisions_for_the_lang_query_parameter()
	{
		RenderHistory("fr");

		await Assert.That(_handler.LastRequestTo("/revisions")!.Query)
			.Contains("lang=fr")
			.Because("the history page must show the locale's own stream, not always the source's");
	}

	[Test]
	public async Task History_page_omits_lang_entirely_when_none_was_requested()
	{
		RenderHistory(null);

		await Assert.That(_handler.LastRequestTo("/revisions")!.Query)
			.DoesNotContain("lang=")
			.Because("an empty lang= would only make the prerender cache key and history noisier");
	}

	[Test]
	public async Task History_page_carries_lang_into_its_diff_links()
	{
		var cut = RenderHistory("fr");

		var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")).Where(h => h is not null).ToList();

		await Assert.That(hrefs.Any(h => h!.Contains("/diff?") && h.Contains("lang=fr")))
			.IsTrue()
			.Because("dropping lang on the way to the diff would diff the wrong locale's revisions");
	}

	[Test]
	public async Task History_page_offers_a_chip_per_translation_and_one_for_the_source()
	{
		var cut = RenderHistory(null);

		var hrefs = cut.FindAll(".wiki-lang-chips a").Select(a => a.GetAttribute("href")!).ToList();

		await Assert.That(hrefs).Contains("/wiki/main/general/dragons/history");
		await Assert.That(hrefs).Contains("/wiki/main/general/dragons/history?lang=fr");
	}

	[Test]
	public async Task Diff_page_requests_both_revisions_in_the_requested_locale()
	{
		NavigateTo("/wiki/main/general/dragons/diff?from=1&to=2&lang=fr");

		Render<SharpMUSH.Client.Pages.WikiPageDiff>(p => p
			.Add(c => c.Ns, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Slug, "dragons"));

		var revisionFetches = _handler.Requests
			.Where(u => u.AbsolutePath.Contains("/revisions/", StringComparison.Ordinal))
			.ToList();

		await Assert.That(revisionFetches.Count)
			.IsEqualTo(2)
			.Because("a diff needs both sides, and both must come from the same stream");
		await Assert.That(revisionFetches.All(u => u.Query.Contains("lang=fr", StringComparison.Ordinal)))
			.IsTrue()
			.Because("one side without lang would diff French against English");
	}
}
