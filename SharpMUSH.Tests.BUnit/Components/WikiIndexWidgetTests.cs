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
internal sealed class WikiListHandler : HttpMessageHandler
{
	private const string Pages = """
	[
	  {"id":"1","slug":"intro","title":"Getting Started","namespace":"wiki","markdownSource":"","renderedHtml":"","plainText":"","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00","isRestricted":false,"revisionNumber":1,"categories":["guides","house_rules"],"published":true},
	  {"id":"2","slug":"lore","title":"World Lore","namespace":"wiki","markdownSource":"","renderedHtml":"","plainText":"","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00","isRestricted":false,"revisionNumber":1,"categories":[],"published":true}
	]
	""";

	private static string FirstPage()
	{
		using var document = System.Text.Json.JsonDocument.Parse(Pages);
		return "[" + document.RootElement[0].GetRawText() + "]";
	}

	private readonly TaskCompletionSource _previews = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _previewsWaiting;

	/// <summary>When set, a category preview is answered only once <see cref="ReleasePreviews"/> is called.</summary>
	public bool HoldPreviews { get; init; }

	/// <summary>How many category previews have been asked for and not yet answered.</summary>
	public int PreviewsWaiting => Volatile.Read(ref _previewsWaiting);

	public void ReleasePreviews() => _previews.TrySetResult();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		if (HoldPreviews && request.RequestUri!.AbsolutePath.StartsWith("/api/wiki/collections/", StringComparison.Ordinal))
		{
			Interlocked.Increment(ref _previewsWaiting);
			await _previews.Task.WaitAsync(ct);
			Interlocked.Decrement(ref _previewsWaiting);
		}
		var body = request.RequestUri!.AbsolutePath switch
		{
			"/api/wiki/pages" => Pages,
			"/api/wiki/navigation" => """{"collection":"game","total":2,"collections":[],"categories":[{"key":"guides","name":"Guides","pages":1,"hasPage":true,"pinned":true},{"key":"house_rules","name":"House rules","pages":1,"hasPage":true,"pinned":true}]}""",
			_ when request.RequestUri.AbsolutePath.StartsWith("/api/wiki/collections/", StringComparison.Ordinal) => FirstPage(),
			"/api/wiki/pinned-categories" => """["guides","house_rules"]""",
			_ => null,
		};
		var result = body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		result.Headers.Add("X-Total-Count", "1");
		return result;
	}
}

/// <summary>
/// Confirms the Wiki Index widget renders the category grid from the REST page list: a card for every
/// pinned category a page names, and the page titles. A page in no pinned category has no card.
/// </summary>
public class WikiIndexWidgetTests : TrackingBunitContext
{
	private readonly WikiListHandler _api = new() { HoldPreviews = true };

	public WikiIndexWidgetTests()
	{
		var apiClient = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		// The D1 hero gates its New page capsule with AuthorizeView.
		AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task RendersCategoriesAndPages()
	{
		_api.ReleasePreviews();
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
		await Assert.That(markup).DoesNotContain("World Lore");
		await Assert.That(markup).DoesNotContain("NavWikiUncategorized");

		var names = cut.FindAll(".wiki-cat-name").Select(n => n.TextContent.Trim()).ToList();
		await Assert.That(names).IsEquivalentTo(["Guides", "House rules"])
			.Because("a page in two pinned categories is listed under each");
	}

	[TUnit.Core.Test]
	public async Task AsksForEveryPinnedCategoryPreviewAtOnce()
	{
		var cut = Render<WikiIndexWidget>();

		// Both pinned categories' previews are in flight before either is answered: the grid waits one
		// round trip, not one per category.
		cut.WaitForAssertion(() =>
		{
			if (_api.PreviewsWaiting != 2)
				throw new InvalidOperationException($"{_api.PreviewsWaiting} previews in flight");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".wiki-cat-card")).IsEmpty();

		_api.ReleasePreviews();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".wiki-cat-card").Count != 2)
				throw new InvalidOperationException("cards not rendered yet");
		}, TimeSpan.FromSeconds(5));
	}
}
