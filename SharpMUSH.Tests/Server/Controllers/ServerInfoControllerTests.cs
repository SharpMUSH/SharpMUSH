using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Server;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <see cref="ServerInfoController"/>: the anonymous server-info endpoint must
/// say whether a visitor can play as a guest (<see cref="IGuestAvailability"/>) so the portal can decide
/// whether to offer a "play as guest" entry,
/// <c>Net.MudName</c> so the portal can brand the shell with the real game name, and the optional
/// applications the game has on so the portal links only to those.
/// </summary>
public class ServerInfoControllerTests
{
	private static SharpMUSHOptions DefaultOptions()
		=> new OptionsService(Substitute.For<ISharpDatabase>(), []).Create(string.Empty);

	private static ServerInfoController MakeController(bool guestsEnabled, string mudName = "SharpMUSH",
		IReadOnlyList<string>? features = null, string logo = "", string favicon = "")
	{
		var options = DefaultOptions();
		options = options with
		{
			Net = options.Net with { Guests = guestsEnabled, MudName = mudName },
			Cosmetic = options.Cosmetic with { PortalLogo = logo, PortalFavicon = favicon }
		};

		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);

		var reader = Substitute.For<IGameFeatureReader>();
		reader.EnabledAsync().Returns(features ?? []);

		var environment = Substitute.For<IHostEnvironment>();
		environment.ContentRootPath.Returns(Path.GetTempPath());

		// GuestAvailabilityTests covers how the answer is reached; here it is only passed through.
		var guests = Substitute.For<IGuestAvailability>();
		guests.CanLogInAsync(Arg.Any<CancellationToken>()).Returns(guestsEnabled);

		return new ServerInfoController(wrapper, reader, PortalBuild.For(environment), guests);
	}

	[Test]
	public async Task Get_ReportsGuestsEnabled_WhenAGuestCanLogIn()
	{
		var result = await MakeController(guestsEnabled: true).Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.GuestsEnabled).IsTrue();
	}

	[Test]
	public async Task Get_ReportsGuestsDisabled_WhenNoGuestCanLogIn()
	{
		var result = await MakeController(guestsEnabled: false).Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.GuestsEnabled).IsFalse();
	}

	[Test]
	public async Task Get_ReportsConfiguredMudName()
	{
		var result = await MakeController(guestsEnabled: true, mudName: "My Grand Game").Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.MudName).IsEqualTo("My Grand Game");
	}

	[Test]
	public async Task Get_ReportsTheApplicationsTheGameHasOn()
	{
		var result = await MakeController(guestsEnabled: true, features: [GameFeatures.WikiReader]).Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.Features).IsEquivalentTo([GameFeatures.WikiReader]);
	}

	[Test]
	public async Task Get_ReportsTheBuildItServes()
	{
		var result = await MakeController(guestsEnabled: true).Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.BuildId).IsNotEmpty();
	}

	[Test]
	public async Task Get_ReportsTheConfiguredLogo()
	{
		var result = await MakeController(guestsEnabled: true, logo: "/api/wiki-assets/a/logo.png").Get();

		var response = (ServerInfoController.ServerInfoResponse)((OkObjectResult)result).Value!;
		await Assert.That(response.Logo).IsEqualTo("/api/wiki-assets/a/logo.png");
	}

	[Test]
	[Arguments("", "", "/assets/Logo.svg")]
	[Arguments("/api/wiki-assets/a/logo.png", "", "/api/wiki-assets/a/logo.png")]
	[Arguments("/api/wiki-assets/a/logo.png", "https://example.com/icon.png", "https://example.com/icon.png")]
	public async Task Favicon_RedirectsToTheFaviconThenTheLogoThenTheSharpMUSHLogo(string logo, string favicon, string expected)
	{
		var controller = MakeController(guestsEnabled: true, logo: logo, favicon: favicon);
		controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

		var result = (RedirectResult)controller.Favicon();

		await Assert.That(result.Url).IsEqualTo(expected);
	}
}
