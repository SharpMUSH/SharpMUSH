using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Answers for one page only after <see cref="Release"/> is called, so a fetch can be left in flight
/// while the widget is pointed somewhere else. Every other page resolves immediately.
/// </summary>
file sealed class GatedWikiHandler(string gatedSlug, params string[] existingSlugs) : HttpMessageHandler
{
	private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public void Release() => _gate.TrySetResult();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		var slug = path[(path.LastIndexOf('/') + 1)..];

		if (slug == gatedSlug)
		{
			await _gate.Task;
		}

		return existingSlugs.Contains(slug)
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Page(slug)) }
			: new HttpResponseMessage(HttpStatusCode.NotFound);
	}

	private static WikiController.WikiPageDto Page(string slug) => new(
		Id: slug, Slug: slug, Title: slug, Namespace: "main",
		MarkdownSource: "body", RenderedHtml: "<p>body</p>", PlainText: "body",
		CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
		IsProtected: false, RevisionNumber: 1,
		Category: "general", Tags: [], Published: true);
}

/// <summary>
/// Two ways the widget's "does this page exist?" answer can go stale. Both hide a page that is really
/// there, which on the default home layout means a blank front page, so both are worth pinning.
/// </summary>
public class WikiBodyWidgetStaleLoadTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public WikiBodyWidgetStaleLoadTests()
	{
		Services.AddMudServices();
		Auth = AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>Registers the wiki stack over a handler, which the individual tests choose.</summary>
	private void UseHandler(HttpMessageHandler handler)
	{
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
	}

	private static JsonElement Config(string slug)
		=> JsonSerializer.SerializeToElement(new { slug },
			new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

	/// <summary>
	/// Reconfiguring the widget replaces its WikiView, but the outgoing one's fetch is already in the
	/// air. Its answer is about the page it was created for; letting it land on the new page hides a
	/// page that exists.
	/// </summary>
	[TUnit.Core.Test]
	public async Task LateAnswerFromAReplacedPage_DoesNotHideThePageNowOnScreen()
	{
		var handler = new GatedWikiHandler("gone", "here");
		UseHandler(handler);

		var cut = Render<WikiBodyWidget>(p => p.Add(x => x.Config, Config("gone")));

		// Point it somewhere real while the first fetch is still outstanding, then let that one land.
		cut.Render(p => p.Add(x => x.Config, Config("here")));
		cut.WaitForState(() => cut.Markup.Contains("wiki-body-widget", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
		handler.Release();

		// The stale "gone does not exist" must not take the frame with it.
		await Task.Delay(TimeSpan.FromMilliseconds(300));
		await Assert.That(cut.Markup).Contains("wiki-body-widget");
	}

	/// <summary>
	/// The right to write a page is re-read when the viewer signs in. Resolving it once at init left
	/// the missing-page offer hidden from an admin who signed in after the widget first rendered.
	/// </summary>
	[TUnit.Core.Test]
	public async Task GainingWriteAccess_RevealsTheOfferOnAMissingPage()
	{
		UseHandler(new GatedWikiHandler(gatedSlug: string.Empty));

		var cut = Render<WikiBodyWidget>(p => p.Add(x => x.Config, Config("unwritten")));

		cut.WaitForState(() => cut.Markup.Trim().Length == 0, TimeSpan.FromSeconds(5));

		// Policies first: SetAuthorized is what raises AuthenticationStateChanged, so a policy added
		// after it would not be in place when the widget re-reads its rights.
		Auth.SetPolicies("wiki.create");
		Auth.SetAuthorized("editor");

		cut.WaitForState(() => cut.Markup.Contains("CreateThisPage", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("CreateThisPage");
	}
}
