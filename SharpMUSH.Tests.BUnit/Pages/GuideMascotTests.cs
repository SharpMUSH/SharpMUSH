using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class GuideMascotTests : TrackingBunitContext
{
	[Test]
	public async Task ApprovedTopicImagesStayWithinTheirStyleAndTopic()
	{
		await Assert.That(SharpieArtCatalog.Resolve("comicbook", "Science Fiction", null, null, GuideImagery.Full, SharpieArtMoment.Welcome)?.Id).IsEqualTo("sharpie-science-fiction-comicbook-welcome");
		await Assert.That(SharpieArtCatalog.Resolve("chibi", "Fantasy", null, null, GuideImagery.Reduced, topic: "player-world")?.Id).IsEqualTo("sharpie-fantasy-chibi-world");
		await Assert.That(SharpieArtCatalog.Resolve("chibi", "Fantasy", null, null, GuideImagery.Full, topic: "player-sign-in")?.Id).IsEqualTo("sharpie-fantasy-chibi-player-sign-in");
		await Assert.That(SharpieArtCatalog.Resolve("comicbook", "Science Fiction", null, null, GuideImagery.Full, topic: "player-world")?.Id).IsEqualTo("sharpie-science-fiction-comicbook-player-world");
		await Assert.That(SharpieArtCatalog.Resolve("chibi", "Fantasy", null, null, GuideImagery.None, topic: "player-world")).IsNull();
	}

	[Test]
	public async Task TopicArtworkUsesSubgenreThenGenreAndNeverAnotherLesson()
	{
		var general = new SharpieArt("general", "chibi", "Fantasy", "general.png", 100, 100);
		var genre = new SharpieArt("genre-channels", "chibi", "Fantasy", "genre.png", 100, 100, "player-channels");
		var subgenre = new SharpieArt("subgenre-channels", "chibi", "High Fantasy", "subgenre.png", 100, 100, "player-channels");
		var other = new SharpieArt("other-topic", "chibi", "High Fantasy", "other.png", 100, 100, "player-character");
		SharpieArt[] approved = [general, genre, subgenre, other];
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "chibi", "Fantasy", "High Fantasy", other.Id, GuideImagery.Full, topic: "player-channels")).IsEqualTo(subgenre);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "chibi", "Fantasy", "unknown", null, GuideImagery.Full, topic: "player-channels")).IsEqualTo(genre);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "chibi", "Fantasy", "High Fantasy", other.Id, GuideImagery.Full, topic: "player-world")).IsEqualTo(general);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "anime", "Fantasy", "High Fantasy", subgenre.Id, GuideImagery.Full, topic: "player-channels")).IsNull();
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "chibi", "Fantasy", "High Fantasy", null, GuideImagery.None, topic: "player-channels")).IsNull();
	}

	[Test]
	public async Task SharedMomentsSelectTheirOwnTopicAndReducedImageryKeepsSelection()
	{
		var welcome = new SharpieArt("welcome", "chibi", "Science Fiction", "welcome.png", 100, 100, "shared-welcome");
		var complete = new SharpieArt("complete", "chibi", "Science Fiction", "complete.png", 100, 100, "shared-completion");
		SharpieArt[] approved = [welcome, complete];
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "chibi", "Science Fiction", null, null, GuideImagery.Reduced, SharpieArtMoment.Welcome)).IsEqualTo(welcome);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "chibi", "Science Fiction", null, welcome.Id, GuideImagery.Full, SharpieArtMoment.Completion)).IsEqualTo(complete);
	}

	[Test]
	public async Task GenreApprovalDoesNotCrossStyleBoundaries()
	{
		foreach (var genre in new[] { "Fantasy", "Science Fiction" })
		{
			var approved = SharpieArtCatalog.Resolve("minimalist", genre, "unapproved-subgenre", null, GuideImagery.Full);
			await Assert.That(approved?.Theme).IsEqualTo(genre);
			foreach (var style in SharpieArtCatalog.Styles.Where(style => style != "minimalist"))
			{
				var fallback = SharpieArtCatalog.Resolve(style, genre, "unapproved-subgenre", approved!.Id, GuideImagery.Full);
				await Assert.That(fallback?.Theme).IsEqualTo("neutral");
				await Assert.That(fallback?.Id).IsEqualTo("sharpie-minimal-neutral");
			}
		}
	}

	[Test]
	public async Task ChannelLessonTeachesDiscoveryAndAudience()
	{
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		var cut = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord).Single(x => x.Id == "channels")));
		await Assert.That(cut.Find(".guide-channels").TextContent).Contains("@channel/list");
		await Assert.That(cut.Find(".guide-channels").TextContent).Contains("@channel/on NAME");
		await Assert.That(cut.Find(".guide-channels").TextContent).Contains("not just your room");
		await Assert.That(cut.FindAll(".guide-channels button").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Every_style_keeps_an_approved_mascot_until_its_art_is_available()
	{
		foreach (var style in SharpieArtCatalog.Styles)
		{
			var art = SharpieArtCatalog.Resolve(style, "Fantasy", "unknown", null, GuideImagery.Full);
			await Assert.That(art).IsNotNull();
			await Assert.That(SharpieArtCatalog.Approved.Contains(art!)).IsTrue();
			await Assert.That(SharpieArtCatalog.Resolve(style, null, null, null, GuideImagery.None)).IsNull();
		}
	}

	[Test]
	public async Task Lesson_card_shows_Sharpie_by_default()
	{
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		var cut = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord)[0]));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(1);
		await Assert.That(cut.Markup).Contains("help say");
		await Assert.That(cut.Find(".guide-panel-heading").TextContent.Trim()).IsEqualTo("");
		await Assert.That(cut.Find(".guide-panel-heading button").GetAttribute("aria-label")).IsEqualTo("Stop this guide");
		await Assert.That(cut.Markup).Contains("Read how a specific command works");
		await Assert.That(cut.Find("img").GetAttribute("src")).Contains("sharpie-minimal.svg");
	}
	[Test]
	public async Task No_artwork_hides_the_mascot_and_reduced_keeps_it_small()
	{
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		var hidden = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord)[0]).Add(x => x.Imagery, GuideImagery.None));
		await Assert.That(hidden.FindAll("img").Count).IsEqualTo(0);
		var reduced = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord)[0]).Add(x => x.Imagery, GuideImagery.Reduced));
		await Assert.That(reduced.FindAll("img.guide-mascot-reduced").Count).IsEqualTo(1);
	}

}
