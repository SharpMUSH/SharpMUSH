using SharpMUSH.Configuration.Mssp;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class SharpieArtCoverageTests
{
	[Test]
	[Arguments("GENRE", SharpieArtThemeKind.Genre)]
	[Arguments("SUBGENRE", SharpieArtThemeKind.Subgenre)]
	public async Task EverySupportedThemeAndLessonResolvesItsOwnArtwork(string variable, SharpieArtThemeKind kind)
	{
		var themes = MsspCatalog.All.Single(item => item.Name == variable).Choices;
		var otherThemes = MsspCatalog.All.Single(item => item.Name == (variable == "GENRE" ? "SUBGENRE" : "GENRE")).Choices;
		var topics = Enum.GetValues<GuidePath>().SelectMany(GuideCatalog.Player)
			.Select(lesson => $"player-{lesson.Id}")
			.Concat(GuideCatalog.Administrator.Select(lesson => $"administrator-{lesson.Id}"))
			.Concat(["shared-welcome", "shared-completion"]).Distinct();
		var errors = new List<string>();
		foreach (var style in SharpieArtCatalog.Styles)
			foreach (var theme in themes)
				foreach (var topic in topics)
				{
					var matches = SharpieArtCatalog.Approved.Where(art => art.Style == style
						&& art.Theme == theme && art.Topic == topic
						&& (art.ThemeKind == kind || art.ThemeKind is null && !otherThemes.Contains(theme))).ToArray();
					var cell = $"{kind}/{theme}/{style}/{topic}";
					if (matches.Length != 1)
					{
						errors.Add($"{cell}: expected one illustration, found {matches.Length}");
						continue;
					}
					var genre = kind == SharpieArtThemeKind.Genre ? theme : null;
					var subgenre = kind == SharpieArtThemeKind.Subgenre ? theme : null;
					foreach (var imagery in new[] { GuideImagery.Full, GuideImagery.Reduced })
						if (SharpieArtCatalog.Resolve(style, genre, subgenre, null, imagery, topic: topic) != matches[0])
							errors.Add($"{cell}: {imagery} resolved a fallback");
					if (SharpieArtCatalog.Resolve(style, genre, subgenre, null, GuideImagery.None, topic: topic) is not null)
						errors.Add($"{cell}: imagery disabled but an illustration was selected");
				}
		await Assert.That(errors).IsEmpty();
	}
}
