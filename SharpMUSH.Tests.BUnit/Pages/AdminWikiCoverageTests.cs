using SharpMUSH.Library.API;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Serves <c>/api/wiki/pages</c> from a fixed page list, <c>/api/wiki/counts</c> from it too, and
/// <c>/api/wiki/{slug}/translations</c> from a per-slug locale map, so the admin grid's coverage column
/// has something real to read. Every request URI is recorded in <see cref="Requests"/>.
/// </summary>
internal sealed class AdminWikiCoverageHandler(
	IReadOnlyList<WikiPageSummaryDto> pages,
	IReadOnlyDictionary<string, string[]> translations) : HttpMessageHandler
{
	private static readonly Regex _translationsRoute =
		new(@"^/api/wiki/([^/]+)/translations$", RegexOptions.Compiled);

	public System.Collections.Concurrent.ConcurrentQueue<Uri> Requests { get; } = new();

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		Requests.Enqueue(request.RequestUri!);
		var path = request.RequestUri!.AbsolutePath;

		if (path == "/api/wiki/counts")
		{
			var published = pages.Count(p => p.Published);
			var counts = new WikiPageCountsDto(pages.Count, published, pages.Count - published, pages.Count(p => p.IsRestricted));
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(counts) });
		}

		if (path == "/api/wiki/pages")
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(pages) };
			response.Headers.Add("X-Total-Count", pages.Count.ToString());
			return Task.FromResult(response);
		}

		if (_translationsRoute.Match(path) is { Success: true } match)
		{
			var slug = Uri.UnescapeDataString(match.Groups[1].Value);
			var locales = translations.TryGetValue(slug, out var found) ? found : [];
			var dtos = locales
				.Select(l => new WikiTranslationSummaryDto(
					l, $"{slug} ({l})", true, DateTimeOffset.UnixEpoch, 1))
				.ToList();
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(dtos) });
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Translation coverage on <c>/admin/wiki</c> is what makes an untranslated Help page findable, so the
/// assertions that matter are the ones about a page with <em>no</em> translations and about the
/// missing-only filter — a coverage column that only renders locales that exist would look correct while
/// leaving the gap it exists to surface invisible.
/// </summary>
public class AdminWikiCoverageTests : TrackingBunitContext
{
	private static WikiPageSummaryDto Summary(string slug, string title, bool published = true, bool isRestricted = false) => new(
		Id: slug,
		Slug: slug,
		Title: title,
		Namespace: "main",
		UpdatedAt: DateTimeOffset.UnixEpoch,
		IsRestricted: isRestricted,
		RevisionNumber: 1,
		Categories: [],
		Published: published,
		Locale: "en",
		IsFallback: false,
		Image: null,
		LastEditedBy: null);

	private AdminWikiCoverageHandler? _handler;

