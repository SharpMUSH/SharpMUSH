using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The portal's shell as the game serves it (<see cref="PortalShell"/>): its title is the game's name and it carries
/// the game's logos and guide, so the first render is the game's own.
/// </summary>
public class PortalShellTests
{
	private const string Shell = "<!DOCTYPE html><html><head><title>SharpMUSH</title><link href=\"css/app.css\" rel=\"stylesheet\" /></head><body>portal</body></html>";

	private static SharpMUSHOptions Options(string name = "Ravenmoor", string logo = "", string fullLogo = "",
		string guideName = "", string guideArt = "")
	{
		var options = SharpMUSHOptions.Default();
		return options with
		{
			Net = options.Net with { MudName = name },
			Cosmetic = options.Cosmetic with { PortalLogo = logo, PortalLogoFull = fullLogo },
			Onboarding = options.Onboarding with { GuideName = guideName, GuideArt = guideArt },
		};
	}

	private static PortalBootIdentity? Carried(string html)
	{
		var start = html.IndexOf($"<script id=\"{PortalBootIdentity.ElementId}\" type=\"application/json\">", StringComparison.Ordinal);
		if (start < 0) return null;
		start = html.IndexOf('>', start) + 1;
		var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
		return JsonSerializer.Deserialize<PortalBootIdentity>(html[start..end], new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}

	[Test]
	public async Task Identity_IsTheGamesNameLogosAndGuide()
	{
		var identity = PortalShell.Identity(Options(logo: " /api/wiki-assets/a/mark.png ", guideName: " Drakie ",
			guideArt: "/api/wiki-assets/a/drakie.png"));

		await Assert.That(identity).IsEqualTo(new PortalBootIdentity("Ravenmoor", "/api/wiki-assets/a/mark.png",
			"/api/wiki-assets/a/mark.png", "Drakie", "/api/wiki-assets/a/drakie.png"));
		await Assert.That(PortalShell.Identity(Options(fullLogo: "/api/wiki-assets/a/full.svg")).LogoFull)
			.IsEqualTo("/api/wiki-assets/a/full.svg");
	}

	[Test]
	public async Task Identity_OfAGameWithNoneOfItsOwn_IsEmpty()
	{
		var identity = PortalShell.Identity(Options(name: " "));

		await Assert.That(identity).IsEqualTo(new PortalBootIdentity("SharpMUSH", "", "", "", ""));
	}

	[Test]
	public async Task Render_TitlesTheShellWithTheGameAndCarriesItsIdentity()
	{
		var identity = new PortalBootIdentity("Ravenmoor", "/m.png", "/f.svg", "Drakie", "/d.png");
		var html = PortalShell.Render(Shell, identity);

		await Assert.That(html).Contains("<title>Ravenmoor</title>");
		await Assert.That(html).DoesNotContain("<title>SharpMUSH</title>");
		await Assert.That(Carried(html)).IsEqualTo(identity);
		await Assert.That(html.IndexOf(PortalBootIdentity.ElementId, StringComparison.Ordinal))
			.IsLessThan(html.IndexOf("</head>", StringComparison.Ordinal));
	}

	[Test]
	public async Task Render_NeverLetsAValueOutOfItsElement()
	{
		var identity = new PortalBootIdentity("</title><script>alert(1)</script>", "/m.png", "", "</script><b>x", "");
		var html = PortalShell.Render(Shell, identity);

		await Assert.That(html).DoesNotContain("<script>alert(1)</script>");
		await Assert.That(html).DoesNotContain("</script><b>x");
		await Assert.That(html).Contains("<title>&lt;/title&gt;&lt;script&gt;alert(1)&lt;/script&gt;</title>");
		await Assert.That(Carried(html)).IsEqualTo(identity);
	}

	private static async Task<(WebApplication App, HttpClient Client, string Root)> StartAsync(SharpMUSHOptions? options)
	{
		var root = Directory.CreateTempSubdirectory("portal-shell-").FullName;
		var webRoot = Path.Join(root, "wwwroot");
		Directory.CreateDirectory(webRoot);
		await File.WriteAllTextAsync(Path.Join(webRoot, PortalStaticFiles.IndexFileName), Shell);

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ContentRootPath = root,
			WebRootPath = webRoot,
			EnvironmentName = Environments.Production,
		});
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddAuthorization();
		builder.Services.AddRateLimiter(_ => { });
		if (options is not null)
		{
			var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			wrapper.CurrentValue.Returns(options);
			builder.Services.AddSingleton(wrapper);
		}

		var app = builder.Build();
		app.UseRouting();
		app.UsePortalStaticFiles(null);
		app.UseAuthorization();
		app.UseRateLimiter();
		app.MapPortal(null);

		await app.StartAsync();
		return (app, app.GetTestClient(), root);
	}

	private static async Task StopAsync(WebApplication app, string root)
	{
		await app.StopAsync();
		await app.DisposeAsync();
		Directory.Delete(root, recursive: true);
	}

	[Test]
	public async Task TheFallback_ServesTheShellWithTheGamesIdentity()
	{
		var (app, client, root) = await StartAsync(Options(fullLogo: "/api/wiki-assets/a/full.svg", guideName: "Drakie"));
		try
		{
			using var response = await client.GetAsync("/wiki/main/home");
			var html = await response.Content.ReadAsStringAsync();

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
			await Assert.That(response.Headers.CacheControl?.ToString()).IsEqualTo(PortalStaticFiles.IndexCacheControl);
			await Assert.That(html).Contains("<title>Ravenmoor</title>");
			await Assert.That(Carried(html)).IsEqualTo(new PortalBootIdentity("Ravenmoor", "", "/api/wiki-assets/a/full.svg", "Drakie", ""));

			// A browser that has this shell already is told so.
			using var again = new HttpRequestMessage(HttpMethod.Get, "/wiki/main/home");
			again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
			using var revalidated = await client.SendAsync(again);
			await Assert.That(revalidated.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
		}
		finally
		{
			await StopAsync(app, root);
		}
	}

	[Test]
	public async Task TheShellFile_IsSentToTheRoot()
	{
		var (app, client, root) = await StartAsync(Options());
		try
		{
			using var response = await client.GetAsync("/index.html?x=1");

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
			await Assert.That(response.Headers.Location?.ToString()).IsEqualTo("/?x=1");
		}
		finally
		{
			await StopAsync(app, root);
		}
	}

	[Test]
	public async Task AnythingButAReadOfTheShell_IsNotFound()
	{
		var (app, client, root) = await StartAsync(Options());
		try
		{
			using var content = new StringContent("");
			using var response = await client.PostAsync("/somewhere", content);

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		}
		finally
		{
			await StopAsync(app, root);
		}
	}
}
