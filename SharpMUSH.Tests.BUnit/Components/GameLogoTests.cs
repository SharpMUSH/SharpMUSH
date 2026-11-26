using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The game's mark: the compact portal_logo where the menu bar draws it, the full logo (portal_logo_full, else
/// portal_logo) where a page asks for it, and the one the shell carried from the first render on.
/// </summary>
public class GameLogoTests : BunitContext
{
	/// <summary>Never answers, so what a logo draws is what the portal knew before asking.</summary>
	private sealed class Silent : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			new TaskCompletionSource<HttpResponseMessage>().Task;
	}

	private sealed class SilentFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(new Silent()) { BaseAddress = new Uri("https://localhost/") };
	}

	[TUnit.Core.Test]
	[Arguments(false, "/api/wiki-assets/a/mark.png")]
	[Arguments(true, "/api/wiki-assets/a/full.svg")]
	public async Task EachPlaceDrawsItsOwnLogo(bool full, string expected)
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true, logo: "/api/wiki-assets/a/mark.png")
		{
			FullLogo = "/api/wiki-assets/a/full.svg",
		});

		var logo = Render<GameLogo>(parameters => parameters.Add(p => p.Full, full)).Find("img");

		await Assert.That(logo.GetAttribute("src")).EndsWith(expected);
		await Assert.That(logo.ClassList.Contains("game-logo--full")).IsEqualTo(full);
	}

	[TUnit.Core.Test]
	public async Task WithNoFullLogo_TheCompactOneStandsIn()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true, logo: "/api/wiki-assets/a/mark.png"));

		var logo = Render<GameLogo>(parameters => parameters.Add(p => p.Full, true)).Find("img");

		await Assert.That(logo.GetAttribute("src")).EndsWith("/api/wiki-assets/a/mark.png");
	}

	[TUnit.Core.Test]
	public async Task TheFirstRender_IsTheLogoTheShellCarried()
	{
		var serverInfo = new ServerInfoService(new SilentFactory());
		serverInfo.Seed(new PortalBootIdentity("Ravenmoor", "/api/wiki-assets/a/mark.png", "/api/wiki-assets/a/full.svg", "", ""));
		Services.AddSingleton(serverInfo);

		var compact = Render<GameLogo>();
		var full = Render<GameLogo>(parameters => parameters.Add(p => p.Full, true));

		await Assert.That(compact.FindAll("svg").Count).IsEqualTo(0).Because("SharpMUSH's mark never shows first");
		await Assert.That(compact.Find("img").GetAttribute("src")).EndsWith("/api/wiki-assets/a/mark.png");
		await Assert.That(full.Find("img").GetAttribute("src")).EndsWith("/api/wiki-assets/a/full.svg");
	}

	[TUnit.Core.Test]
	public async Task AGameWithNoLogo_DrawsSharpMUSHs()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));

		var cut = Render<GameLogo>(parameters => parameters.Add(p => p.Full, true));

		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find("svg path").GetAttribute("fill")).IsEqualTo("var(--accent)");
	}
}
