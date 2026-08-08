using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Json;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Serves a roster on http/characters and a *different*, smaller connection list on http/online,
/// mirroring GET`CHARACTERS vs GET`ONLINE. The two deliberately disagree: that divergence is the
/// whole point of the fix, and a widget reading the wrong route shows up as a failure here.
/// </summary>
file sealed class PresenceHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category);

	/// <summary>Everyone who exists — includes a seeded principal nobody plays.</summary>
	private static readonly Row[] Roster =
	[
		new("Idle Ida", "#10:1000", 1000, ""),
		new("Package Manager", "#7:700", 700, "Wizard"),
		new("Connected Cass", "#12:1200", 1200, ""),
	];

	/// <summary>Only who actually holds a connection.</summary>
	private static readonly Row[] Online =
	[
		new("Connected Cass", "#12:1200", 1200, ""),
	];

	public int OnlineCalls { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Method != HttpMethod.Get)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		switch (request.RequestUri!.AbsolutePath)
		{
			case "/http/online":
				OnlineCalls++;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Online) });
			case "/http/characters":
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Roster) });
			default:
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}
}

/// <summary>
/// Serves the same character on several rows, which is what a connection-shaped online route
/// produces for anyone logged in twice — the shape measured live against GET`ONLINE, where one
/// character came back eight times under one objid. The widget must render people, not sockets.
/// </summary>
file sealed class DoubledConnectionHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category);

	private static readonly Row[] Online =
	[
		new("Solitaire", "#11:1100", 1100, ""),
		new("Solitaire", "#11:1100", 1100, ""),
		new("Solitaire", "#11:1100", 1100, ""),
		new("Castor", "#12:1200", 1200, ""),
	];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(request.RequestUri!.AbsolutePath == "/http/online"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Online) }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>Serves an empty connection list, for the nobody-connected state.</summary>
file sealed class NobodyOnlineHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(request.RequestUri!.AbsolutePath == "/http/online"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>
/// Serves 200 with a completely empty body — what the route emits if the softcode ever loses its
/// firstof() guard, because fold() drops its base case on an empty list. The widget must degrade
/// to the unavailable state rather than throwing out of OnInitializedAsync (a blank body raises
/// JsonException) — and specifically not to the nobody-connected state, which is a claim about the
/// game that a failed read cannot support.
/// </summary>
file sealed class EmptyBodyHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
}

/// <summary>
/// Serves valid JSON under a Content-Type carrying an unrecognised charset — what a redefined
/// handler produces from a typo in `@respond/type application/json; charset=…`. Reading the body
/// then throws InvalidOperationException before any JSON is parsed.
/// </summary>
file sealed class BadCharsetHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var content = new StringContent("[]");
		content.Headers.Remove("Content-Type");
		content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=bogus-charset");
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
	}
}

/// <summary>
/// The widget must render presence, not the roster. It previously called ListAsync() (every
/// character) while painting a presence dot on each row, so seeded principals like Package Manager
/// appeared to be connected.
/// </summary>
public class OnlineCharactersWidgetTests : BunitContext
{
	private static void Wire(BunitContext ctx, HttpMessageHandler handler)
	{
		var apiClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		ctx.Services
			.AddSingleton(apiClient)
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new CharacterDirectoryService(
				sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task ListsOnlyConnectedCharacters_NotTheWholeRoster()
	{
		var handler = new PresenceHandler();
		Wire(this, handler);

		var cut = Render<OnlineCharactersWidget>();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Connected Cass")) throw new InvalidOperationException("online list not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Connected Cass");
		// Exists in the roster but holds no connection — the old widget showed both of these.
		await Assert.That(markup).DoesNotContain("Idle Ida");
		await Assert.That(markup).DoesNotContain("Package Manager");
		// It must have asked the connection route, not derived presence from the roster.
		await Assert.That(handler.OnlineCalls).IsGreaterThan(0);
	}

	[TUnit.Core.Test]
	public async Task NobodyConnected_SaysSo_RatherThanShowingCharacters()
	{
		Wire(this, new NobodyOnlineHandler());

		var cut = Render<OnlineCharactersWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("mud-skeleton")) throw new InvalidOperationException("still loading");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("WidNobodyConnected");
	}

	[TUnit.Core.Test]
	public async Task ACharacterConnectedTwice_IsListedOnce()
	{
		Wire(this, new DoubledConnectionHandler());

		var cut = Render<OnlineCharactersWidget>();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Castor")) throw new InvalidOperationException("online list not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var rows = cut.FindAll("a[href^='/characters/']").Select(a => a.TextContent.Trim()).ToList();

		// Name ordering survives the deduplication: Castor before Solitaire, each exactly once.
		await Assert.That(rows).IsEquivalentTo(new[] { "Castor", "Solitaire" });
	}

	// An unrecognised charset makes reading the body throw InvalidOperationException before the
	// JSON parser is ever reached, so the JsonException filter does not cover it. Reachable from a
	// typo in a redefined handler's `@respond/type`.
	[TUnit.Core.Test]
	public async Task BadCharsetHeader_SaysUnavailable_NotNobodyConnected()
	{
		Wire(this, new BadCharsetHandler());

		var cut = Render<OnlineCharactersWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("mud-skeleton")) throw new InvalidOperationException("still loading");
		}, TimeSpan.FromSeconds(5));

		// Still degrades instead of throwing out of OnInitializedAsync — but a request that failed
		// is not evidence that the game is empty, and it used to be reported as exactly that.
		await Assert.That(cut.Markup).Contains("WidUnavailable");
		await Assert.That(cut.Markup).DoesNotContain("WidNobodyConnected");
	}

	[TUnit.Core.Test]
	public async Task EmptyResponseBody_SaysUnavailable_NotNobodyConnected()
	{
		Wire(this, new EmptyBodyHandler());

		var cut = Render<OnlineCharactersWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("mud-skeleton")) throw new InvalidOperationException("still loading");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("WidUnavailable");
		await Assert.That(cut.Markup).DoesNotContain("WidNobodyConnected");
	}
}
