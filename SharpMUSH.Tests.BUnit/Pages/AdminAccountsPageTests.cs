using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// HttpMessageHandler faking the admin-accounts API surface: GET api/admin/accounts (with
/// optional ?search=), and the per-account POST/DELETE mutation routes. The fixed row set is
/// returned regardless of query so tests can assert on rendered content deterministically.
/// </summary>
file sealed class AdminAccountsApiHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');

        if (request.Method == HttpMethod.Get && path == "api/admin/accounts")
        {
            var rows = new object[]
            {
                new
                {
                    Id = "1",
                    Username = "headwiz-target",
                    Email = (string?)"headwiz@example.com",
                    IsDisabled = false,
                    MustChangePassword = false,
                    Characters = new[] { new { DbrefNumber = 1, Name = "Headwiz" } },
                },
                new
                {
                    Id = "2",
                    Username = "banned-account",
                    Email = (string?)null,
                    IsDisabled = true,
                    MustChangePassword = false,
                    Characters = Array.Empty<object>(),
                },
            };
            return Task.FromResult(Json(rows));
        }

        if (request.Method == HttpMethod.Post &&
            (path.EndsWith("/reset-password") || path.EndsWith("/disable") || path.EndsWith("/enable")))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        if (request.Method == HttpMethod.Delete && path.Contains("/characters/"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

/// <summary>Helper to register a real AdminAccountsService backed by <see cref="AdminAccountsApiHandler"/>.</summary>
file static class AdminAccountsTestServices
{
    public static void AddAdminAccountsTestServices(this BunitContext ctx)
    {
        var apiClient = new HttpClient(new AdminAccountsApiHandler())
        {
            BaseAddress = new Uri("https://localhost:8081/")
        };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("api").Returns(apiClient);

        ctx.Services
            .AddMudServices()
            .AddSingleton(factory)
            .AddSingleton(sp => new AccountAuthService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
                NullLogger<AccountAuthService>.Instance))
            .AddSingleton(sp => new AdminAccountsService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<AccountAuthService>()));

        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }
}

/// <summary>
/// bUnit tests for the /admin/accounts portal page: row rendering for authorized wizard-role users.
/// </summary>
public class AdminAccountsPageTests : BunitContext
{
    private BunitAuthorizationContext Auth { get; }

    public AdminAccountsPageTests()
    {
        Auth = this.AddAuthorization();
        this.AddAdminAccountsTestServices();
    }

    [TUnit.Core.Test]
    public async Task RendersAccountRows_ForAuthorizedUser()
    {
        Auth.SetAuthorized("headwiz");
        Auth.SetPolicies("players.moderate");

        var cut = Render<SharpMUSH.Client.Pages.Admin.AdminAccounts>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("headwiz-target") || !cut.Markup.Contains("DISABLED"))
                throw new InvalidOperationException("account rows not rendered yet");
        });

        await Assert.That(cut.Markup).Contains("headwiz-target");
        await Assert.That(cut.Markup).Contains("DISABLED");
    }
}
