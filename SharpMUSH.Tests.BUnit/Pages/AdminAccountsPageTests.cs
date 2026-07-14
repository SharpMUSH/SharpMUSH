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
    public HttpStatusCode ListStatusCode { get; set; } = HttpStatusCode.OK;
    public string? ListErrorContent { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');

        if (request.Method == HttpMethod.Get && path == "api/admin/accounts")
        {
            if (ListStatusCode != HttpStatusCode.OK)
            {
                // Returned to the caller (the HttpClient pipeline / AdminAccountsService), which
                // owns and disposes it — must not be disposed here. Built in one expression
                // (rather than a local `var response` mutated afterwards) so CodeQL's disposal
                // analysis doesn't flag a local it was never meant to dispose.
                return Task.FromResult(new HttpResponseMessage(ListStatusCode)
                {
                    Content = ListErrorContent != null ? new StringContent(ListErrorContent) : null
                });
            }

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
    /// <summary>
    /// Wires up the substitute <see cref="IHttpClientFactory"/> and returns the <see cref="HttpClient"/>
    /// it hands out, so the caller can take ownership of disposing it (registering the instance
    /// itself as a DI singleton does not get it disposed: the container only auto-disposes
    /// services it resolves through a factory call site, and nothing here resolves a plain
    /// <see cref="HttpClient"/> from <c>ctx.Services</c> — everything goes through the factory
    /// substitute instead). Some tests call this twice (default handler in the constructor, then
    /// a per-test handler) so each call must return its own client rather than the container
    /// silently owning only the last one.
    /// </summary>
    public static HttpClient AddAdminAccountsTestServices(this BunitContext ctx, AdminAccountsApiHandler? handler = null)
    {
        handler ??= new AdminAccountsApiHandler();
        var apiClient = new HttpClient(handler)
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
        return apiClient;
    }
}

/// <summary>
/// bUnit tests for the /admin/accounts portal page: row rendering for authorized wizard-role users.
/// </summary>
public class AdminAccountsPageTests : BunitContext, IAsyncDisposable
{
    private readonly List<HttpClient> ownedHttpClients = [];
    private BunitAuthorizationContext Auth { get; }

    public AdminAccountsPageTests()
    {
        Auth = this.AddAuthorization();
        ownedHttpClients.Add(this.AddAdminAccountsTestServices());
    }

    [TUnit.Core.Test]
    public async Task RendersAccountRows_ForAuthorizedUser()
    {
        Auth.SetAuthorized("headwiz");
        Auth.SetRoles("Wizard");

        var cut = Render<SharpMUSH.Client.Pages.Admin.AdminAccounts>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("headwiz-target") || !cut.Markup.Contains("DISABLED"))
                throw new InvalidOperationException("account rows not rendered yet");
        });

        await Assert.That(cut.Markup).Contains("headwiz-target");
        await Assert.That(cut.Markup).Contains("DISABLED");
    }

    [TUnit.Core.Test]
    public async Task RendersWithoutCrashing_WhenListReturns401()
    {
        Auth.SetAuthorized("headwiz");
        Auth.SetRoles("Wizard");

        var handler = new AdminAccountsApiHandler
        {
            ListStatusCode = HttpStatusCode.Unauthorized,
            ListErrorContent = "Session expired"
        };
        ownedHttpClients.Add(this.AddAdminAccountsTestServices(handler));

        // This should not throw; the page should render with error handling
        var cut = Render<SharpMUSH.Client.Pages.Admin.AdminAccounts>();

        // Wait for initial load to complete
        await Task.Delay(500);

        // Verify no accounts are shown (empty rows, since API returned 401)
        await Assert.That(cut.Markup).DoesNotContain("headwiz-target");
        await Assert.That(cut.Markup).DoesNotContain("banned-account");
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
