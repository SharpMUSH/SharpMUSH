using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Home's hero without a banner shows the welcome picture for the genre the game named in setup, and none for a
/// game that named no genre, has a banner of its own or turned portal_sharpie off; portal_side_art takes its place.
/// </summary>
public class GameHeroTests : BunitContext
{
	private static OnboardingPresentation Onboarding(string genre, string subgenre) =>
		new("game", "", genre, subgenre, "minimalist", "", null, null, null);

	private IRenderedComponent<GameHero> RenderHero(StubServerInfoService serverInfo)
	{
		Services
			.AddMudServices()
			.AddSingleton<ServerInfoService>(serverInfo)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		return Render<GameHero>();
	}

	[TUnit.Core.Test]
	public async Task NoBanner_ShowsTheGenresWelcomePicture()
	{
		var cut = RenderHero(new StubServerInfoService(guestsEnabled: true, onboarding: Onboarding("Fantasy", "None")));

		var art = cut.Find(".game-hero-art");
		await Assert.That(art.GetAttribute("src")).EndsWith(GameHero.GenreArtFor(Onboarding("Fantasy", "None"))!.Url);
		await Assert.That(art.GetAttribute("src")).Contains("fantasy");
		await Assert.That(art.GetAttribute("alt")).IsEqualTo(string.Empty);
	}

	[TUnit.Core.Test]
	public async Task ABanner_TakesThePicturesPlace()
	{
		var serverInfo = new StubServerInfoService(guestsEnabled: true, onboarding: Onboarding("Fantasy", "None"))
		{
			Identity = ("/api/wiki-assets/a/banner.png", null, null),
		};
		var cut = RenderHero(serverInfo);

		await Assert.That(cut.FindAll(".game-hero-img").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".game-hero-art").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task AGameWithNoGenre_HasNoPicture()
	{
		await Assert.That(GameHero.GenreArtFor(Onboarding("", ""))).IsNull();
		await Assert.That(GameHero.GenreArtFor(Onboarding("None", "None"))).IsNull();
		await Assert.That(GameHero.GenreArtFor(null)).IsNull();

		var cut = RenderHero(new StubServerInfoService(guestsEnabled: true));
		await Assert.That(cut.FindAll(".game-hero-art").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task ASideArtOfTheGamesOwn_TakesSharpiesPlace()
	{
		var serverInfo = new StubServerInfoService(guestsEnabled: true, onboarding: Onboarding("Fantasy", "None"))
		{
			Side = ("/api/wiki-assets/a/mascot.png", true),
		};
		var cut = RenderHero(serverInfo);

		var art = cut.Find(".game-hero-art");
		await Assert.That(art.ClassList.Contains("game-hero-art--own")).IsTrue();
		await Assert.That(art.GetAttribute("src")).EndsWith("/api/wiki-assets/a/mascot.png");
	}

	[TUnit.Core.Test]
	public async Task ASideArtOfTheGamesOwn_StaysOverABanner()
	{
		var serverInfo = new StubServerInfoService(guestsEnabled: true)
		{
			Identity = ("/api/wiki-assets/a/banner.png", null, null),
			Side = ("/api/wiki-assets/a/mascot.png", true),
		};
		var cut = RenderHero(serverInfo);

		await Assert.That(cut.FindAll(".game-hero-img").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".game-hero-art--own").Count).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task SharpieOff_LeavesTheSideEmpty()
	{
		var serverInfo = new StubServerInfoService(guestsEnabled: true, onboarding: Onboarding("Fantasy", "None"))
		{
			Side = (null, false),
		};
		var cut = RenderHero(serverInfo);

		await Assert.That(cut.FindAll(".game-hero-art").Count).IsEqualTo(0);
	}
}