	private IRenderedComponent<SharpMUSH.Client.Pages.Admin.AdminWiki> RenderAdminWikiWith(
		IReadOnlyList<WikiPageSummaryDto> pages,
		Dictionary<string, string[]> translations)
	{
		_handler = new AdminWikiCoverageHandler(pages, translations);
		var apiClient = Track(new HttpClient(_handler)
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services.AddMudServices();
		Services.AddSingleton(apiClient);
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		AddAuthorization().SetAuthorized("headwiz");
		JSInterop.Mode = JSRuntimeMode.Loose;

		// The grid's MudSelect and MudTooltip both need a MudPopoverProvider in the render tree.
		var host = Render<Components.MudHarness>(p => p
			.AddChildContent<SharpMUSH.Client.Pages.Admin.AdminWiki>());
		var cut = host.FindComponent<SharpMUSH.Client.Pages.Admin.AdminWiki>();

		// MudDataGrid's ServerData load is asynchronous; wait for the first row to land.
		if (pages.Count > 0)
		{
			cut.WaitForAssertion(
				() =>
				{
					if (!cut.Markup.Contains("admin-wiki-coverage", StringComparison.Ordinal))
						throw new InvalidOperationException("grid rows not loaded yet");
				},
				TimeSpan.FromSeconds(5));
		}

		return cut;
	}

	[Test]
	public async Task Stats_come_from_the_counts_endpoint_not_from_listing_every_page()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("a", "A"), Summary("b", "B", published: false), Summary("c", "C", isRestricted: true)],
			translations: new());

		cut.WaitForAssertion(
			() =>
			{
				if (cut.Find("[data-stat='pages'] .adm-stat-value").TextContent != "3")
					throw new InvalidOperationException("stats not loaded yet");
			},
			TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("[data-stat='published'] .adm-stat-value").TextContent).IsEqualTo("2");
		await Assert.That(cut.Find("[data-stat='drafts'] .adm-stat-value").TextContent).IsEqualTo("1");
		await Assert.That(cut.Find("[data-stat='restricted'] .adm-stat-value").TextContent).IsEqualTo("1");
		await Assert.That(_handler!.Requests.Any(u => u.AbsolutePath == "/api/wiki/counts")).IsTrue();
		await Assert.That(_handler.Requests.Any(u => u.Query.Contains("take=2000", StringComparison.Ordinal))).IsFalse()
			.Because("the stats tiles no longer page through the whole wiki to count it");
	}

	[Test]
	public async Task Coverage_column_lists_each_pages_locales()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("dragons", "Dragons")],
			translations: new() { ["dragons"] = ["fr", "de"] });

		await Assert.That(cut.Markup).Contains("WikiTranslations");
		var coverage = cut.Find(".admin-wiki-coverage").TextContent;
		await Assert.That(coverage).Contains("fr");
		await Assert.That(coverage).Contains("de");
	}

	[Test]
	public async Task Coverage_column_marks_a_page_with_no_translations()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("lonely", "Lonely")],
			translations: new());

		await Assert.That(cut.Find(".admin-wiki-coverage").ClassList)
			.Contains("admin-wiki-coverage-none")
			.Because("an untranslated page is the thing staff came here to find");
	}

	[Test]
	public async Task Coverage_column_does_not_mark_a_translated_page_as_missing()
	{
		// Without this the "none" test above passes just as well against a class applied unconditionally.
		var cut = RenderAdminWikiWith(
			pages: [Summary("dragons", "Dragons")],
			translations: new() { ["dragons"] = ["fr"] });

		await Assert.That(cut.Find(".admin-wiki-coverage").ClassList)
			.DoesNotContain("admin-wiki-coverage-none");
	}

	[Test]
	public async Task Locale_filter_offers_every_portal_locale_and_a_missing_only_option()
	{
		// Asserted against the option set rather than the markup: a MudSelect does not render its items
		// until its popover opens, so a markup assertion here would only prove the label is absent.
		var cut = RenderAdminWikiWith(pages: [Summary("dragons", "Dragons")], translations: new());

		var options = cut.Instance.LocaleFilterOptions;

		await Assert.That(cut.Markup).Contains("WikiLocaleFilter");
		await Assert.That(options.Select(o => o.Value)).Contains(string.Empty);
		foreach (var locale in SharpMUSH.Client.Resources.PortalLocales.Codes)
		{
			await Assert.That(options.Select(o => o.Value))
				.Contains(locale)
				.Because($"{locale} must be filterable");
			await Assert.That(options.Select(o => o.Value))
				.Contains($"{locale}:missing")
				.Because($"pages lacking {locale} are the ones staff came here to find");
		}
		await Assert.That(options.Any(o => o.Label.Contains("WikiUntranslatedOnly", StringComparison.Ordinal)))
			.IsTrue();
	}

	[Test]
	public async Task Missing_only_filter_hides_pages_that_already_have_that_locale()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("done", "Done"), Summary("todo", "Todo")],
			translations: new() { ["done"] = ["fr"] });

		await cut.InvokeAsync(() => cut.Instance.SetLocaleFilterAsync("fr:missing"));

		await Assert.That(cut.Markup).Contains("Todo");
		await Assert.That(cut.Markup).DoesNotContain("Done");
	}

	[Test]
	public async Task Positive_locale_filter_keeps_only_pages_that_have_it()
	{
		// The mirror of the case above: ":missing" must not be the only branch that filters anything.
		var cut = RenderAdminWikiWith(
			pages: [Summary("done", "Done"), Summary("todo", "Todo")],
			translations: new() { ["done"] = ["fr"] });

		await cut.InvokeAsync(() => cut.Instance.SetLocaleFilterAsync("fr"));

		await Assert.That(cut.Markup).Contains("Done");
		await Assert.That(cut.Markup).DoesNotContain("Todo");
	}
}
