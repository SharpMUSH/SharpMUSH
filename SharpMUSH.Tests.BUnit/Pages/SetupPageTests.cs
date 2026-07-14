using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Services;

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
file sealed class SetupApiHandler(
    bool needsSetup, HttpStatusCode completeStatus, string? completeBody, string completeSessionToken = "test-session-token")
    : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');

        if (request.Method == HttpMethod.Get && path == "api/setup/status")
            return Json(new { needsSetup });

        if (request.Method == HttpMethod.Post && path == "api/setup/complete")
        {
            if (completeStatus == HttpStatusCode.OK)
            {
                var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var requestBody = JsonDocument.Parse(requestJson);
                var username = requestBody.RootElement.GetProperty("username").GetString() ?? "headwiz";

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
        this BunitContext ctx, bool needsSetup, HttpStatusCode completeStatus = HttpStatusCode.OK,
        string? completeBody = null, string completeSessionToken = "test-session-token")
    {
        var apiClient = new HttpClient(new SetupApiHandler(needsSetup, completeStatus, completeBody, completeSessionToken))
        {
            BaseAddress = new Uri("https://localhost:8081/")
        };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("api").Returns(apiClient);

        ctx.Services
            .AddSingleton(factory)
            .AddSingleton(sp => new AccountAuthService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
                NullLogger<AccountAuthService>.Instance));

        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return apiClient;
    }
}

/// <summary>
/// bUnit tests for the first-run setup wizard: password-confirmation validation and
/// 409-conflict error mapping ("someone else completed setup" / username taken).
/// </summary>
public class SetupPageTests : BunitContext, IAsyncDisposable
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

    [TUnit.Core.Test]
    public async Task Setup_Success_ShowsAdministratorStateInsteadOfRedirect()
    {
        ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true));

        var cut = Render<SharpMUSH.Client.Pages.Setup>();
        cut.Find("#setup-username").Input("headwiz");
        cut.Find("#setup-password").Input("password-one");
        cut.Find("#setup-confirm").Input("password-one");
        cut.Find("button.setup-submit").Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("You are the administrator"))
                throw new InvalidOperationException("success view not rendered yet");
        });

        await Assert.That(cut.Markup).Contains("You are the administrator");
        await Assert.That(cut.Markup).Contains("headwiz");

        // Auto-login after first-run setup: the claimer is signed in immediately (no separate
        // "Sign in" step), so the success copy reflects that and offers a portal button instead.
        await Assert.That(cut.Markup).Contains("You are signed in as");
        var enterPortalButton = cut.Find("button.setup-signin");
        await Assert.That(enterPortalButton.TextContent).Contains("Enter the portal");

        var accountAuth = Services.GetRequiredService<AccountAuthService>();
        await Assert.That(accountAuth.IsLoggedIn).IsTrue();
        await Assert.That(accountAuth.Username).IsEqualTo("headwiz");
        await Assert.That(accountAuth.Role).IsEqualTo("God");

        var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        enterPortalButton.Click();
        await Assert.That(nav.Uri).IsEqualTo(nav.BaseUri);
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
        cut.Find("#setup-username").Input("headwiz");
        cut.Find("#setup-password").Input("password-one");
        cut.Find("#setup-confirm").Input("password-one");
        cut.Find("button.setup-submit").Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("You are the administrator"))
                throw new InvalidOperationException("success view not rendered yet");
        });

        await Assert.That(cut.Markup).Contains("You are the administrator");
        await Assert.That(cut.Markup).Contains("Sign in to start managing your game");
        await Assert.That(cut.Markup).DoesNotContain("You are signed in as");

        var signInLink = cut.Find("a.setup-signin");
        await Assert.That(signInLink.GetAttribute("href")).IsEqualTo("/login");
        await Assert.That(signInLink.TextContent).Contains("Sign in");

        var accountAuth = Services.GetRequiredService<AccountAuthService>();
        await Assert.That(accountAuth.IsLoggedIn).IsFalse();
    }

    [TUnit.Core.Test]
    public async Task Setup_ValidatesPasswordConfirmation()
    {
        ownedHttpClients.Add(this.AddSetupTestServices(needsSetup: true));

        var cut = Render<SharpMUSH.Client.Pages.Setup>();
        cut.Find("#setup-username").Input("headwiz");
        cut.Find("#setup-password").Input("password-one");
        cut.Find("#setup-confirm").Input("password-two");
        cut.Find("button.setup-submit").Click();

        await Assert.That(cut.Find(".setup-error").TextContent).Contains("do not match");
    }

    [TUnit.Core.Test]
    public async Task Setup_Conflict_ShowsClaimedMessage()
    {
        ownedHttpClients.Add(this.AddSetupTestServices(
            needsSetup: true,
            completeStatus: HttpStatusCode.Conflict,
            completeBody: "Setup has already been completed."));

        var cut = Render<SharpMUSH.Client.Pages.Setup>();
        cut.Find("#setup-username").Input("headwiz");
        cut.Find("#setup-password").Input("password-one");
        cut.Find("#setup-confirm").Input("password-one");
        cut.Find("button.setup-submit").Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Find(".setup-error").TextContent.Contains("completed by someone else"))
                throw new InvalidOperationException("conflict error not mapped yet");
        });

        await Assert.That(cut.Find(".setup-error").TextContent).Contains("completed by someone else");
    }

    [TUnit.Core.Test]
    public async Task Setup_Conflict_UsernameTaken_ShowsFriendlyMessage()
    {
        ownedHttpClients.Add(this.AddSetupTestServices(
            needsSetup: true,
            completeStatus: HttpStatusCode.Conflict,
            completeBody: "Username is already taken."));

        var cut = Render<SharpMUSH.Client.Pages.Setup>();
        cut.Find("#setup-username").Input("headwiz");
        cut.Find("#setup-password").Input("password-one");
        cut.Find("#setup-confirm").Input("password-one");
        cut.Find("button.setup-submit").Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Find(".setup-error").TextContent.Contains("already taken"))
                throw new InvalidOperationException("conflict error not mapped yet");
        });

        await Assert.That(cut.Find(".setup-error").TextContent).Contains("already taken");
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
