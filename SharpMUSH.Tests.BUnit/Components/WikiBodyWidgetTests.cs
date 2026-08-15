using System.Net;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Records the page URL the widget asked for and answers 404 so nothing renders.</summary>
file sealed class RecordingWikiHandler : HttpMessageHandler
{
	public string? RequestedPath { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		RequestedPath ??= request.RequestUri?.PathAndQuery;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Confirms which wiki page the Wiki Body widget addresses. The fetch URL
/// (<c>/api/wiki/ns/{ns}/{category}/{slug}</c>) is the observable answer, so these assert on the
/// recorded request rather than on rendered markup.
/// </summary>
public class WikiBodyWidgetTests : BunitContext
{
	// Typed as the base so the file-local handler never appears in this type's member signatures.
	private readonly HttpMessageHandler _handler = new RecordingWikiHandler();

	private string? RequestedPath => ((RecordingWikiHandler)_handler).RequestedPath;

	public WikiBodyWidgetTests()
	{
		var apiClient = new HttpClient(_handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance))
			.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static JsonElement BuildConfig(object obj)
		=> JsonSerializer.SerializeToElement(obj,
			new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

	private string? RenderAndCapture(Action<ComponentParameterCollectionBuilder<WikiBodyWidget>> configure)
	{
		var cut = Render<WikiBodyWidget>(configure);
		cut.WaitForState(() => RequestedPath is not null, TimeSpan.FromSeconds(5));
		return RequestedPath;
	}

	private string? RenderInProfileAndCapture(string character, JsonElement? config)
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext(character, false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<WikiBodyWidget>(c => c.Add(w => w.Config, config)));

		cut.WaitForState(() => RequestedPath is not null, TimeSpan.FromSeconds(5));
		return RequestedPath;
	}

	[TUnit.Core.Test]
	public async Task NoConfigAndNoContext_RendersNothing()
	{
		var cut = Render<WikiBodyWidget>();
		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
		await Assert.That(RequestedPath).IsNull();
	}

	[TUnit.Core.Test]
	public async Task ProfileContext_FetchesCharacterBiography()
	{
		var path = RenderInProfileAndCapture("Gandalf", config: null);
		await Assert.That(path).IsEqualTo("/api/wiki/ns/character/general/Gandalf");
	}

	[TUnit.Core.Test]
	public async Task CharacterShorthand_FetchesCharacterBiography()
	{
		var path = RenderAndCapture(p => p.Add(x => x.Config, BuildConfig(new { Character = "Frodo" })));
		await Assert.That(path).IsEqualTo("/api/wiki/ns/character/general/Frodo");
	}

	[TUnit.Core.Test]
	public async Task Slug_FetchesArbitraryPageFromMainNamespace()
	{
		var path = RenderAndCapture(p => p.Add(x => x.Config, BuildConfig(new { Slug = "house-rules" })));
		await Assert.That(path).IsEqualTo("/api/wiki/ns/main/general/house-rules");
	}

	[TUnit.Core.Test]
	public async Task SlugWithNamespaceAndCategory_FetchesThatPage()
	{
		var path = RenderAndCapture(p => p.Add(x => x.Config,
			BuildConfig(new { Slug = "combat", Namespace = "help", Category = "systems" })));

		await Assert.That(path).IsEqualTo("/api/wiki/ns/help/systems/combat");
	}

	[TUnit.Core.Test]
	public async Task Locale_IsPassedThrough()
	{
		var path = RenderAndCapture(p => p.Add(x => x.Config,
			BuildConfig(new { Slug = "house-rules", Locale = "fr" })));

		await Assert.That(path).IsEqualTo("/api/wiki/ns/main/general/house-rules?lang=fr");
	}

	[TUnit.Core.Test]
	public async Task ExplicitSlug_OutranksProfileContext()
	{
		// An admin who configures a page means it, even on a profile page.
		var path = RenderInProfileAndCapture("Gandalf", BuildConfig(new { Slug = "house-rules" }));
		await Assert.That(path).IsEqualTo("/api/wiki/ns/main/general/house-rules");
	}
}
