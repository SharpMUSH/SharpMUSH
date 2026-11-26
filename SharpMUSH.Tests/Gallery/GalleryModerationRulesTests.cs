using SharpMUSH.Library.API;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Gallery;

/// <summary>
/// The rules behind the gallery's moderation options (#2879): no portrait chosen by itself while staff approve them,
/// one waiting portrait at a time, the byte limit and its exemption for official art, and the consent text's version.
/// </summary>
public class GalleryModerationRulesTests
{
	private static GalleryEntry Entry(string id, int order, bool icon = false, bool pending = false, bool official = false, string? warning = null) =>
		new(id, $"{id}.png", $"/api/wiki-assets/{id}/{id}.png", null, order, icon, Warning: warning)
		{
			PortraitPending = pending,
			Official = official,
		};

	[Test]
	public async Task WithoutAutoIcon_NoImageBecomesThePortraitByItself()
	{
		var result = GalleryRules.Normalize([Entry("a", 0), Entry("b", 1)], autoIcon: false);
		await Assert.That(result.Any(e => e.IsIcon)).IsFalse().Because("staff choose the portrait; the profile shows the default");
	}

	[Test]
	public async Task WithoutAutoIcon_AChosenPortraitStays()
	{
		var result = GalleryRules.Normalize([Entry("a", 0), Entry("b", 1, icon: true)], autoIcon: false);
		await Assert.That(result.Single(e => e.IsIcon).AssetId).IsEqualTo("b");
	}

	[Test]
	public async Task OnlyTheLastRequestWaits()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, pending: true), Entry("b", 1, pending: true)], autoIcon: false);
		await Assert.That(result.Where(e => e.PortraitPending).Select(e => e.AssetId).ToList()).IsEquivalentTo(["b"]);
	}

	[Test]
	public async Task ThePortraitItselfNeverWaits()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, icon: true, pending: true)], autoIcon: false);
		await Assert.That(result[0].PortraitPending).IsFalse();
	}

	[Test]
	public async Task AnImageWithAWarningNeverWaits()
	{
		var result = GalleryRules.Normalize([Entry("a", 0, pending: true, warning: "blood")], autoIcon: false);
		await Assert.That(result[0].PortraitPending).IsFalse().Because("a warned image can never be the portrait");
	}

	[Test]
	public async Task TheImageCount_IsTheLimitWithoutAByteLimit()
	{
		var gallery = new[] { (Entry("a", 0), 10L), (Entry("b", 1), 10L) };
		await Assert.That(GalleryRules.OverLimit(gallery, 5, maxImages: 2, maxMegabytes: 0, skipOfficial: false))
			.IsEqualTo("A gallery holds at most 2 images. Remove one before adding another.");
		await Assert.That(GalleryRules.OverLimit(gallery, 5, maxImages: 3, maxMegabytes: 0, skipOfficial: false)).IsNull();
	}

	[Test]
	public async Task TheByteLimit_RefusesAnImageThatWouldPassIt()
	{
		const long mb = 1_048_576;
		var gallery = new[] { (Entry("a", 0), 3 * mb) };
		await Assert.That(GalleryRules.OverLimit(gallery, 2 * mb, maxImages: 50, maxMegabytes: 5, skipOfficial: false)).IsNull()
			.Because("exactly at the limit still fits");
		await Assert.That(GalleryRules.OverLimit(gallery, 2 * mb + 1, maxImages: 50, maxMegabytes: 5, skipOfficial: false))
			.IsEqualTo("This gallery's images take 3 MB of its 5 MB, and this one needs 2 MB. Remove an image or add a smaller one.");
	}

	[Test]
	public async Task OfficialArt_CountsTowardNeitherLimit_WhenTheGameSaysSo()
	{
		const long mb = 1_048_576;
		var gallery = new[] { (Entry("a", 0, official: true), 4 * mb), (Entry("b", 1), 1 * mb) };
		await Assert.That(GalleryRules.OverLimit(gallery, 3 * mb, maxImages: 2, maxMegabytes: 5, skipOfficial: true)).IsNull();
		await Assert.That(GalleryRules.OverLimit(gallery, 3 * mb, maxImages: 2, maxMegabytes: 5, skipOfficial: false)).IsNotNull();
	}

	[Test]
	public async Task TheConsentVersion_FollowsTheWording()
	{
		var version = GalleryRules.ConsentVersion("I made this.");
		await Assert.That(version.Length).IsEqualTo(16);
		await Assert.That(GalleryRules.ConsentVersion("I made this.")).IsEqualTo(version);
		await Assert.That(GalleryRules.ConsentVersion("I made this!")).IsNotEqualTo(version);
	}
}
