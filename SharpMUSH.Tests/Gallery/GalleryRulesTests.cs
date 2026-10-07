using SharpMUSH.Library.API;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Gallery;

/// <summary>
/// The gallery's invariants (spec §2): one icon while any image other than the banner exists, at most one banner, and the
/// three IMAGE attributes a gallery write mirrors, so softcode and OOB payloads see the same picture
/// the portal does.
/// </summary>
public class GalleryRulesTests
{
	private static GalleryEntry Entry(string id, int order, bool icon = false, bool banner = false, string? caption = null) =>
		new(id, $"{id}.jpg", $"/api/wiki-assets/{id}/{id}.jpg", caption, order, icon, banner);

	[Test]
	public async Task Normalize_OrdersAndRenumbers()
	{
		var result = GalleryRules.Normalize([Entry("b", 7, icon: true), Entry("a", 2)]);
		await Assert.That(result.Select(e => e.AssetId).ToList()).IsEquivalentTo(new[] { "a", "b" });
		await Assert.That(result.Select(e => e.Order).ToList()).IsEquivalentTo(new[] { 0, 1 });
	}

	[Test]
	public async Task Normalize_KeepsTheFirstIconAndTheFirstBanner()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, icon: true, banner: true), Entry("b", 1, icon: true, banner: true)]);
		await Assert.That(result.Count(e => e.IsIcon)).IsEqualTo(1);
		await Assert.That(result.Count(e => e.IsBanner)).IsEqualTo(1);
		await Assert.That(result[0].IsIcon).IsTrue();
		await Assert.That(result[0].IsBanner).IsTrue();
	}

	[Test]
	public async Task Normalize_PromotesTheFirstImageToIcon_ButNeverInventsABanner()
	{
		var result = GalleryRules.Normalize([Entry("a", 0), Entry("b", 1)]);
		await Assert.That(result[0].IsIcon).IsTrue();
		await Assert.That(result.Any(e => e.IsBanner)).IsFalse()
			.Because("no banner means the profile's hue gradient, which is a choice the owner makes");
	}

	[Test]
	public async Task Normalize_PromotesTheFirstImageThatIsNotTheBanner()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, banner: true), Entry("b", 1)]);
		await Assert.That(result[0].IsIcon).IsFalse();
		await Assert.That(result[1].IsIcon).IsTrue();
	}

	[Test]
	public async Task Normalize_AGalleryOfOnlyTheBanner_HasNoAvatar()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, banner: true)]);
		await Assert.That(result[0].IsIcon).IsFalse().Because("the banner does not stand in for the avatar; the profile draws initials");
		await Assert.That(result[0].IsBanner).IsTrue();
	}

	[Test]
	public async Task Normalize_KeepsAnImageChosenAsBothBannerAndAvatar()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, icon: true, banner: true), Entry("b", 1)]);
		await Assert.That(result[0].IsIcon).IsTrue();
		await Assert.That(result[1].IsIcon).IsFalse();
	}

	[Test]
	public async Task Normalize_EmptyStaysEmpty()
		=> await Assert.That(GalleryRules.Normalize([]).Count).IsEqualTo(0);

	[Test]
	public async Task Mirror_TakesTheIconUrlTheBannerUrlAndTheIconCaption()
	{
		var mirror = GalleryRules.Mirror([Entry("a", 0, icon: true, caption: "Tomas at dusk"), Entry("b", 1, banner: true)]);
		await Assert.That(mirror.Image).IsEqualTo("/api/wiki-assets/a/a.jpg");
		await Assert.That(mirror.Banner).IsEqualTo("/api/wiki-assets/b/b.jpg");
		await Assert.That(mirror.Alt).IsEqualTo("Tomas at dusk");
	}

	[Test]
	public async Task Mirror_OfAnEmptyGallery_ClearsAllThree()
	{
		var mirror = GalleryRules.Mirror([]);
		await Assert.That(mirror.Image).IsEqualTo(string.Empty);
		await Assert.That(mirror.Banner).IsEqualTo(string.Empty);
		await Assert.That(mirror.Alt).IsEqualTo(string.Empty);
	}
}
