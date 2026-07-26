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
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/Wiki/Page/edit")).IsEqualTo("/wiki/Page/edit");
    }

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

    // --- Character biography aliasing -------------------------------------------------
    // /wiki/character/general/{slug} is the storage route; /character/{slug} is the one the
    // portal serves. The middleware aliases the former to the latter so bookmarks and external
    // links land on the canonical page.

    [Test]
    public async Task BuildCanonical_CharacterViewRoute_AliasedToProfile()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/character/general/mercutio"))
            .IsEqualTo("/character/mercutio");
    }

    [Test]
    public async Task BuildCanonical_CharacterViewRouteWithSpaces_AliasedAndSlugified()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/character/general/Mannaz Byron"))
            .IsEqualTo("/character/mannaz_byron");
    }

    /// <summary>
    /// History, diff and edit have no equivalent under /character, so they stay on the wiki
    /// routes. This is also what stops the profile page's own history link from bouncing.
    /// </summary>
    [Test]
    [Arguments("history")]
    [Arguments("diff")]
    [Arguments("edit")]
    public async Task BuildCanonical_CharacterSubRoutes_NotAliased(string subRoute)
    {
        var path = $"/wiki/character/general/mercutio/{subRoute}";

        await Assert.That(CanonicalUrlMiddleware.BuildCanonical(path)).IsEqualTo(path);
    }

    /// <summary>
    /// /character/{slug} carries no category segment, so a character page filed elsewhere
    /// cannot round-trip through it and keeps its wiki path.
    /// </summary>
    [Test]
    public async Task BuildCanonical_CharacterPageInOtherCategory_NotAliased()
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical("/wiki/character/npcs/mercutio"))
            .IsEqualTo("/wiki/character/npcs/mercutio");
    }

    [Test]
    [Arguments("/wiki/main/general/mercutio")]
    [Arguments("/wiki/help/general/markdown_guide")]
    public async Task BuildCanonical_OtherNamespaces_NotAliased(string path)
    {
        await Assert.That(CanonicalUrlMiddleware.BuildCanonical(path)).IsEqualTo(path);
    }

    /// <summary>The alias must not loop: the target is already canonical.</summary>
    [Test]
    public async Task BuildCanonical_ProfileAlias_IsAFixedPoint()
    {
        var once = CanonicalUrlMiddleware.BuildCanonical("/wiki/character/general/mercutio");

        await Assert.That(CanonicalUrlMiddleware.BuildCanonical(once)).IsEqualTo(once);
    }

    [Test]
    public async Task Request_CharacterWikiRoute_Returns301ToProfile()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/wiki/character/general/mercutio");

        await Assert.That((int)response.StatusCode).IsEqualTo(301);
        await Assert.That(response.Headers.Location?.ToString()).IsEqualTo("/character/mercutio");
    }

    [Test]
    public async Task Request_CharacterHistoryRoute_IsNotRedirected()
    {
        await using var app = await BuildAndStartAsync();
        using var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var response = await client.GetAsync("/wiki/character/general/mercutio/history");

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
    }
}
