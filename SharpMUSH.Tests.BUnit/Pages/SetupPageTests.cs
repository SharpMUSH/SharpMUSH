using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// HttpMessageHandler faking the setup-wizard API surface: GET api/setup/status and
/// POST api/setup/complete. The complete response is configurable per test so tests can
/// exercise the happy path, validation-only path (never reaches the handler), and the
/// 409-conflict path (already completed / username taken).
///
/// On the happy path, api/setup/complete now mints a session exactly like account-login (auto-login
/// after first-run setup) — the fake echoes back the posted username so the success view's
/// "signed in as" copy can be asserted against what the test typed into the form.
/// </summary>
/// <param name="statusGate">
/// When set, api/setup/status does not answer until this task completes — the cold-load window in
/// which the claim form must not be on screen.
/// </param>
/// <param name="statusUnavailable">
/// When true, api/setup/status errors, which is what makes <c>NeedsSetupAsync</c> return null. That
/// answer must NOT bounce the visitor away: /setup is the only place first-run setup can happen.
/// </param>
/// <param name="wizard">
/// What api/setup/wizard answers once the game is claimed: null is a refusal (not the administrator),
/// otherwise whether the wizard is pending. The game has an HTTP handler (#8, carrying http-handler and
/// profile-handler), no event handler, and the Scene System.
/// </param>
file sealed class SetupApiHandler(
		bool needsSetup, HttpStatusCode completeStatus, string? completeBody, string completeSessionToken = "test-session-token",
		Task? statusGate = null, bool statusUnavailable = false, bool? wizard = null)
		: HttpMessageHandler
{
	private bool? _wizardPending = wizard;
	private int? _eventHandler;
	private int? _httpHandler = 8;
	private readonly HashSet<string> _installed = ["http-handler", "profile-handler", "common-functions", "plus-help", "scene"];

	/// <summary>What api/setup/wizard/handlers/{kind}/clashes answers for the HTTP handler; none by default.</summary>
	public List<object> HttpClashes { get; } = [];

	/// <summary>Each clash check, as "kind dbref", in order.</summary>
	public List<string> ClashChecks { get; } = [];

	/// <summary>Each PUT to api/setup/wizard/handlers/{kind}, as "kind body", in order.</summary>
	public List<string> HandlerChanges { get; } = [];

	/// <summary>The bodies PUT to api/setup/wizard/packages, in order.</summary>
	public List<string> PackageChoices { get; } = [];

	public int FinishCalls { get; private set; }

	/// <summary>How many times api/setup/wizard/starter-wiki was posted.</summary>
	public int StarterWikiCalls { get; private set; }

	/// <summary>Whether the game already has the starter wiki pages.</summary>
	public bool StarterWikiApplied { get; set; }

	/// <summary>Whether api/setup/wizard/starter-wiki refuses (409), as when some pages could not be written.</summary>
	public bool StarterWikiFails { get; set; }

	/// <summary>Whether api/setup/wizard/packages refuses (409).</summary>
	public bool PackagesFail { get; set; }

	/// <summary>Whether api/setup/wizard/finish refuses, leaving the wizard pending.</summary>
	public bool FinishFails { get; set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (path == "api/setup/wizard" && request.Method == HttpMethod.Get)
		{
			return _wizardPending is { } pending ? Json(Wizard(pending)) : new HttpResponseMessage(HttpStatusCode.Forbidden);
		}

		if (path.StartsWith("api/setup/wizard/handlers/", StringComparison.Ordinal) && path.EndsWith("/clashes", StringComparison.Ordinal))
		{
			var kind = path["api/setup/wizard/handlers/".Length..^"/clashes".Length];
			ClashChecks.Add($"{kind} {request.RequestUri.Query.TrimStart('?')}");
			return Json(kind == "http" ? HttpClashes : []);
		}

		if (path.StartsWith("api/setup/wizard/handlers/", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
		{
			var kind = path["api/setup/wizard/handlers/".Length..];
			var body = await request.Content!.ReadAsStringAsync(cancellationToken);
			HandlerChanges.Add($"{kind} {body}");
			using var document = JsonDocument.Parse(body);
			int? target = document.RootElement.GetProperty("mode").GetString() switch
			{
				"create" => 120,
				"use" => document.RootElement.GetProperty("dbref").GetInt32(),
				_ => null
			};
			if (kind == "http") _httpHandler = target;
			else _eventHandler = target;
			return Json(Wizard(_wizardPending ?? true));
		}

		if (path == "api/setup/wizard/packages" && request.Method == HttpMethod.Put)
		{
			if (PackagesFail)
			{
				return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("Scene could not be installed.") };
			}

			var body = await request.Content!.ReadAsStringAsync(cancellationToken);
			PackageChoices.Add(body);
			using var document = JsonDocument.Parse(body);
			_installed.Clear();
			_installed.UnionWith(document.RootElement.GetProperty("installed").EnumerateArray().Select(e => e.GetString()!));
			return Json(Wizard(_wizardPending ?? true));
		}

		if (path == "api/setup/wizard/starter-wiki" && request.Method == HttpMethod.Post)
		{
			StarterWikiCalls++;
			StarterWikiApplied = true;
			return StarterWikiFails
				? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("Theme could not be written.") }
				: Json(Wizard(_wizardPending ?? true));
		}

		if (path == "api/setup/wizard/finish" && request.Method == HttpMethod.Post)
		{
			FinishCalls++;
			if (FinishFails)
			{
				return new HttpResponseMessage(HttpStatusCode.InternalServerError);
			}

			_wizardPending = false;
			return new HttpResponseMessage(HttpStatusCode.NoContent);
		}

		if (request.Method == HttpMethod.Get && path == "api/setup/status")
		{
			if (statusGate is not null)
				await statusGate;

			return statusUnavailable
				? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				: Json(new { needsSetup });
		}

		if (request.Method == HttpMethod.Post && path == "api/setup/complete")
		{
			if (completeStatus == HttpStatusCode.OK)
			{
				var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
				using var requestBody = JsonDocument.Parse(requestJson);
				var username = requestBody.RootElement.GetProperty("username").GetString() ?? "headwiz";

				// The claim leaves the rest of the wizard pending, as SetupService does.
				_wizardPending ??= true;
				return Json(new
				{
					accountId = "test-account-id",
					username,
					characters = Array.Empty<object>(),
					accountSessionToken = completeSessionToken,
					mustChangePassword = false,
					role = completeSessionToken.Length == 0 ? "Guest" : "God",
					permissions = completeSessionToken.Length == 0 ? Array.Empty<string>() : new[] { "*" },
				});
			}

			// The HttpResponseMessage constructed here is returned to the caller (the
			// HttpClient pipeline / AccountAuthService), which owns and disposes it — it must
			// not be disposed here. Building it in one expression (rather than a local `var
			// response` mutated afterwards) keeps CodeQL's disposal analysis from flagging a
			// local it was never meant to dispose.
			return new HttpResponseMessage(completeStatus)
			{
				Content = completeBody is not null ? new StringContent(completeBody) : null
			};
		}

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}

	private object Wizard(bool pending) => new
	{
		pending,
		starterWikiApplied = StarterWikiApplied,
		handlers = new object[]
		{
			Handler("http", _httpHandler, _httpHandler is null ? null : "HTTP Handler", ["http-handler", "profile-handler"]),
			Handler("event", _eventHandler, _eventHandler is null ? null : "Event Handler", []),
		},
		packages = new[]
		{
			Package("http-handler", "http", []),
			Package("profile-handler", "http", ["http-handler"]),
			Package("room-contents", "event", [], recommended: true),
			Package("common-functions", null, []),
			Package("plus-help", null, []),
			Package("scene", null, ["plus-help", "common-functions"]),
			Package("wiki-reader", null, ["plus-help", "common-functions"]),
		},
	};

	private object Handler(string kind, int? dbref, string? name, string[] packages) =>
		new { kind, dbref, name, isWizard = true, packages = dbref is null ? [] : packages.Where(_installed.Contains).ToArray() };

	private object Package(string id, string? requires, string[] dependsOn, bool recommended = false) => new
	{
		recommended,
		id,
		description = $"{id} manifest description",
		installed = _installed.Contains(id),
		requires,
		available = requires switch { "http" => _httpHandler is not null, "event" => _eventHandler is not null, _ => true },
		dependsOn,
	};

	private static HttpResponseMessage Json<T>(T value) =>
			new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

/// <summary>Helper to register a real AccountAuthService backed by <see cref="SetupApiHandler"/>.</summary>
file static class SetupTestServices
{
	/// <summary>
	/// Wires up the substitute <see cref="IHttpClientFactory"/> and returns the <see cref="HttpClient"/>
	/// it hands out, so the caller can take ownership of disposing it (registering the instance
	/// itself as a DI singleton does not get it disposed: the container only auto-disposes
	/// services it resolves through a factory call site, and nothing here resolves a plain
	/// <see cref="HttpClient"/> from <c>ctx.Services</c> — everything goes through the factory
	/// substitute instead).
	/// </summary>
	public static HttpClient AddSetupTestServices(
			this TrackingBunitContext ctx, bool needsSetup, HttpStatusCode completeStatus = HttpStatusCode.OK,
			string? completeBody = null, string completeSessionToken = "test-session-token",
			Task? statusGate = null, bool statusUnavailable = false, bool? wizard = null)
		=> ctx.AddSetupTestServices(out _, needsSetup, completeStatus, completeBody, completeSessionToken, statusGate,
			statusUnavailable, wizard);

	public static HttpClient AddSetupTestServices(
			this TrackingBunitContext ctx, out SetupApiHandler handler, bool needsSetup,
			HttpStatusCode completeStatus = HttpStatusCode.OK, string? completeBody = null,
			string completeSessionToken = "test-session-token", Task? statusGate = null, bool statusUnavailable = false,
			bool? wizard = null)
	{
		handler = new SetupApiHandler(needsSetup, completeStatus, completeBody, completeSessionToken, statusGate,
			statusUnavailable, wizard);
		var apiClient = ctx.Track(new HttpClient(handler)
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		ctx.Services
				.AddMudServices()
				.AddSingleton(factory)
				.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
				.AddSingleton(sp => new AccountAuthService(
						sp.GetRequiredService<IHttpClientFactory>(),
						sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
						NullLogger<AccountAuthService>.Instance, []))
				.AddSingleton<ServerInfoService>()
				.AddSingleton<SetupWizardService>();

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		return apiClient;
	}
}

/// <summary>
/// bUnit tests for the first-run setup wizard: password-confirmation validation and
/// 409-conflict error mapping ("someone else completed setup" / username taken).
/// </summary>
public class SetupPageTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> ownedHttpClients = [];

	[TUnit.Core.Test]
	public async Task Setup_UsesOnboardingLayout()
	{
		var layoutAttribute = typeof(SharpMUSH.Client.Pages.Setup)
				.GetCustomAttributes(typeof(LayoutAttribute), inherit: true)
				.Cast<LayoutAttribute>()
				.SingleOrDefault();

		await Assert.That(layoutAttribute).IsNotNull();
		await Assert.That(layoutAttribute!.LayoutType).IsEqualTo(typeof(OnboardingLayout));
	}

	/// <summary>
	/// The measured defect: on a cold load of /setup against an ALREADY-CLAIMED game, the claim form
	/// ("This game hasn't been claimed yet") was on screen for ~12 seconds before the status check
	/// came back and redirected. The POST it invited was safely rejected with a 409, so this was
	/// never a security hole — it just told every visitor a lie about the game they were looking at.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_DoesNotShowTheClaimFormWhileTheStatusIsStillUnknown()
	{
		var gate = new TaskCompletionSource();
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true, statusGate: gate.Task));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();

		// Status still outstanding: no form, no "claim the administrator account" copy.
		await Assert.That(cut.FindAll("#setup-username")).IsEmpty();
		await Assert.That(cut.Markup).DoesNotContain("AuthClaimAdminTitle");
		await Assert.That(cut.Markup).Contains("Loading");

		gate.SetResult();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("#setup-username").Count == 0)
				throw new InvalidOperationException("claim form not rendered yet");
		});
		await Assert.That(cut.Markup).Contains("AuthClaimAdminTitle");
	}

	[TUnit.Core.Test]
	public async Task Setup_AlreadyComplete_NeverRendersTheClaimFormAndRedirectsHome()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: false));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();

		await Assert.That(cut.FindAll("#setup-username")).IsEmpty();
		await Assert.That(cut.Markup).DoesNotContain("AuthClaimAdminTitle");

		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		await Assert.That(nav.History.Single().Uri).IsEqualTo("/");
	}

	/// <summary>
	/// The deliberate behaviour this fix had to preserve: an unknown-yet status hides the form, but a
	/// status check that came back and FAILED still shows it. /setup is the only place first-run setup
	/// can be completed, so a transient/unreachable server must not lock the operator out of it.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_StatusUnavailable_StillShowsTheClaimFormAndDoesNotRedirect()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true, statusUnavailable: true));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("#setup-username").Count == 0)
				throw new InvalidOperationException("claim form not rendered yet");
		});

		await Assert.That(cut.Markup).Contains("AuthClaimAdminTitle");

		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		await Assert.That(nav.History).IsEmpty();
	}

	/// <summary>
	/// The claim hands straight over to the rest of the wizard as the new administrator: the database step,
	/// then the handlers — keeping the HTTP handler the game has and creating the event handler it lacks — then
	/// the packages, sent with what they need, then the finished state.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_Success_WalksTheAdministratorThroughImportHandlersAndPackages()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: true));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		await cut.Find("#setup-username").ChangeAsync("headwiz");
		await cut.Find("#setup-password").ChangeAsync("password-one");
		await cut.Find("#setup-confirm").ChangeAsync("password-one");
		await cut.Find("button.setup-submit").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AdmSetupImportTitle"))
				throw new InvalidOperationException("import step not rendered yet");
		});

		// Auto-login after the claim: signed in already, and told so.
		await Assert.That(cut.Markup).Contains("AuthSetupSignedInAs");
		var accountAuth = Services.GetRequiredService<AccountAuthService>();
		await Assert.That(accountAuth.IsLoggedIn).IsTrue();
		await Assert.That(accountAuth.Username).IsEqualTo("headwiz");
		await Assert.That(accountAuth.Role).IsEqualTo("God");
		await Assert.That(cut.Find("a.setup-import").GetAttribute("href")).IsEqualTo("/admin/import?setup=1");

		await cut.Find("button.setup-fresh").ClickAsync();

		// The handler the game has is kept by default; the one it lacks is created.
		await Assert.That(cut.Find("#setup-handler-http input[value='keep']").HasAttribute("checked")).IsTrue();
		await Assert.That(cut.Find("#setup-handler-http .setup-handler-now").TextContent).Contains("http-handler, profile-handler");
		await Assert.That(cut.FindAll("#setup-handler-event input[value='keep']")).IsEmpty();
		await Assert.That(cut.Find("#setup-handler-event input[value='create']").HasAttribute("checked")).IsTrue();
		await cut.Find("button.setup-save-handlers").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AdmSetupPackagesTitle"))
				throw new InvalidOperationException("packages step not rendered yet");
		});
		await Assert.That(handler.HandlerChanges).IsEquivalentTo(["event {\"mode\":\"create\",\"dbref\":null}"])
			.Because("keeping the HTTP handler changes nothing, so it is not sent");

		// What a ticked package needs is ticked with it and cannot be unticked on its own.
		await Assert.That(cut.Find("#setup-pkg-scene").HasAttribute("checked")).IsTrue();
		await Assert.That(cut.Find("#setup-pkg-plus-help").HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.Find("#setup-pkg-room-contents").HasAttribute("disabled")).IsFalse()
			.Because("the event handler it needs was just created");
		await Assert.That(cut.Find("#setup-pkg-room-contents").HasAttribute("checked")).IsTrue()
			.Because("a new game has it, and the handler it builds on is there now");

		await cut.Find("#setup-pkg-scene").ChangeAsync(false);
		await cut.Find("#setup-pkg-wiki-reader").ChangeAsync(true);
		await cut.Find("button.setup-save").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthSetupComplete"))
				throw new InvalidOperationException("finished state not rendered yet");
		});

		await Assert.That(handler.PackageChoices).HasSingleItem();
		using (var sent = JsonDocument.Parse(handler.PackageChoices[0]))
		{
			var installed = sent.RootElement.GetProperty("installed").EnumerateArray().Select(e => e.GetString()!).ToList();
			await Assert.That(installed).IsEquivalentTo(
				["http-handler", "profile-handler", "room-contents", "common-functions", "plus-help", "wiki-reader"]);
		}
		await Assert.That(handler.FinishCalls).IsEqualTo(1);
		await Assert.That(handler.StarterWikiCalls).IsEqualTo(1)
			.Because("a new game is offered the starter wiki pages ticked");

		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		var enterPortalButton = cut.Find("button.setup-signin");
		await Assert.That(enterPortalButton.TextContent).Contains("AuthEnterPortal");
		await enterPortalButton.ClickAsync();
		await Assert.That(nav.Uri).IsEqualTo(nav.BaseUri);
	}

	/// <summary>
	/// A wizard the server could not mark finished comes back at the next sign-in, so the page says so and
	/// stays on the applications rather than showing the setup as done.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_Packages_AFinishTheServerRefuses_IsNotShownAsDone()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		handler.FinishFails = true;
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=packages");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("button.setup-save"));
		await cut.Find("button.setup-save").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".setup-error"));
		await Assert.That(cut.Find(".setup-error").TextContent).Contains("AdmSetupFinishFailed");
		await Assert.That(cut.Markup).DoesNotContain("AuthSetupComplete");
		await Assert.That(handler.FinishCalls).IsEqualTo(1);
	}

	/// <summary>An administrator who unticks the starter wiki pages gets none.</summary>
	[TUnit.Core.Test]
	public async Task Setup_Packages_StarterWikiUnticked_IsNotWritten()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=packages");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("#setup-starter-wiki"));
		await Assert.That(cut.Find("#setup-starter-wiki").HasAttribute("checked")).IsTrue();

		await cut.Find("#setup-starter-wiki").ChangeAsync(false);
		await cut.Find("button.setup-save").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthSetupComplete"))
				throw new InvalidOperationException("finished state not rendered yet");
		});
		await Assert.That(handler.StarterWikiCalls).IsEqualTo(0);
	}

	/// <summary>A failed package save keeps the administrator's unticked choice, so saving again writes no pages.</summary>
	[TUnit.Core.Test]
	public async Task Setup_Packages_StarterWikiUnticked_SurvivesAFailedSave()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		handler.PackagesFail = true;
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=packages");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("#setup-starter-wiki"));
		await cut.Find("#setup-starter-wiki").ChangeAsync(false);
		await cut.Find("button.setup-save").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".setup-error"));
		await Assert.That(cut.Find("#setup-starter-wiki").HasAttribute("checked")).IsFalse();

		handler.PackagesFail = false;
		await cut.Find("button.setup-save").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthSetupComplete"))
				throw new InvalidOperationException("finished state not rendered yet");
		});
		await Assert.That(handler.StarterWikiCalls).IsEqualTo(0);
	}

	/// <summary>
	/// The starter pages are offered once: a game that has them sees the box ticked and fixed, and saving does not
	/// write them again, so a page the administrator deleted stays deleted.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_Packages_StarterWikiApplied_IsNotWrittenAgain()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		handler.StarterWikiApplied = true;
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=packages");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("#setup-starter-wiki"));
		await Assert.That(cut.Find("#setup-starter-wiki").HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.Markup).Contains("AdmSetupStarterWikiApplied");

		await cut.Find("button.setup-save").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthSetupComplete"))
				throw new InvalidOperationException("finished state not rendered yet");
		});
		await Assert.That(handler.StarterWikiCalls).IsEqualTo(0);
	}

	/// <summary>
	/// Starter pages the server could not all write keep the wizard on this step with the reason; the packages are
	/// saved, and saving again finishes without writing the pages a second time.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_Packages_StarterWikiFailure_ShowsTheReason()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		handler.StarterWikiFails = true;
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=packages");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("button.setup-save"));
		await cut.Find("button.setup-save").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".setup-error"));
		await Assert.That(cut.Find(".setup-error").TextContent).Contains("AdmSetupStarterWikiFailed");
		await Assert.That(handler.FinishCalls).IsEqualTo(0);

		await cut.Find("button.setup-save").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthSetupComplete"))
				throw new InvalidOperationException("finished state not rendered yet");
		});
		await Assert.That(handler.StarterWikiCalls).IsEqualTo(1);
	}

	/// <summary>
	/// "Use another object" builds on an object the game already has — a PennMUSH game's own handler — and
	/// asks for its number before sending anything.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_Handlers_UseAnObjectTheGameHas()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=handlers");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("#setup-handler-http"));

		await cut.Find("#setup-handler-http input[value='use']").ChangeAsync(true);
		await cut.Find("#setup-handler-event input[value='none']").ChangeAsync(true);
		await cut.Find("button.setup-save-handlers").ClickAsync();
		await Assert.That(cut.Find(".setup-error").TextContent).Contains("AdmSetupHandlerDbrefRequired");
		await Assert.That(handler.HandlerChanges).IsEmpty();

		await cut.Find("#setup-handler-http-dbref").ChangeAsync("#46");
		await cut.Find("button.setup-save-handlers").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AdmSetupPackagesTitle"))
				throw new InvalidOperationException("packages step not rendered yet");
		});
		await Assert.That(handler.HandlerChanges).IsEquivalentTo([
			"http {\"mode\":\"use\",\"dbref\":46}",
			"event {\"mode\":\"none\",\"dbref\":null}",
		]);
	}

	/// <summary>
	/// Building onto an object that already holds some of the packages' attributes keeps its values, so those
	/// parts of the packages would not run as shipped. The wizard lists them and goes no further until the
	/// administrator chooses to use the object anyway.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_Handlers_AnObjectWithClashingAttributes_NeedsAnExplicitChoice()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(out var handler, needsSetup: false, wizard: true));
		handler.HttpClashes.Add(new { package = "http-handler", attribute = "GET" });
		handler.HttpClashes.Add(new { package = "profile-handler", attribute = "GET`ONLINE" });
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/setup?step=handlers");

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		cut.WaitForAssertion(() => cut.Find("#setup-handler-http"));
		await cut.Find("#setup-handler-event input[value='none']").ChangeAsync(true);

		await cut.Find("button.setup-save-handlers").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("#setup-handler-http .setup-handler-clashes"));

		await Assert.That(handler.ClashChecks).IsEquivalentTo(["http dbref=8"])
			.Because("only the kept HTTP handler is an existing object to build onto");
		await Assert.That(cut.Find("#setup-handler-http .setup-handler-clashes").TextContent).Contains("GET`ONLINE");
		await Assert.That(handler.HandlerChanges).IsEmpty().Because("nothing changes before the choice is made");
		await Assert.That(cut.Markup).DoesNotContain("AdmSetupPackagesTitle");

		await cut.Find("#setup-handler-http-accept").ChangeAsync(true);
		await cut.Find("button.setup-save-handlers").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AdmSetupPackagesTitle"))
				throw new InvalidOperationException("packages step not rendered yet");
		});
		await Assert.That(handler.ClashChecks).HasSingleItem().Because("the same object is not checked twice");
		await Assert.That(handler.HandlerChanges).IsEquivalentTo(["event {\"mode\":\"none\",\"dbref\":null}"]);
	}

	/// <summary>
	/// An administrator who closed the tab after the claim comes back to the wizard, not the home page —
	/// and the database import page's way back lands them on the handlers step.
	/// </summary>
	[TUnit.Core.Test]
	[TUnit.Core.Arguments("/setup", "AdmSetupImportTitle")]
	[TUnit.Core.Arguments("/setup?step=handlers", "AdmSetupHandlersTitle")]
	[TUnit.Core.Arguments("/setup?step=packages", "AdmSetupPackagesTitle")]
	public async Task Setup_ClaimedWithTheWizardPending_ResumesIt(string address, string heading)
	{
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: false, wizard: true));
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo(address);

		var cut = Render<SharpMUSH.Client.Pages.Setup>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(heading))
				throw new InvalidOperationException("wizard step not rendered yet");
		});
		await Assert.That(cut.FindAll("#setup-username")).IsEmpty();
		await Assert.That(nav.Uri).IsEqualTo(nav.ToAbsoluteUri(address).ToString());
	}

	[TUnit.Core.Test]
	public async Task Setup_ClaimedWithTheWizardFinished_RedirectsHome()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: false, wizard: false));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();

		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		cut.WaitForAssertion(() =>
		{
			if (nav.History.Count == 0)
				throw new InvalidOperationException("not redirected yet");
		});
		await Assert.That(nav.History.Single().Uri).IsEqualTo("/");
		await Assert.That(cut.Markup).DoesNotContain("AdmSetupImportTitle");
	}

	[TUnit.Core.Test]
	public async Task Setup_Success_EmptySessionToken_ShowsSignInVariantAndDoesNotPersistSession()
	{
		// Claim succeeded (200) but the server degraded post-claim enrichment (see
		// SetupController.Complete's try/catch) and returned an empty AccountSessionToken.
		// The client must show the success view, but with the pre-auto-login "Sign in" link
		// variant instead of "Enter the portal" — and must not persist a session.
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true, completeSessionToken: ""));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		await cut.Find("#setup-username").ChangeAsync("headwiz");
		await cut.Find("#setup-password").ChangeAsync("password-one");
		await cut.Find("#setup-confirm").ChangeAsync("password-one");
		await cut.Find("button.setup-submit").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("AuthSetupComplete"))
				throw new InvalidOperationException("success view not rendered yet");
		});

		await Assert.That(cut.Markup).Contains("AuthSetupComplete");
		await Assert.That(cut.Markup).Contains("AuthSetupSignInToManage");
		await Assert.That(cut.Markup).DoesNotContain("AuthSetupSignedInAs");

		var signInLink = cut.Find("a.setup-signin");
		await Assert.That(signInLink.GetAttribute("href")).IsEqualTo("/login?returnUrl=%2Fsetup");
		await Assert.That(signInLink.TextContent).Contains("AuthSignIn");

		var accountAuth = Services.GetRequiredService<AccountAuthService>();
		await Assert.That(accountAuth.IsLoggedIn).IsFalse();
	}

	/// <summary>
	/// /setup was the portal's only form page not using the shared field component: Login (5),
	/// Account (3) and CharacterCreate (2) all use MudTextField, /setup hand-rolled three bare
	/// &lt;input&gt; elements with uppercase micro-labels above them. It sits one click from /login
	/// inside the same OnboardingLayout, so the difference read as an inconsistency.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Setup_UsesTheSharedFieldComponentLikeEveryOtherFormPage()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();

		await Assert.That(cut.FindComponents<MudTextField<string>>().Count).IsEqualTo(3);
		await Assert.That(cut.FindAll(".setup-input")).IsEmpty();

		// Both password fields stay password fields, and the leading adornment icons match /login's.
		await Assert.That(cut.Find("#setup-password").GetAttribute("type")).IsEqualTo("password");
		await Assert.That(cut.Find("#setup-confirm").GetAttribute("type")).IsEqualTo("password");
		await Assert.That(cut.FindAll(".mud-input-adornment-start")).IsNotEmpty();
	}

	[TUnit.Core.Test]
	public async Task Setup_ValidatesPasswordConfirmation()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		await cut.Find("#setup-username").ChangeAsync("headwiz");
		await cut.Find("#setup-password").ChangeAsync("password-one");
		await cut.Find("#setup-confirm").ChangeAsync("password-two");
		await cut.Find("button.setup-submit").ClickAsync();

		await Assert.That(cut.Find(".setup-error").TextContent).Contains("AuthPasswordsDoNotMatch");
	}

	[TUnit.Core.Test]
	public async Task Setup_Conflict_ShowsClaimedMessage()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(
				needsSetup: true,
				completeStatus: HttpStatusCode.Conflict,
				completeBody: "Setup has already been completed."));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		await cut.Find("#setup-username").ChangeAsync("headwiz");
		await cut.Find("#setup-password").ChangeAsync("password-one");
		await cut.Find("#setup-confirm").ChangeAsync("password-one");
		await cut.Find("button.setup-submit").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".setup-error").TextContent.Contains("AuthSetupAlreadyCompleted"))
				throw new InvalidOperationException("conflict error not mapped yet");
		});

		await Assert.That(cut.Find(".setup-error").TextContent).Contains("AuthSetupAlreadyCompleted");
	}

	[TUnit.Core.Test]
	public async Task Setup_Conflict_UsernameTaken_ShowsFriendlyMessage()
	{
		ownedHttpClients.Add(this.AddSetupTestServices(
				needsSetup: true,
				completeStatus: HttpStatusCode.Conflict,
				completeBody: "Username is already taken."));

		var cut = Render<SharpMUSH.Client.Pages.Setup>();
		await cut.Find("#setup-username").ChangeAsync("headwiz");
		await cut.Find("#setup-password").ChangeAsync("password-one");
		await cut.Find("#setup-confirm").ChangeAsync("password-one");
		await cut.Find("button.setup-submit").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".setup-error").TextContent.Contains("AuthUsernameTaken"))
				throw new InvalidOperationException("conflict error not mapped yet");
		});

		await Assert.That(cut.Find(".setup-error").TextContent).Contains("AuthUsernameTaken");
	}

	/// <summary>
	/// Disposes the HttpClient(s) created for the substitute IHttpClientFactory. TUnit's
	/// disposer prefers <see cref="IAsyncDisposable"/> over <see cref="IDisposable"/> when a
	/// type implements both (as <see cref="BunitContext"/> does), so overriding only
	/// <c>Dispose</c> would never run. <see cref="BunitContext"/>'s own Dispose members aren't
	/// virtual, so this re-declares <see cref="IAsyncDisposable"/> to take over the interface's
	/// dispatch slot for this type; <c>base.DisposeAsync()</c> still runs to dispose bUnit's own
	/// service provider.
	/// </summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
