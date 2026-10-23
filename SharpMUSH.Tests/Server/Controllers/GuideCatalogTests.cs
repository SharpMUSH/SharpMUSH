using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.Server.Controllers;

public class GuideCatalogTests
{
	[Test]
	public async Task EveryPathUsesStablePlayerSteps()
	{
		foreach (var path in Enum.GetValues<GuidePath>())
		{
			var lessons = GuideCatalog.Player(path);
			await Assert.That(lessons.All(lesson => GuideStepIds.Player.Contains(lesson.Id))).IsTrue();
			await Assert.That(lessons.Select(lesson => lesson.Id).Distinct().Count()).IsEqualTo(lessons.Count);
			await Assert.That(lessons.All(lesson => !string.IsNullOrWhiteSpace(lesson.Body))).IsTrue();
			await Assert.That(lessons.Where(lesson => lesson.InsertCommand is not null).All(lesson => lesson.InsertCommand == "look")).IsTrue();
		}
	}

	[Test]
	public async Task FamiliarityChangesTheExplanationAndOnlyLookIsInsertable()
	{
		await Assert.That(GuideCatalog.Player(GuidePath.Discord)[0].Body == GuideCatalog.Player(GuidePath.Mush)[0].Body).IsFalse();
		await Assert.That(GuideCatalog.Commands.Single(command => command.Command == "page NAME=Hello").Audience).IsEqualTo("One named player");
		await Assert.That(GuideCatalog.Player(GuidePath.Mush).Any(lesson => lesson.Body.Contains("Command", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task UnknownGenreCannotBecomeAPathAndNoArtAlwaysWins()
	{
		await Assert.That(SharpieArtCatalog.Resolve("chibi", "../../secret", "made-up", "../../image", GuideImagery.Full)?.Id).IsEqualTo("sharpie-minimal-neutral");
		await Assert.That(SharpieArtCatalog.Resolve("chibi", "Fantasy", "", "", GuideImagery.None)).IsNull();
		await Assert.That(SharpieArtCatalog.Styles).IsEquivalentTo(new[] { "minimalist", "chibi", "anime", "kemono", "pixel", "comicbook" });
	}
	[Test]
	public async Task ApprovedNeutralMascotIsTheSafeMinimalistFallback()
	{
		var art = SharpieArtCatalog.Resolve("minimalist", "../../secret", "unknown", "../../image", GuideImagery.Full);
		await Assert.That(art?.Id).IsEqualTo("sharpie-minimal-neutral");
		await Assert.That(art?.Url).IsEqualTo("images/sharpie/sharpie-minimal.svg");
		await Assert.That(SharpieArtCatalog.Resolve("minimalist", "", "", art!.Id, GuideImagery.None)).IsNull();
	}

	[Test]
	public async Task VeteransGetAConciseDeltaTourAndRoleplayersGetTheirOwnExamples()
	{
		var veteran = GuideCatalog.Player(GuidePath.Mush);
		await Assert.That(veteran.Select(lesson => lesson.Id)).IsEquivalentTo(new[] { "place", "sign-in", "world", "channels", "continue" });
		await Assert.That(veteran.Any(lesson => lesson.InsertCommand is not null)).IsFalse();
		await Assert.That(veteran.Single(lesson => lesson.Id == "world").Body.Contains("Command", StringComparison.Ordinal)).IsTrue();
		var roleplay = GuideCatalog.Player(GuidePath.OtherRoleplay);
		await Assert.That(roleplay.Single(lesson => lesson.Id == "speak-act").Body.Contains("tabletop", StringComparison.Ordinal)).IsTrue();
	}

	[Test]
	public async Task AdministratorChecklistMatchesItsStableStepsAndKeepsGodForEmergencies()
	{
		await Assert.That(GuideCatalog.Administrator.Select(lesson => lesson.Id)).IsEquivalentTo(GuideStepIds.Administrator, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var staff = GuideCatalog.Administrator.Single(lesson => lesson.Id == "staff-character");
		await Assert.That(staff.Body).Contains("#1");
		await Assert.That(staff.Body).Contains("=WIZARD");
		await Assert.That(GuideCatalog.Administrator.Single(lesson => lesson.Id == "channels").Body).Contains("@channel/add Staff=admin");
	}
}
