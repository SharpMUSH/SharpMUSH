using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for the DB-backed, scope-aware <see cref="LayoutService"/>. A scriptable handler stands
/// in for <c>/api/layouts/{scope}</c> so we can assert default fallback (HTTP 404), caching, stored-
/// layout reads, and the save/reset round-trip.
/// </summary>
public class LayoutServiceTests
{
	/// <summary>HttpMessageHandler whose response is produced by a per-request callback, and which counts calls.</summary>
	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public int Calls { get; private set; }
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			Requests.Add(request);
			return Task.FromResult(respond(request));
		}
	}

	private static LayoutService Build(ScriptedHandler handler)
	{
		var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return new LayoutService(factory, Substitute.For<ILogger<LayoutService>>());
	}

	private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

	private static HttpResponseMessage Ok(LayoutConfiguration layout)
		=> new(HttpStatusCode.OK) { Content = JsonContent.Create(layout, options: LayoutSerialization.Options) };

	// ─── GetDefaultLayout ─────────────────────────────────────────────────

	[Test]
	public async Task GetDefaultLayout_Global_HasChromeZonesAndQuickLinks()
	{
		var svc = Build(new ScriptedHandler(_ => NotFound()));
		var layout = svc.GetDefaultLayout(LayoutScopes.Global);

		foreach (var zone in Enum.GetValues<WidgetZone>())
			await Assert.That(layout.Zones.ContainsKey(zone)).IsTrue();
		await Assert.That(layout.Zones[WidgetZone.TopBar][0].WidgetName).IsEqualTo("QuickLinks");
	}

	[Test]
	public async Task GetDefaultLayout_WikiIndex_HasWikiIndexWidget()
	{
		var svc = Build(new ScriptedHandler(_ => NotFound()));
		var layout = svc.GetDefaultLayout(LayoutScopes.WikiIndex);

		var main = layout.Zones[WidgetZone.MainContent];
		await Assert.That(main.Count).IsEqualTo(1);
		await Assert.That(main[0].WidgetName).IsEqualTo("WikiIndex");
	}

	[Test]
	public async Task GetDefaultLayout_Profile_HasHeaderBodyAndGallery()
	{
		var svc = Build(new ScriptedHandler(_ => NotFound()));
		var layout = svc.GetDefaultLayout(LayoutScopes.Profile);

		var main = layout.Zones[WidgetZone.MainContent];
		await Assert.That(main.Select(p => p.WidgetName)).Contains("character-header");
		await Assert.That(main.Select(p => p.WidgetName)).Contains("WikiBody");
		await Assert.That(layout.Zones[WidgetZone.RightSidebar][0].WidgetName).IsEqualTo("CharacterGallery");
	}

	// ─── GetLayoutAsync ───────────────────────────────────────────────────

	[Test]
	public async Task GetLayoutAsync_WhenNotFound_ReturnsScopeDefault()
	{
		var svc = Build(new ScriptedHandler(_ => NotFound()));
		var layout = await svc.GetLayoutAsync(LayoutScopes.WikiIndex);

		await Assert.That(layout.Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("WikiIndex");
	}

	[Test]
	public async Task GetLayoutAsync_CachesResult_HandlerCalledOnce()
	{
		var handler = new ScriptedHandler(_ => NotFound());
		var svc = Build(handler);

		var first = await svc.GetLayoutAsync(LayoutScopes.Global);
		var second = await svc.GetLayoutAsync(LayoutScopes.Global);

		await Assert.That(ReferenceEquals(first, second)).IsTrue();
		await Assert.That(handler.Calls).IsEqualTo(1);
	}

	[Test]
	public async Task GetLayoutAsync_WhenStored_ReturnsStoredLayout()
	{
		var stored = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WelcomeText", 0, null)]
			},
			new LayoutSettings(LeftSidebarEnabled: true, RightSidebarEnabled: false));

		var svc = Build(new ScriptedHandler(_ => Ok(stored)));
		var layout = await svc.GetLayoutAsync(LayoutScopes.Home);

		await Assert.That(layout.Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("WelcomeText");
		await Assert.That(layout.Settings.LeftSidebarEnabled).IsTrue();
	}

	[Test]
	public async Task GetLayoutAsync_ServerError_FallsBackToDefault()
	{
		var svc = Build(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
		{
			Content = new StringContent("boom", Encoding.UTF8)
		}));

		var layout = await svc.GetLayoutAsync(LayoutScopes.WikiIndex);
		await Assert.That(layout.Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("WikiIndex");
	}

	// ─── SaveLayoutAsync / ResetLayoutAsync ───────────────────────────────

	[Test]
	public async Task SaveLayoutAsync_Null_Throws()
	{
		var svc = Build(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
		await Assert.ThrowsAsync<ArgumentNullException>(
			async () => await svc.SaveLayoutAsync(LayoutScopes.Home, null!));
	}

	[Test]
	public async Task SaveLayoutAsync_Success_FiresEventAndUpdatesCache()
	{
		var svc = Build(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
		string? firedScope = null;
		svc.OnLayoutChanged += scope => firedScope = scope;

		var modified = svc.GetDefaultLayout(LayoutScopes.Home) with
		{
			Settings = new LayoutSettings(LeftSidebarEnabled: true, RightSidebarEnabled: true)
		};

		var ok = await svc.SaveLayoutAsync(LayoutScopes.Home, modified);

		await Assert.That(ok).IsTrue();
		await Assert.That(firedScope).IsEqualTo(LayoutScopes.Home);
		var cached = await svc.GetLayoutAsync(LayoutScopes.Home);
		await Assert.That(cached.Settings.LeftSidebarEnabled).IsTrue();
	}

	[Test]
	public async Task SaveLayoutAsync_ServerRejects_ReturnsFalse()
	{
		var svc = Build(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

		var ok = await svc.SaveLayoutAsync(LayoutScopes.Home, svc.GetDefaultLayout(LayoutScopes.Home));
		await Assert.That(ok).IsFalse();
	}

	[Test]
	public async Task ResetLayoutAsync_Success_RestoresDefaultInCache()
	{
		// First GET returns a stored layout; DELETE succeeds; cache should revert to default afterward.
		var stored = new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>> { [WidgetZone.MainContent] = [] },
			new LayoutSettings(LeftSidebarEnabled: true, RightSidebarEnabled: true));

		var svc = Build(new ScriptedHandler(req => req.Method == HttpMethod.Delete
			? new HttpResponseMessage(HttpStatusCode.OK)
			: Ok(stored)));

		var before = await svc.GetLayoutAsync(LayoutScopes.WikiIndex);
		await Assert.That(before.Settings.LeftSidebarEnabled).IsTrue();

		var ok = await svc.ResetLayoutAsync(LayoutScopes.WikiIndex);
		await Assert.That(ok).IsTrue();

		var after = await svc.GetLayoutAsync(LayoutScopes.WikiIndex);
		await Assert.That(after.Zones[WidgetZone.MainContent][0].WidgetName).IsEqualTo("WikiIndex");
	}
}
