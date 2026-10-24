using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class SharpieArtThemeTests
{
	[Test]
	public async Task SameNamedGenreAndSubgenreSelectTheirOwnArtwork()
	{
		var genre = new SharpieArt("genre-none", "pixel", "None", "genre.png", 100, 100, "player-world", SharpieArtThemeKind.Genre);
		var subgenre = new SharpieArt("subgenre-none", "pixel", "None", "subgenre.png", 100, 100, "player-world", SharpieArtThemeKind.Subgenre);
		SharpieArt[] approved = [genre, subgenre];
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "None", "None", null, GuideImagery.Full, topic: "player-world")).IsEqualTo(subgenre);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "None", null, null, GuideImagery.Full, topic: "player-world")).IsEqualTo(genre);
		await Assert.That(SharpieArtCatalog.ResolveFrom([subgenre], "pixel", "None", null, null, GuideImagery.Full, topic: "player-world")).IsNull();
		await Assert.That(SharpieArtCatalog.ResolveFrom([genre], "pixel", "Fantasy", "None", null, GuideImagery.Full, topic: "player-world")).IsNull();
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "None", "None", null, GuideImagery.Reduced, topic: "player-world")).IsEqualTo(subgenre);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "None", "None", null, GuideImagery.None, topic: "player-world")).IsNull();
	}
	[Test]
	public async Task KindSpecificFallbackKeepsStyleAndTopicBoundaries()
	{
		var genre = new SharpieArt("genre", "pixel", "None", "genre.png", 100, 100, "player-world", SharpieArtThemeKind.Genre);
		var subgenre = new SharpieArt("subgenre", "pixel", "None", "subgenre.png", 100, 100, null, SharpieArtThemeKind.Subgenre);
		SharpieArt[] approved = [subgenre, genre];
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "None", "None", null, GuideImagery.Full, topic: "player-world")).IsEqualTo(genre);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "Fantasy", "None", null, GuideImagery.Full, topic: "player-channels")).IsEqualTo(subgenre);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "anime", "None", "None", null, GuideImagery.Full, topic: "player-world")).IsNull();
		await Assert.That(SharpieArtCatalog.ResolveFrom([genre], "pixel", "None", "None", null, GuideImagery.Full, topic: "player-channels")).IsNull();
	}

	[Test]
	public async Task ExistingUnambiguousArtworkAndExplicitOverridesRemainCompatible()
	{
		var legacy = new SharpieArt("legacy", "pixel", "High Fantasy", "legacy.png", 100, 100, "player-world");
		var selected = new SharpieArt("selected", "pixel", "None", "selected.png", 100, 100, "player-world", SharpieArtThemeKind.Genre);
		SharpieArt[] approved = [legacy, selected];
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "Fantasy", "High Fantasy", null, GuideImagery.Full, topic: "player-world")).IsEqualTo(legacy);
		await Assert.That(SharpieArtCatalog.ResolveFrom(approved, "pixel", "Fantasy", "High Fantasy", selected.Id, GuideImagery.Full, topic: "player-world")).IsEqualTo(selected);
	}

}
