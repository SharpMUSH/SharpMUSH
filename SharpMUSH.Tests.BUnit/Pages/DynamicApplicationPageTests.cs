using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Serves the seeded character-header application and the profile pipeline behind it, and records
/// every request URI so a test can assert what actually went over the wire — the reported bug was
/// visible only there: <c>GET /http/profile?objid=%7Bobjid%7D</c>, the literal placeholder.
/// </summary>
file sealed class DynamicAppHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	public ConcurrentQueue<string> Requests { get; } = new();

	private const string Schema = """
	{"kind":"view","schema_version":1,"pages":[{"key":"profile","order":1,"sections":[
	  {"name":"Demographics","order":1,"elements":[
	    {"kind":"field","key":"fullname","label":"Full Name","type":"text","visible_to":"public"}]}]}]}
	""";

	private const string Data = """
	{"character":"Alpha","objid":"#5:1000","dbref":"5","fields":{
	  "fullname":{"value":"Alpha the Grey","visible":true}}}
	""";

	private const string AppDto = """
	{"slug":"character-header","displayName":"Character Header","icon":"Badge","kind":"Widget",
	 "schemaUrl":"http/profile/schema","dataUrl":"http/profile?objid={objid}","submitRoute":null,
	 "minimumRole":"Guest","navPlacement":null,"zones":["MainContent"],"order":0,"owningPackage":null}
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var uri = request.RequestUri!;
		Requests.Enqueue(uri.PathAndQuery);

		if (uri.AbsolutePath == "/api/account/characters")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(characters) });

		var body = uri.AbsolutePath switch
		{
			"/api/applications/character-header" => AppDto,
			"/http/profile/schema" => Schema,
			// Only answers a request that carries a real objid. A request still holding the literal
			// {objid} placeholder gets nothing, which is precisely what produced "Nothing to display."
			"/http/profile" when uri.Query.Contains("%23", StringComparison.Ordinal) => Data,
			_ => null
		};

		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// The one shipped example application, /apps/character-header, rendered "Nothing to display." for
/// every persona including a God account that owns a character. The outbound request was
/// <c>GET /http/profile?objid=%7Bobjid%7D</c> — the placeholder was never substituted.
///
/// <para>
/// SchemaWidget fills that token from the cascading <c>ProfilePageContext</c>, which only exists on
/// /character/{name}. The standalone /apps/{slug} route has no such context, so nothing filled it and
/// the request went out verbatim. The page now resolves the token from the viewer's own acting
/// character, and says so plainly when there isn't one instead of reporting an impossible request as
/// an empty result.
/// </para>
/// </summary>
public class DynamicApplicationPageTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];
	private ConcurrentQueue<string> _requests = new();

	private static readonly PortalApplication CharacterHeader = new(
		"character-header", "Character Header", "Badge", "Widget",
		"http/profile/schema", "http/profile?objid={objid}", null, "Guest", null,
		["MainContent"], 0);

	private void Seed(bool loggedIn, IReadOnlyList<CharacterSummary> characters)
	{
		var handler = new DynamicAppHandler(characters);
		_requests = handler.Requests;
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([CharacterHeader]))
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new PluginComponentLoader(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<PluginComponentLoader>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken")
			.SetResult(loggedIn ? "session-token-1" : null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Player");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[]");

		Services.AddSingleton(new AccountAuthService(
			factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()));

		AddAuthorization();
	}

	private IRenderedComponent<DynamicApplication> RenderApp() =>
		Render<DynamicApplication>(p => p.Add(c => c.Slug, "character-header"));

	[Test]
	public async Task The_acting_characters_objid_replaces_the_placeholder()
	{
		// #5 created at 1000 → objid "#5:1000", which is what DBRef.ToString() spells.
		Seed(loggedIn: true, characters: [new CharacterSummary(5, 1000L, "Alpha", "", IsActing: true)]);

		var cut = RenderApp();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Alpha the Grey"))
				throw new InvalidOperationException("profile data not loaded yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Alpha the Grey");

		var profileRequest = _requests.Single(r => r.StartsWith("/http/profile?", StringComparison.Ordinal));
		await Assert.That(profileRequest).IsEqualTo("/http/profile?objid=%235%3A1000");
		await Assert.That(profileRequest).DoesNotContain("%7Bobjid%7D");
	}

	[Test]
	public async Task An_anonymous_visitor_is_told_why_and_offered_a_sign_in()
	{
		Seed(loggedIn: false, characters: []);

		var cut = RenderApp();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavApplicationNeedsCharacter"))
				throw new InvalidOperationException("explanation not rendered yet");
		}, TimeSpan.FromSeconds(5));

		// The point of the finding: not a silent empty result after an impossible request.
		await Assert.That(cut.Markup).DoesNotContain("WidNothingToDisplay");
		await Assert.That(cut.Find("a.app-alert-link").GetAttribute("href")).IsEqualTo("/login");
		await Assert.That(_requests.Any(r => r.StartsWith("/http/profile?", StringComparison.Ordinal))).IsFalse()
			.Because("a route whose token cannot be filled must not be fetched at all");
	}

	[Test]
	public async Task An_account_with_no_character_is_pointed_at_character_creation()
	{
		Seed(loggedIn: true, characters: []);

		var cut = RenderApp();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavApplicationNeedsCharacter"))
				throw new InvalidOperationException("explanation not rendered yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).DoesNotContain("WidNothingToDisplay");
		await Assert.That(cut.Find("a.app-alert-link").GetAttribute("href")).IsEqualTo("/characters/new");
	}

	/// <summary>
	/// The roster's acting marker is the server's answer to "who is this tab?", and an unmarked
	/// roster means the session token is bound to nobody — so an account that owns characters can
	/// still have no character to fill <c>{objid}</c> with. Substituting one anyway would ask the
	/// server for a profile under an identity it will not honour.
	/// </summary>
	[Test]
	public async Task A_roster_with_no_acting_character_fills_nothing_and_says_so()
	{
		Seed(loggedIn: true, characters: [new CharacterSummary(5, 1000L, "Alpha", "")]);

		var cut = RenderApp();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavApplicationNeedsCharacter"))
				throw new InvalidOperationException("explanation not rendered yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(_requests.Any(r => r.StartsWith("/http/profile?", StringComparison.Ordinal))).IsFalse()
			.Because("an unmarked roster is not an acting character, so there is nothing to substitute");
	}

	/// <summary>
	/// The correctness case behind the same rule: on a multi-character account the marked entry is
	/// the one the page renders, wherever it sits in the roster. Taking the first would show the
	/// viewer somebody else's profile under their own session.
	/// </summary>
	[Test]
	public async Task The_marked_character_wins_over_the_first_one_on_the_roster()
	{
		Seed(loggedIn: true, characters:
		[
			new CharacterSummary(7, 2000L, "Beta", ""),
			new CharacterSummary(5, 1000L, "Alpha", "", IsActing: true),
		]);

		var cut = RenderApp();
		cut.WaitForAssertion(() =>
		{
			if (!_requests.Any(r => r.StartsWith("/http/profile?", StringComparison.Ordinal)))
				throw new InvalidOperationException("profile not requested yet");
		}, TimeSpan.FromSeconds(5));

		var profileRequest = _requests.Single(r => r.StartsWith("/http/profile?", StringComparison.Ordinal));
		await Assert.That(profileRequest).IsEqualTo("/http/profile?objid=%235%3A1000");
	}

	/// <summary>
	/// Disposes the HttpClient(s) created for the substitute IHttpClientFactory. TUnit's disposer
	/// prefers IAsyncDisposable over IDisposable when a type implements both (as BunitContext does),
	/// so overriding only Dispose would never run.
	/// </summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}

/// <summary>
/// The second half of the same finding: /apps/{slug}'s topbar read "Applications" — the name of the
/// ADMIN page that manages applications — rather than the name of the application being looked at.
/// MainLayout now resolves the slug through the startup <see cref="ApplicationCatalog"/>.
/// </summary>
public class DynamicApplicationTopbarTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];

	private static readonly PortalApplication CharacterHeader = new(
		"character-header", "Character Header", "Badge", "Widget",
		"http/profile/schema", "http/profile?objid={objid}", null, "Guest", null,
		["MainContent"], 0);

	public DynamicApplicationTopbarTests()
	{
		var handler = new DynamicAppHandler([new CharacterSummary(5, 1000L, "Alpha", "")]);
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(new ApplicationCatalog([CharacterHeader]))
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionStateService>())
			.AddSingleton(Substitute.For<ILayoutService>());

		JSInterop.Mode = JSRuntimeMode.Loose;

		// MainLayout always mounts GlobalTerminal; "Production" keeps it out of the dev debug-OTT path.
		var hostEnv = Substitute.For<Microsoft.AspNetCore.Components.WebAssembly.Hosting.IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);

		var terminal = Substitute.For<ITerminalService>();
		var terminalHost = new TerminalServiceHost(() => terminal);
		Services.AddSingleton(terminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);

		var playTerminal = Substitute.For<IPlayTerminalService>();
		var playHost = new PlayTerminalServiceHost(() => playTerminal);
		Services.AddSingleton(playHost);
		Services.AddSingleton<IPlayTerminalService>(playHost);

		Services.AddSingleton(new AccountAuthService(
			factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()));
		Services.AddSingleton<CharacterSwitchService>();
		Services.AddSingleton<TerminalLoginService>();

		AddAuthorization().SetNotAuthorized();
	}

	[Test]
	public async Task The_topbar_names_the_application_not_the_admin_page()
	{
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
			.NavigateTo("/apps/character-header");

		var cut = Render<SharpMUSH.Client.Layout.MainLayout>();

		var title = cut.Find(".phosphor-topbar-title").TextContent.Trim();
		await Assert.That(title).IsEqualTo("Character Header");
		await Assert.That(title).IsNotEqualTo("LayApplications");
	}

	[Test]
	public async Task An_application_missing_from_the_boot_snapshot_falls_back_to_its_slug()
	{
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
			.NavigateTo("/apps/some-other-app");

		var cut = Render<SharpMUSH.Client.Layout.MainLayout>();

		await Assert.That(cut.Find(".phosphor-topbar-title").TextContent.Trim()).IsEqualTo("Some other app");
	}

	/// <summary>Disposes the HttpClient created for the substitute IHttpClientFactory.</summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
