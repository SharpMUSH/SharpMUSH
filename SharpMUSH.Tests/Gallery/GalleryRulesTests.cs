using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Gallery;

/// <summary>
/// The gallery's invariants (spec §2): one icon while any image other than the banner exists, at most one banner, and the
/// three IMAGE attributes a gallery write mirrors, so softcode and OOB payloads see the same picture
/// the portal does.
/// </summary>
public class GalleryRulesTests
{
	private static GalleryEntry Entry(string id, int order, bool icon = false, bool banner = false, string? caption = null, string? warning = null) =>
		new(id, $"{id}.jpg", $"/api/wiki-assets/{id}/{id}.jpg", caption, order, icon, banner, Warning: warning);

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

	[Test]
	public async Task Normalize_AWarnedImage_IsNeitherIconNorBanner()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, icon: true, banner: true, warning: "blood"), Entry("b", 1)]);
		await Assert.That(result[0].IsIcon).IsFalse().Because("the avatar shows everywhere without a click");
		await Assert.That(result[0].IsBanner).IsFalse();
		await Assert.That(result[1].IsIcon).IsTrue().Because("the first image without a warning becomes the avatar");
	}

	[Test]
	public async Task Normalize_AGalleryOfOnlyWarnedImages_HasNoAvatar()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, warning: "blood")]);
		await Assert.That(result[0].IsIcon).IsFalse();
		await Assert.That(GalleryRules.Mirror(result).Image).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Clean_TrimsAndLowerCasesTags_EachOnce_AndBlankIsNull()
	{
		var cleaned = GalleryRules.Clean(Entry("a", 0, caption: "  At the docks ", warning: "   ") with { Tags = [" Night", "night", "", "Docks "] })
			.Expect<GalleryEntry>();
		await Assert.That(cleaned.Caption).IsEqualTo("At the docks");
		await Assert.That(cleaned.Warning).IsNull();
		await Assert.That(cleaned.Tags!).IsEquivalentTo(new[] { "night", "docks" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Clean_NoTags_IsNull()
		=> await Assert.That(GalleryRules.Clean(Entry("a", 0) with { Tags = ["  "] }).Expect<GalleryEntry>().Tags).IsNull();

	[Test]
	[Arguments(GalleryRules.MaxCaptionLength + 1, 0, 1, 0, "caption")]
	[Arguments(0, GalleryRules.MaxWarningLength + 1, 1, 0, "warning")]
	[Arguments(0, 0, GalleryRules.MaxTags + 1, 3, "at most")]
	[Arguments(0, 0, 1, GalleryRules.MaxTagLength + 1, "each tag")]
	public async Task Clean_RefusesWhatIsTooLong(int captionLength, int warningLength, int tagCount, int tagLength, string says)
	{
		var entry = Entry("a", 0, caption: new string('c', captionLength), warning: new string('w', warningLength)) with
		{
			Tags = Enumerable.Range(0, tagCount).Select(i => $"{i}{new string('t', Math.Max(0, tagLength - 1))}").ToList(),
		};
		var refused = GalleryRules.Clean(entry).Expect<Error<string>>();
		await Assert.That(refused.Value).Contains(says);
	}

	[Test]
	[Arguments("iVBORw0KGgoAAAANSUhEUg==", "image/png")]
	[Arguments("/9j/4AAQSkZJRgABAQ==", "image/jpeg")]
	[Arguments("R0lGODlhAQABAA==", "image/gif")]
	[Arguments("UklGRiQAAABXRUJQVlA4", "image/webp")]
	public async Task SniffImageType_NamesTheTypeFromTheBytes(string base64, string type)
		=> await Assert.That(GalleryRules.SniffImageType(Convert.FromBase64String(base64)).Expect<string>()).IsEqualTo(type);

	[Test]
	[Arguments("PGh0bWw+PHNjcmlwdD4=")]
	[Arguments("PHN2ZyB4bWxucz0i")]
	[Arguments("")]
	public async Task SniffImageType_RefusesAnythingElse(string base64)
		=> await Assert.That(GalleryRules.SniffImageType(Convert.FromBase64String(base64)) is NotFound).IsTrue();
}
