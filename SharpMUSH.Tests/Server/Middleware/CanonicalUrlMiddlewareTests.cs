using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server.Middleware;

namespace SharpMUSH.Tests.Server.Middleware;

/// <summary>
/// Unit tests for <see cref="CanonicalUrlMiddleware"/>.
/// Uses in-process TestServer — no Docker containers required.
/// </summary>
public class CanonicalUrlMiddlewareTests
{
    // ── BuildCanonical static helper ────────────────────────────────────────

    [Test]
    public async Task BuildCanonical_RootPath_Unchanged()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/")).IsEqualTo("/");
    }

    [Test]
    public async Task BuildCanonical_TrailingSlash_Stripped()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/")).IsEqualTo("/wiki");
    }

    [Test]
    public async Task BuildCanonical_SpaceInSegment_ReplacedWithUnderscore()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/Page Name")).IsEqualTo("/wiki/Page_Name");
    }

    [Test]
    public async Task BuildCanonical_PercentEncodedSpace_ReplacedWithUnderscore()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/Page%20Name")).IsEqualTo("/wiki/Page_Name");
    }

    [Test]
    public async Task BuildCanonical_UppercaseFirstSegment_LowercasedToWiki()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/Wiki/SomePage")).IsEqualTo("/wiki/SomePage");
    }

    [Test]
    public async Task BuildCanonical_UppercaseCharacter_LowercasedToCharacter()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/Character/Gandalf")).IsEqualTo("/character/Gandalf");
    }

    [Test]
    public async Task BuildCanonical_AlreadyCanonical_Unchanged()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/Magic_System")).IsEqualTo("/wiki/Magic_System");
    }

    [Test]
    public async Task BuildCanonical_DeepPath_OnlyFirstSegmentLowercased()
    {
        // The second and deeper segments preserve their case
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/Wiki/Page/edit")).IsEqualTo("/wiki/Page/edit");
    }

    // ── HTTP behaviour via TestServer ────────────────────────────────────────

    /// <summary>Builds a minimal in-process app with only CanonicalUrlMiddleware registered.</summary>
    private static async Task<WebApplication> BuildAndStartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(l => l.ClearProviders());
        builder.Services.AddRouting();

        var app = builder.Build();
        app.UseMiddleware<CanonicalUrlMiddleware>();
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("ok");
        });

        await app.StartAsync();
        return app;
    }

    [Test]
    public async Task Request_WithSpaceInPath_Returns301ToUnderscoredPath()
    {
        await using var app = await BuildAndStartAsync();
        // TestServer handler does NOT follow redirects — returns raw response
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/wiki/Magic%20System");

        await Assert.That((int)response.StatusCode).IsEqualTo(301);
        await Assert.That(response.Headers.Location?.ToString()).IsEqualTo("/wiki/Magic_System");
    }

    [Test]
    public async Task Request_WithTrailingSlash_Returns301WithoutSlash()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/character/Gandalf/");

        await Assert.That((int)response.StatusCode).IsEqualTo(301);
        await Assert.That(response.Headers.Location?.ToString()).IsEqualTo("/character/Gandalf");
    }

    [Test]
    public async Task Request_ApiRoute_PassesThrough_NoRedirect()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/api/wiki/some-page");

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task Request_StaticFileWithExtension_PassesThrough_NoRedirect()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/css/app.css");

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task Request_CanonicalPath_Returns200()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/wiki/Magic_System");

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task Request_UppercasePrefixPath_Returns301WithLowercasePrefix()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/Wiki/Magic_System");

        await Assert.That((int)response.StatusCode).IsEqualTo(301);
        await Assert.That(response.Headers.Location?.ToString()).IsEqualTo("/wiki/Magic_System");
    }

    [Test]
    public async Task Request_QueryStringPreservedOnRedirect()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/wiki/Page%20Name?search=foo");

        await Assert.That((int)response.StatusCode).IsEqualTo(301);
        await Assert.That(response.Headers.Location?.ToString()).IsEqualTo("/wiki/Page_Name?search=foo");
    }
}
