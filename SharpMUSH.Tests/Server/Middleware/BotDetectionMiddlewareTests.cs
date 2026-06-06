using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server.Middleware;

namespace SharpMUSH.Tests.Server.Middleware;

/// <summary>
/// Unit tests for <see cref="BotDetectionMiddleware"/>.
/// Tests the static <see cref="BotDetectionMiddleware.IsBot"/> helper and
/// verifies end-to-end HTTP behaviour via in-process TestServer.
/// </summary>
public class BotDetectionMiddlewareTests
{
    // ── IsBot static helper ──────────────────────────────────────────────────

    [Test]
    public async Task IsBot_Googlebot_ReturnsTrue()
    {
        await Assert.That(
            BotDetectionMiddleware.IsBot("Mozilla/5.0 (compatible; Googlebot/2.1)", QueryCollection.Empty)
        ).IsTrue();
    }

    [Test]
    public async Task IsBot_Bingbot_ReturnsTrue()
    {
        await Assert.That(
            BotDetectionMiddleware.IsBot("Mozilla/5.0 (compatible; bingbot/2.0)", QueryCollection.Empty)
        ).IsTrue();
    }

    [Test]
    public async Task IsBot_RegularBrowser_ReturnsFalse()
    {
        await Assert.That(
            BotDetectionMiddleware.IsBot(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
                QueryCollection.Empty
            )
        ).IsFalse();
    }

    [Test]
    public async Task IsBot_EmptyUserAgent_ReturnsFalse()
    {
        await Assert.That(BotDetectionMiddleware.IsBot(string.Empty, QueryCollection.Empty)).IsFalse();
    }

    [Test]
    public async Task IsBot_EscapedFragmentQueryParam_ReturnsTrue()
    {
        var query = new QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                { "_escaped_fragment_", string.Empty }
            }
        );
        await Assert.That(BotDetectionMiddleware.IsBot("NormalBrowser/1.0", query)).IsTrue();
    }

    [Test]
    public async Task IsBot_CaseInsensitiveMatch_Twitterbot_ReturnsTrue()
    {
        await Assert.That(
            BotDetectionMiddleware.IsBot("TWITTERBOT/1.0", QueryCollection.Empty)
        ).IsTrue();
    }

    // ── HTTP behaviour via TestServer ────────────────────────────────────────

    /// <summary>Builds a minimal in-process app with only BotDetectionMiddleware registered.</summary>
    private static async Task<WebApplication> BuildAndStartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(l => l.ClearProviders());
        builder.Services.AddRouting();

        var app = builder.Build();
        app.UseMiddleware<BotDetectionMiddleware>();
        app.Run(ctx =>
        {
            var isBot = ctx.Items[BotDetectionMiddleware.BotFlagKey] is true;
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync(isBot ? "bot" : "human");
        });

        await app.StartAsync();
        return app;
    }

    [Test]
    public async Task Request_BotOnPublicRoute_SetsBotFlagAndReturns200()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Googlebot/2.1");

        var response = await client.GetAsync("/wiki/Magic_System");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        await Assert.That(body).IsEqualTo("bot");
    }

    [Test]
    public async Task Request_BotOnAuthenticatedRoute_Returns403()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Googlebot/2.1");

        var response = await client.GetAsync("/mail");

        await Assert.That((int)response.StatusCode).IsEqualTo(403);
    }

    [Test]
    public async Task Request_BotOnSettingsRoute_Returns403()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        client.DefaultRequestHeaders.Add("User-Agent", "bingbot/2.0");

        var response = await client.GetAsync("/settings");

        await Assert.That((int)response.StatusCode).IsEqualTo(403);
    }

    [Test]
    public async Task Request_BotOnAdminRoute_Returns403()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Googlebot/2.1");

        var response = await client.GetAsync("/admin/players");

        await Assert.That((int)response.StatusCode).IsEqualTo(403);
    }

    [Test]
    public async Task Request_HumanOnAnyRoute_SetsBotFlagFalseAndPassesThrough()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await client.GetAsync("/wiki/Magic_System");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        await Assert.That(body).IsEqualTo("human");
    }

    [Test]
    public async Task Request_HumanOnAuthenticatedRoute_PassesThrough()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await client.GetAsync("/mail");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        await Assert.That(body).IsEqualTo("human");
    }
}
