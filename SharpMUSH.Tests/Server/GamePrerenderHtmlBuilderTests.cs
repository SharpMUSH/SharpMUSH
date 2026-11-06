using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// A link to the game's Home or its public lists, pasted into Discord, shows the game: its name, tagline,
/// description and banner, never "SharpMUSH".
/// </summary>
public class GamePrerenderHtmlBuilderTests
{
	private const string Base = "https://game.example";

	private static SharpMUSHOptions Options(string banner = "", string tagline = "", string description = "",
		string introduction = "", string logo = "")
	{
		var options = SharpMUSHOptions.Default();
		return options with
		{
			Net = options.Net with { MudName = "Gaslight & Rain" },
			Cosmetic = options.Cosmetic with
			{
				PortalBanner = banner,
				PortalTagline = tagline,
				PortalDescription = description,
				PortalLogo = logo
			},
			Onboarding = options.Onboarding with { Introduction = introduction }
		};
	}

	[Test]
	public async Task AConfiguredGame_IsItsNameTaglineDescriptionAndBanner()
	{
		var html = GamePrerenderHtmlBuilder.GenerateGamePrerenderHtml(Options("/api/wiki-assets/a/banner.png",
			"Intrigue in a city that never sleeps", "Players run the guilds."), Base, "/");

		await Assert.That(html).Contains("<title>Gaslight &amp; Rain: Intrigue in a city that never sleeps</title>");
		await Assert.That(html).Contains("<meta property=\"og:site_name\" content=\"Gaslight &amp; Rain\" />");
		await Assert.That(html).Contains("<meta property=\"og:description\" content=\"Players run the guilds.\" />");
		await Assert.That(html).Contains("<meta property=\"og:image\" content=\"https://game.example/api/wiki-assets/a/banner.png\" />")
			.Because("a crawler cannot resolve a relative address");
		await Assert.That(html).Contains("<meta name=\"twitter:card\" content=\"summary_large_image\" />");
		await Assert.That(html).Contains("<meta property=\"og:url\" content=\"https://game.example/\" />");
	}

	[Test]
	public async Task AFreshGame_ShowsItsNameAndTheSharpMUSHLogoAsAPicture()
	{
		var html = GamePrerenderHtmlBuilder.GenerateGamePrerenderHtml(Options(), Base, "/characters");

		await Assert.That(html).Contains("<title>Gaslight &amp; Rain</title>");
		await Assert.That(html).Contains($"<meta property=\"og:image\" content=\"https://game.example{GamePrerenderHtmlBuilder.DefaultImage}\" />");
		await Assert.That(html).Contains("<meta name=\"twitter:card\" content=\"summary\" />");
		await Assert.That(html).DoesNotContain("og:description").Because("there is nothing to describe the game with");
	}

	[Test]
	public async Task WithoutADescription_TheSetupIntroductionDescribesTheGame()
	{
		var html = GamePrerenderHtmlBuilder.GenerateGamePrerenderHtml(Options(introduction: "Welcome, traveller."), Base, "/");

		await Assert.That(html).Contains("<meta property=\"og:description\" content=\"Welcome, traveller.\" />");
	}

	[Test]
	public async Task WithoutABanner_TheGamesLogoIsThePicture()
	{
		var html = GamePrerenderHtmlBuilder.GenerateGamePrerenderHtml(Options(logo: "https://cdn.example/crest.png"), Base, "/");

		await Assert.That(html).Contains("<meta property=\"og:image\" content=\"https://cdn.example/crest.png\" />");
	}

	[Test]
	[Arguments("/", true)]
	[Arguments("/characters", true)]
	[Arguments("/scenes/", true)]
	[Arguments("/scenes/42", true)]
	[Arguments("/LOGIN", true)]
	[Arguments("/wiki/main/home", false)]
	[Arguments("/help/look", false)]
	[Arguments("/character/alice", false)]
	[Arguments("/admin", false)]
	[Arguments("/css/custom.css", false)]
	public async Task OnlyHomeAndThePublicListsGetTheGamesCard(string path, bool expected)
	{
		await Assert.That(GamePrerenderHtmlBuilder.IsGamePath(path)).IsEqualTo(expected);
	}

	[Test]
	public async Task ACrawlerAskingForHome_GetsTheGamesCard()
	{
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(Options(tagline: "Intrigue"));
		var services = new ServiceCollection()
			.AddSingleton(wrapper)
			.AddSingleton(Substitute.For<SharpMUSH.Library.Services.Interfaces.IWikiService>())
			.AddSingleton(Substitute.For<IWikiLocalizationService>())
			.BuildServiceProvider();
		var cache = Substitute.For<IPrerenderCacheService>();
		var passedOn = false;
		var middleware = new BotPrerenderMiddleware(_ =>
		{
			passedOn = true;
			return Task.CompletedTask;
		}, services.GetRequiredService<IServiceScopeFactory>(), cache, NullLogger<BotPrerenderMiddleware>.Instance);

		var context = new DefaultHttpContext();
		context.Items[BotDetectionMiddleware.BotFlagKey] = true;
		context.Request.Scheme = "https";
		context.Request.Host = new HostString("game.example");
		context.Request.Path = "/";
		context.Response.Body = new MemoryStream();
		await middleware.InvokeAsync(context);

		context.Response.Body.Position = 0;
		var html = await new StreamReader(context.Response.Body).ReadToEndAsync();
		await Assert.That(passedOn).IsFalse();
		await Assert.That(html).Contains("<title>Gaslight &amp; Rain: Intrigue</title>");
		cache.DidNotReceive().Set(Arg.Any<string>(), Arg.Any<string>());
	}
}
