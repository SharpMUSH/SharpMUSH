using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// HttpMessageHandler faking the setup-wizard API surface: GET api/setup/status and
/// POST api/setup/complete. The complete response is configurable per test so tests can
/// exercise the happy path, validation-only path (never reaches the handler), and the
/// 409-conflict path (already completed / username taken).
/// </summary>
file sealed class SetupApiHandler(bool needsSetup, HttpStatusCode completeStatus, string? completeBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');

        if (request.Method == HttpMethod.Get && path == "api/setup/status")
            return Task.FromResult(Json(new { needsSetup }));

        if (request.Method == HttpMethod.Post && path == "api/setup/complete")
        {
            var response = new HttpResponseMessage(completeStatus);
            if (completeBody is not null)
                response.Content = new StringContent(completeBody);
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

/// <summary>Helper to register a real AccountAuthService backed by <see cref="SetupApiHandler"/>.</summary>
file static class SetupTestServices
{
    public static void AddSetupTestServices(
        this BunitContext ctx, bool needsSetup, HttpStatusCode completeStatus = HttpStatusCode.OK, string? completeBody = null)
    {
        var apiClient = new HttpClient(new SetupApiHandler(needsSetup, completeStatus, completeBody))
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
    }
}

/// <summary>
/// bUnit tests for the first-run setup wizard: password-confirmation validation and
/// 409-conflict error mapping ("someone else completed setup" / username taken).
/// </summary>
public class SetupPageTests : BunitContext
{
    [TUnit.Core.Test]
    public async Task Setup_ValidatesPasswordConfirmation()
    {
        this.AddSetupTestServices(needsSetup: true);

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
        this.AddSetupTestServices(
            needsSetup: true,
            completeStatus: HttpStatusCode.Conflict,
            completeBody: "Setup has already been completed.");

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
        this.AddSetupTestServices(
            needsSetup: true,
            completeStatus: HttpStatusCode.Conflict,
            completeBody: "Username is already taken.");

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
}
