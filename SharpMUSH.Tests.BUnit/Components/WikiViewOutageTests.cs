using SharpMUSH.Library.API;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers 503 until <see cref="Up"/> is set, then the page.</summary>
internal sealed class RestartingWikiHandler : HttpMessageHandler
{
	public bool Up { get; set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
		Task.FromResult(Up
			? new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new WikiPageDto(
					Id: "home", Slug: "home", Title: "Front Page", Namespace: "main",
					MarkdownSource: "body", RenderedHtml: "<p>Welcome back</p>", PlainText: "Welcome back",
					CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
					IsRestricted: false, RevisionNumber: 1, Categories: [], Published: true))
			}
			: new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}

/// <summary>
/// A wiki page read while the game is restarting. The server not answering is not a missing page: the view
/// says it could not load, offers no "create this page", and shows the page once the server answers.
/// </summary>
public class WikiViewOutageTests : TrackingBunitContext
{
	private readonly RestartingWikiHandler _handler = new();

	public WikiViewOutageTests()
	{
		Services.AddMudServices();
		var auth = AddAuthorization();
		auth.SetAuthorized("admin");
		auth.SetPolicies("wiki.create");
		JSInterop.Mode = JSRuntimeMode.Loose;

		var apiClient = Track(new HttpClient(_handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
	}

	[Test]
	public async Task AnUnansweredRead_SaysSo_WithoutOfferingToCreateThePage_AndRecovers()
	{
		var cut = Render<WikiView>(p => p.Add(x => x.Slug, "home"));
		cut.WaitForState(() => cut.FindAll(".wiki-load-failed").Count == 1, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("WikiLoadFailed");
		await Assert.That(cut.Markup).DoesNotContain("WikiPageNotExist");
		await Assert.That(cut.Markup).DoesNotContain("CreateThisPage");

		_handler.Up = true;
		cut.Find(".wiki-load-failed button").Click();
		cut.WaitForState(() => cut.Markup.Contains("Welcome back", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".wiki-load-failed").Count).IsEqualTo(0);
	}

	/// <summary>Nobody clicks: the view asks again on its own and shows the page once the server answers.</summary>
	[Test]
	public async Task AnUnansweredRead_IsTriedAgain_WithoutAClick()
	{
		var cut = Render<WikiView>(p => p.Add(x => x.Slug, "home"));
		cut.WaitForState(() => cut.FindAll(".wiki-load-failed").Count == 1, TimeSpan.FromSeconds(5));

		_handler.Up = true;
		cut.WaitForState(() => cut.Markup.Contains("Welcome back", StringComparison.Ordinal), TimeSpan.FromSeconds(10));
		await Assert.That(cut.FindAll(".wiki-load-failed").Count).IsEqualTo(0);
	}
}
