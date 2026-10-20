namespace SharpMUSH.Library.Models.Portal.Onboarding;

public sealed record SharpieArt(string Id, string Style, string Theme, string Url, int Width, int Height);

public enum SharpieArtMoment { Lesson, Welcome, Completion }

/// <summary>Only reviewed, shipped assets belong here. User metadata never becomes a file path.</summary>
public static class SharpieArtCatalog
{
	public static IReadOnlyList<string> Styles { get; } = Array.AsReadOnly(new[] { "minimalist", "chibi", "anime", "kemono", "pixel", "comicbook" });
	// Only individually owner-approved artwork is shipped; other genres retain the neutral fallback.
	public static IReadOnlyList<SharpieArt> Approved { get; } = Array.AsReadOnly(new[] {
		new SharpieArt("sharpie-fantasy-minimalist", "minimalist", "Fantasy", "images/sharpie/sharpie-fantasy-minimalist.png", 1254, 1254),
		new SharpieArt("sharpie-science-fiction-minimalist", "minimalist", "Science Fiction", "images/sharpie/sharpie-science-fiction-minimalist.png", 1254, 1254),
		new SharpieArt("sharpie-minimal-neutral", "minimalist", "neutral", "images/sharpie/sharpie-minimal.svg", 512, 512),
		new SharpieArt("sharpie-primary-neutral", "minimalist", "neutral", "images/sharpie/sharpie-primary.svg", 512, 512),
		new SharpieArt("sharpie-smile-neutral", "minimalist", "neutral", "images/sharpie/sharpie-smile.svg", 512, 512)
	});

	public static SharpieArt? Resolve(string? style, string? genre, string? subgenre, string? overrideId, GuideImagery imagery, SharpieArtMoment moment = SharpieArtMoment.Lesson)
	{
		if (imagery == GuideImagery.None) return null;
		var family = style is not null && Styles.Contains(style) ? style : "minimalist";
		var neutralId = moment switch
		{
			SharpieArtMoment.Welcome => "sharpie-primary-neutral",
			SharpieArtMoment.Completion => "sharpie-smile-neutral",
			_ => "sharpie-minimal-neutral"
		};
		return Approved.FirstOrDefault(art => art.Id == overrideId && art.Style == family)
			?? Approved.FirstOrDefault(art => art.Style == family && string.Equals(art.Theme, subgenre, StringComparison.OrdinalIgnoreCase))
			?? Approved.FirstOrDefault(art => art.Style == family && string.Equals(art.Theme, genre, StringComparison.OrdinalIgnoreCase))
			?? Approved.FirstOrDefault(art => art.Id == neutralId);
	}
}
