using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The guide's picture wherever the portal draws it: the game's own <c>onboarding_guide_art</c> when it has one,
/// else Sharpie from the reviewed artwork for the game's style, genre and subgenre (<see cref="SharpieArtCatalog"/>).
/// </summary>
public static class GuideArt
{
	/// <summary>The <see cref="SharpieArt.Id"/> and <see cref="SharpieArt.Theme"/> of the game's own guide picture.</summary>
	public const string OwnId = "game-guide";

	/// <summary>
	/// The game's own guide picture as a square <see cref="SharpieArt"/> the size of Sharpie's (each place that draws
	/// it fits it in, keeping its shape), or null when <paramref name="picture"/> is empty or not an address the portal draws.
	/// </summary>
	public static SharpieArt? Own(string? picture) =>
		ImageUrlPolicy.IsRenderable(picture) ? new SharpieArt(OwnId, "", OwnId, picture!.Trim(), 512, 512) : null;

	/// <summary>Whether <paramref name="art"/> is the game's own guide picture rather than Sharpie.</summary>
	public static bool IsOwn(SharpieArt? art) => art?.Id == OwnId;

	/// <summary>
	/// The guide's picture for <paramref name="presentation"/>: its own when the game set one, else Sharpie as
	/// <see cref="SharpieArtCatalog.Resolve"/> picks. Null when <paramref name="imagery"/> is None.
	/// </summary>
	public static SharpieArt? Resolve(OnboardingPresentation? presentation, GuideImagery imagery,
		SharpieArtMoment moment = SharpieArtMoment.Lesson, string? topic = null) =>
		Resolve(presentation?.GuideArt, presentation?.ArtStyle, presentation?.Genre, presentation?.Subgenre,
			presentation?.ArtOverrideId, imagery, moment, topic);

	/// <summary>As <see cref="Resolve(OnboardingPresentation?, GuideImagery, SharpieArtMoment, string?)"/>, from the values an editor holds.</summary>
	public static SharpieArt? Resolve(string? ownPicture, string? style, string? genre, string? subgenre, string? overrideId,
		GuideImagery imagery, SharpieArtMoment moment = SharpieArtMoment.Lesson, string? topic = null) =>
		imagery == GuideImagery.None
			? null
			: Own(ownPicture) ?? SharpieArtCatalog.Resolve(style, genre, subgenre, overrideId, imagery, moment, topic);
}
