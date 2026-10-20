namespace SharpMUSH.Library.Models.Portal.Onboarding;

public sealed record SharpieArt(string Id, string Style, string Theme, string Url, int Width, int Height);

/// <summary>Only reviewed, shipped assets belong here. User metadata never becomes a file path.</summary>
public static class SharpieArtCatalog
{
	public static IReadOnlyList<string> Styles { get; } = Array.AsReadOnly(new[] { "minimalist", "chibi", "anime", "kemono", "pixel", "comicbook" });
	// The owner-approved neutral mascot remains available while genre-specific candidates are reviewed.
	public static IReadOnlyList<SharpieArt> Approved { get; } = Array.AsReadOnly(new[] { new SharpieArt("sharpie-minimal-neutral", "minimalist", "neutral", "images/sharpie/sharpie-minimal.svg", 512, 512) });

	public static SharpieArt? Resolve(string? style, string? genre, string? subgenre, string? overrideId, GuideImagery imagery)
	{
		if (imagery == GuideImagery.None) return null;
		var family = style is not null && Styles.Contains(style) ? style : "minimalist";
		return Approved.FirstOrDefault(art => art.Id == overrideId && art.Style == family)
			?? Approved.FirstOrDefault(art => art.Style == family && string.Equals(art.Theme, subgenre, StringComparison.OrdinalIgnoreCase))
			?? Approved.FirstOrDefault(art => art.Style == family && string.Equals(art.Theme, genre, StringComparison.OrdinalIgnoreCase))
			?? Approved.FirstOrDefault(art => art.Style == family && art.Theme == "neutral")
			?? Approved.FirstOrDefault(art => art.Id == "sharpie-minimal-neutral");
	}
}
