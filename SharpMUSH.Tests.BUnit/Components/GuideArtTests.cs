using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>The guide's picture: the game's own when it set one, else Sharpie, and none when a player hid artwork.</summary>
public class GuideArtTests
{
	private static OnboardingPresentation Presentation(string guideArt) =>
		new("game", "", "Fantasy", "None", "minimalist", "", null, null, null) { GuideArt = guideArt };

	[TUnit.Core.Test]
	public async Task TheGamesOwnPicture_IsTheGuide()
	{
		var art = GuideArt.Resolve(Presentation(" /api/wiki-assets/a/drakie.png "), GuideImagery.Full, topic: "player-place");

		await Assert.That(art!.Url).IsEqualTo("/api/wiki-assets/a/drakie.png");
		await Assert.That(GuideArt.IsOwn(art)).IsTrue();
	}

	[TUnit.Core.Test]
	[Arguments("")]
	[Arguments("javascript:alert(1)")]
	[Arguments("http://example.com/drakie.png")]
	public async Task WithoutAPictureThePortalDraws_SharpieIsTheGuide(string guideArt)
	{
		var art = GuideArt.Resolve(Presentation(guideArt), GuideImagery.Full, SharpieArtMoment.Welcome);

		await Assert.That(art).IsEqualTo(SharpieArtCatalog.Resolve("minimalist", "Fantasy", "None", "", GuideImagery.Full, SharpieArtMoment.Welcome));
	}

	[TUnit.Core.Test]
	public async Task AnyPicture_StaysHiddenForAPlayerWhoHidArtwork()
	{
		await Assert.That(GuideArt.Resolve(Presentation("/api/wiki-assets/a/drakie.png"), GuideImagery.None)).IsNull();
	}
}
