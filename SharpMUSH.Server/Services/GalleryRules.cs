using SharpMUSH.Library.API;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The invariants every gallery write keeps (spec §2), and the three standard image attributes the
/// gallery mirrors so softcode, OOB payloads and the portal all read the same picture.
/// </summary>
public static class GalleryRules
{
	/// <summary>The values a gallery write puts into <c>IMAGE</c>, <c>IMAGE`BANNER</c> and <c>IMAGE`ALT</c>.
	/// An empty string clears the attribute.</summary>
	public readonly record struct ImageMirror(string Image, string Banner, string Alt);

	/// <summary>
	/// Orders by <see cref="GalleryEntry.Order"/> and renumbers from 0; keeps the first icon and the
	/// first banner. A gallery with no icon gets its first image that is not the banner as its icon, so
	/// the avatar comes from somewhere without the banner standing in for it; a gallery holding only its
	/// banner has no avatar (the profile draws initials), and a gallery without a banner keeps none.
	/// </summary>
	public static IReadOnlyList<GalleryEntry> Normalize(IEnumerable<GalleryEntry> entries)
	{
		var ordered = entries.OrderBy(e => e.Order).Select((e, i) => e with { Order = i }).ToList();
		var iconSeen = false;
		var bannerSeen = false;
		for (var i = 0; i < ordered.Count; i++)
		{
			var entry = ordered[i];
			var isIcon = entry.IsIcon && !iconSeen;
			var isBanner = entry.IsBanner && !bannerSeen;
			iconSeen |= isIcon;
			bannerSeen |= isBanner;
			ordered[i] = entry with { IsIcon = isIcon, IsBanner = isBanner };
		}

		var firstNotBanner = ordered.FindIndex(e => !e.IsBanner);
		if (!iconSeen && firstNotBanner >= 0)
		{
			ordered[firstNotBanner] = ordered[firstNotBanner] with { IsIcon = true };
		}

		return ordered;
	}

	/// <summary>The icon's URL, the banner's URL and the icon's caption; empty where there is none.</summary>
	public static ImageMirror Mirror(IReadOnlyList<GalleryEntry> entries)
	{
		var icon = entries.FirstOrDefault(e => e.IsIcon);
		var banner = entries.FirstOrDefault(e => e.IsBanner);
		return new ImageMirror(icon?.Url ?? string.Empty, banner?.Url ?? string.Empty, icon?.Caption ?? string.Empty);
	}
}
