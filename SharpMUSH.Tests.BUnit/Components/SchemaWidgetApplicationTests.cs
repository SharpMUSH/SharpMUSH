using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Serves the profile pipeline (roster for name→objid, the view schema, and the per-character data).</summary>
file sealed class ProfileAppHandler : HttpMessageHandler
{
	private const string Roster = """[{"name":"Gandalf","objid":"#5:1000","created":1000,"category":""}]""";

	private const string Schema = """
	{"kind":"view","schema_version":1,"pages":[{"key":"profile","order":1,"sections":[
	  {"name":"Demographics","order":1,"elements":[
	    {"kind":"field","key":"fullname","label":"Full Name","type":"text","visible_to":"public"}]}]}]}
	""";

	private const string Data = """
	{"character":"Gandalf","objid":"#5:1000","dbref":"5","fields":{
	  "fullname":{"value":"Gandalf the Grey","visible":true}}}
	""";

	// The application DTO returned by GET /api/applications/{slug} (camelCase, as MVC serializes it).
	private const string AppDto = """
	{"slug":"character-header","displayName":"Character Header","icon":"Badge","kind":"Widget",
	 "schemaUrl":"http/profile/schema","dataUrl":"http/profile?objid={objid}","submitRoute":null,
	 "minimumRole":"Guest","navPlacement":null,"zones":["MainContent"],"order":0,"owningPackage":null}
	""";

	private const string LiveSchema = """
	{"kind":"view","schema_version":1,"refresh":30,"pages":[{"key":"profile","order":1,"sections":[
	  {"name":"Demographics","order":1,"elements":[
	    {"kind":"field","key":"fullname","label":"Full Name","type":"text","visible_to":"public"}]}]}]}
	""";

	private int _liveReads;

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var body = request.RequestUri!.AbsolutePath switch
		{
			"/http/live/schema" => LiveSchema,
			"/http/live" => Interlocked.Increment(ref _liveReads) == 1 ? """{"fields":{},"shown":false}""" : Data,
			"/http/characters" => Roster,
			"/http/profile/schema" => Schema,
			"/http/profile" => Data,
			"/http/sheet" => """{"fields":{},"shown":false}""",
			"/api/applications/character-header" => AppDto,
			_ => null
		};
		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// Confirms SchemaWidget, when used as an application-backed widget, resolves its schema/data routes
/// from the application catalog by slug and fills the {objid} token from the cascading profile page
/// context — i.e. the Character Header application renders the routed character.
/// </summary>
public class SchemaWidgetApplicationTests : TrackingBunitContext
{
	public SchemaWidgetApplicationTests()
	{
		var apiClient = Track(new HttpClient(new ProfileAppHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		var characterHeaderApp = new PortalApplication(
			"character-header", "Character Header", "Badge", "Widget",
			"http/profile/schema", "http/profile?objid={objid}", null, "Guest", null,
			["MainContent"], 0);
		var sheetApp = new PortalApplication(
			"sheet", "Sheet", "Badge", "Widget",
			"http/profile/schema", "http/sheet?objid={objid}", null, "Guest", null,
			["MainContent"], 0);
		var liveApp = new PortalApplication(
			"live", "Live", "Badge", "Widget",
			"http/live/schema", "http/live?objid={objid}", null, "Guest", null,
			["MainContent"], 0);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([characterHeaderApp, sheetApp, liveApp]))
			.AddSingleton(Substitute.For<IPlayTerminalService>())
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton<TimeProvider>(_clock);

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>A clock that moves only when told, for the refresh timer.</summary>
	private sealed class Clock : TimeProvider
	{
		private TimerCallback? _tick;
		private object? _state;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			(_tick, _state) = (callback, state);
			return Substitute.For<ITimer>();
		}
		public void Tick() => _tick?.Invoke(_state);
	}

	private readonly Clock _clock = new();

	[TUnit.Core.Test]
	public async Task ApplicationBacked_ResolvesRoutesAndContext_RendersCharacter()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<SchemaWidget>(cp => cp.Add(w => w.WidgetName, "character-header")));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Gandalf the Grey"))
				throw new InvalidOperationException("profile data not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Demographics");        // section from schema
		await Assert.That(markup).Contains("Full Name");           // field label from schema
		await Assert.That(markup).Contains("Gandalf the Grey");    // data resolved via {objid} substitution
	}

	[TUnit.Core.Test]
	public async Task A_data_route_that_answers_not_shown_draws_nothing_at_all()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<SchemaWidget>(cp => cp.Add(w => w.WidgetName, "sheet")));

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-progress-circular").Count > 0)
				throw new InvalidOperationException("still loading");
		}, TimeSpan.FromSeconds(5));

		// Neither the view nor the "nothing to display" placeholder: the card is not there.
		await Assert.That(cut.Markup.Trim()).IsEmpty();
	}

	[TUnit.Core.Test]
	public async Task A_widget_not_shown_with_refresh_comes_back_when_its_data_route_answers_fields()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<SchemaWidget>(cp => cp.Add(w => w.WidgetName, "live")));

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mud-progress-circular").Count > 0)
				throw new InvalidOperationException("still loading");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup.Trim()).IsEmpty();

		_clock.Tick();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Gandalf the Grey"))
				throw new InvalidOperationException("refresh not shown yet");
		}, TimeSpan.FromSeconds(5));
	}
}

/// <summary>
/// Confirms the robustness fallback: with an EMPTY startup catalog, an application-backed SchemaWidget
/// still renders by lazily fetching its application by slug (GET /api/applications/{slug}). This is the
/// path that fixes "the header shows nothing" when the startup snapshot was empty.
/// </summary>
public class SchemaWidgetLazyFetchTests : TrackingBunitContext
{
	public SchemaWidgetLazyFetchTests()
	{
		var apiClient = Track(new HttpClient(new ProfileAppHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([]))   // empty — forces the lazy fetch path
			.AddSingleton(Substitute.For<IPlayTerminalService>())
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task EmptyCatalog_LazyFetchesAppBySlug_RendersCharacter()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<SchemaWidget>(cp => cp.Add(w => w.WidgetName, "character-header")));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Gandalf the Grey"))
				throw new InvalidOperationException("profile data not loaded yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Gandalf the Grey");
	}
}
