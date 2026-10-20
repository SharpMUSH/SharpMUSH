using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Answers the sign-in service routes: the providers the sign-in page lists, the start of a sign-in, and the
/// finish (<see cref="Complete"/>, one answer per call in order). Records what the portal sent.
/// </summary>
internal sealed class ServiceSignInApiHandler : HttpMessageHandler
{
	public List<ExternalSignInProviderInfo> Providers { get; init; } = [];
	public Queue<(HttpStatusCode Status, object Body)> Complete { get; } = new();
	public List<JsonElement> Begun { get; } = [];
	public List<JsonElement> Completed { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Get && path == "api/auth/external/providers")
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Providers) };
		if (request.Method == HttpMethod.Post && path.StartsWith("api/auth/external/", StringComparison.Ordinal) && path.EndsWith("/begin"))
		{
			Begun.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken));
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new ExternalSignInBeginResponse("https://discord.com/oauth2/authorize?state=s"))
			};
		}
		if (request.Method == HttpMethod.Post && path == "api/auth/external/complete" && Complete.TryDequeue(out var answer))
		{
			Completed.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken));
			return new HttpResponseMessage(answer.Status) { Content = JsonContent.Create(answer.Body) };
		}

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}
}

/// <summary>
/// Signing in through another service from the portal: the sign-in page's service buttons, and the page the service
/// sends the browser back to, which signs in, links, or asks for a username for a new account.
/// </summary>
public class ExternalSignInPageTests : TrackingBunitContext, IAsyncDisposable
{
	private const string PendingKey = "sharpmush.externalSignIn";
	private readonly List<HttpClient> ownedHttpClients = [];

	private static readonly object Session = new
	{
		accountId = "acct-1",
		username = "ariel",
		characters = Array.Empty<object>(),
		accountSessionToken = "session-1",
		mustChangePassword = false,
		role = "Player",
		permissions = Array.Empty<string>()
	};

	private ServiceSignInApiHandler Seed(ServiceSignInApiHandler handler, string? pending)
	{
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []))
			.AddSingleton(Substitute.For<ITerminalService>())
			.AddSingleton<ExternalSignInService>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", PendingKey).SetResult(pending);
		return handler;
	}

	private static string Pending(bool linking = false, string? returnUrl = null) =>
		JsonSerializer.Serialize(new ExternalSignInService.Pending("binding-1", "discord", true, returnUrl, linking));

	private IRenderedComponent<SharpMUSH.Client.Pages.ExternalSignIn> RenderReturn(string query)
	{
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo($"/login/external{query}");
		return Render<SharpMUSH.Client.Pages.ExternalSignIn>();
	}

	[Test]
	public async Task Login_OffersEachService_AndSendsTheBrowserToIt()
	{
		var handler = Seed(new ServiceSignInApiHandler
		{
			Providers = [new ExternalSignInProviderInfo("discord", "Discord", ExternalSignInKinds.Discord)]
		}, null);

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		cut.WaitForAssertion(() => cut.Find("button.login-service"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("button.login-service").TextContent).Contains("AuthContinueWith(Discord)");

		await cut.Find("button.login-service").ClickAsync();
		var nav = Services.GetRequiredService<NavigationManager>();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.StartsWith("https://discord.com/", StringComparison.Ordinal)) throw new InvalidOperationException(nav.Uri);
		}, TimeSpan.FromSeconds(5));

		// The tab keeps the binding it started with, and the server got the same one.
		var saved = JSInterop.Invocations.Single(i => i.Identifier == "sessionStorage.setItem" && (string)i.Arguments[0]! == PendingKey);
		var pending = JsonSerializer.Deserialize<ExternalSignInService.Pending>((string)saved.Arguments[1]!)!;
		await Assert.That(handler.Begun.Single().GetProperty("binding").GetString()).IsEqualTo(pending.Binding);
		await Assert.That(pending.Binding.Length).IsGreaterThanOrEqualTo(32);
		await Assert.That(pending.Provider).IsEqualTo("discord");
		await Assert.That(pending.Linking).IsFalse();
	}

	[Test]
	public async Task Login_WithNoServices_ShowsNoServiceButtons()
	{
		Seed(new ServiceSignInApiHandler(), null);
		var cut = Render<SharpMUSH.Client.Pages.Login>();
		cut.WaitForAssertion(() => cut.Find(".login-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".login-service")).IsEmpty();
		await Assert.That(cut.FindAll(".login-or")).IsEmpty();
	}

	[Test]
	public async Task Return_ForANewIdentity_AsksForAUsername_ThenCreatesTheAccount()
	{
		var handler = Seed(new ServiceSignInApiHandler(), Pending());
		handler.Complete.Enqueue((HttpStatusCode.OK, new
		{
			outcome = "newAccount",
			providerName = "Discord",
			serviceName = "ariel",
			suggestedUsername = "ariel",
			suggestedEmail = "ariel@example.com",
			canCreate = true
		}));
		handler.Complete.Enqueue((HttpStatusCode.OK, new { outcome = "signedIn", providerName = "Discord", serviceName = "ariel", session = Session }));

		var cut = RenderReturn("?flow=flow-1");
		cut.WaitForAssertion(() => cut.Find("button.ext-create"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("AuthServiceNewAccountBody(Discord, ariel)");
		await Assert.That(cut.Find("#ext-username").GetAttribute("value")).IsEqualTo("ariel");

		await cut.Find("#ext-username").ChangeAsync("Ariel2");
		await cut.Find("button.ext-create").ClickAsync();

		var nav = Services.GetRequiredService<NavigationManager>();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/characters/new", StringComparison.Ordinal)) throw new InvalidOperationException(nav.Uri);
		}, TimeSpan.FromSeconds(5));
		var created = handler.Completed[1];
		await Assert.That(created.GetProperty("createAccount").GetBoolean()).IsTrue();
		await Assert.That(created.GetProperty("username").GetString()).IsEqualTo("Ariel2");
		await Assert.That(created.GetProperty("binding").GetString()).IsEqualTo("binding-1");
		await Assert.That(created.GetProperty("rememberMe").GetBoolean()).IsTrue();
	}

	[Test]
	public async Task Return_UsernameTaken_KeepsTheFormToTryAnother()
	{
		var handler = Seed(new ServiceSignInApiHandler(), Pending());
		handler.Complete.Enqueue((HttpStatusCode.OK, new { outcome = "newAccount", providerName = "Discord", serviceName = "ariel", suggestedUsername = "ariel", canCreate = true }));
		handler.Complete.Enqueue((HttpStatusCode.Conflict, new { error = "Username is already taken." }));

		var cut = RenderReturn("?flow=flow-1");
		cut.WaitForAssertion(() => cut.Find("button.ext-create"), TimeSpan.FromSeconds(5));
		await cut.Find("button.ext-create").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.ext-create")).IsNotEmpty();
	}

	[Test]
	public async Task Return_ForALinkedIdentity_SignsIn_AndGoesWhereTheVisitorWasHeaded()
	{
		var withCharacter = new
		{
			accountId = "acct-1",
			username = "ariel",
			accountSessionToken = "session-1",
			mustChangePassword = false,
			role = "Player",
			permissions = Array.Empty<string>(),
			characters = new[] { new { dbrefNumber = 5, creationTime = 5L, name = "Ariel", flags = "", isActing = true } }
		};
		var handler = Seed(new ServiceSignInApiHandler(), Pending(returnUrl: "/wiki"));
		handler.Complete.Enqueue((HttpStatusCode.OK, new { outcome = "signedIn", providerName = "Discord", serviceName = "ariel", session = withCharacter }));

		var cut = RenderReturn("?flow=flow-1");
		var nav = Services.GetRequiredService<NavigationManager>();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/wiki", StringComparison.Ordinal)) throw new InvalidOperationException(nav.Uri);
		}, TimeSpan.FromSeconds(5));
		await Assert.That(handler.Completed.Single().GetProperty("createAccount").GetBoolean()).IsFalse();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "sessionStorage.removeItem" && (string)i.Arguments[0]! == PendingKey))
			.IsTrue();
	}

	[Test]
	public async Task Return_FromLinking_GoesBackToTheAccountsSignIns()
	{
		var handler = Seed(new ServiceSignInApiHandler(), Pending(linking: true));
		handler.Complete.Enqueue((HttpStatusCode.OK, new { outcome = "linked", providerName = "Discord", serviceName = "ariel" }));

		var cut = RenderReturn("?flow=flow-1");
		var nav = Services.GetRequiredService<NavigationManager>();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/account#sign-ins", StringComparison.Ordinal)) throw new InvalidOperationException(nav.Uri);
		}, TimeSpan.FromSeconds(5));
	}

	/// <summary>Only the tab that left for the service holds the binding, so another tab is told where to finish.</summary>
	[Test]
	public async Task Return_InATabThatStartedNothing_SaysToFinishInTheOtherTab()
	{
		var handler = Seed(new ServiceSignInApiHandler(), null);
		var cut = RenderReturn("?flow=flow-1");
		cut.WaitForAssertion(() => cut.Find(".ext-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".ext-error").TextContent).Contains("AuthServiceOtherTab");
		await Assert.That(handler.Completed).IsEmpty();
	}

	[Test]
	public async Task Return_WhenTheServerWasBusy_SaysSo()
	{
		Seed(new ServiceSignInApiHandler(), Pending());
		var cut = RenderReturn("?error=busy");
		cut.WaitForAssertion(() => cut.Find(".ext-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".ext-error").TextContent).Contains("AuthServiceBusy");
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in ownedHttpClients) client.Dispose();
		await base.DisposeAsync();
	}
}
