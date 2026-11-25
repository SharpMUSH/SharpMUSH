using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

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

	public const int MaxCaptionLength = 300;
	public const int MaxTags = 10;
	public const int MaxTagLength = 32;
	public const int MaxWarningLength = 100;
	public const int MaxReportLength = 500;

	/// <summary>
	/// Orders by <see cref="GalleryEntry.Order"/> and renumbers from 0; keeps the first icon and the
	/// first banner. A gallery with no icon gets its first image that is not the banner as its icon, so
	/// the avatar comes from somewhere without the banner standing in for it; a gallery holding only its
	/// banner has no avatar (the profile draws initials), and a gallery without a banner keeps none.
	/// An image with a warning is neither: the avatar and the banner show everywhere without a click.
	/// Without <paramref name="autoIcon"/> (<c>gallery_portrait_approval</c>), no image becomes the avatar by
	/// itself: staff choose it, and a gallery whose avatar went keeps none. At most one image waits for approval,
	/// and never the avatar itself; with <paramref name="autoIcon"/> none waits.
	/// </summary>
	public static IReadOnlyList<GalleryEntry> Normalize(IEnumerable<GalleryEntry> entries, bool autoIcon = true)
	{
		var ordered = entries.OrderBy(e => e.Order).Select((e, i) => e with { Order = i }).ToList();
		var iconSeen = false;
		var bannerSeen = false;
		for (var i = 0; i < ordered.Count; i++)
		{
			var entry = ordered[i];
			var shown = entry.Warning is null;
			var isIcon = entry.IsIcon && shown && !iconSeen;
			var isBanner = entry.IsBanner && shown && !bannerSeen;
			iconSeen |= isIcon;
			bannerSeen |= isBanner;
			ordered[i] = entry with { IsIcon = isIcon, IsBanner = isBanner };
		}

		var firstNotBanner = ordered.FindIndex(e => !e.IsBanner && e.Warning is null);
		if (autoIcon && !iconSeen && firstNotBanner >= 0)
		{
			ordered[firstNotBanner] = ordered[firstNotBanner] with { IsIcon = true };
		}

		// Requests exist only while staff choose portraits; turning approval off settles them.
		var waiting = autoIcon ? -1 : ordered.FindLastIndex(e => e.PortraitPending && !e.IsIcon && e.Warning is null);
		for (var i = 0; i < ordered.Count; i++)
		{
			if (ordered[i].PortraitPending != (i == waiting))
			{
				ordered[i] = ordered[i] with { PortraitPending = i == waiting };
			}
		}

		return ordered;
	}

	/// <summary>
	/// A short, stable name for one wording of the consent text. The portal sends the name of the text it showed,
	/// so terms changed after the uploader read them are never recorded as accepted.
	/// </summary>
	public static string ConsentVersion(string text) =>
		Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];

	/// <summary>
	/// Why one more image of <paramref name="adding"/> bytes does not fit, or null when it does. With
	/// <paramref name="skipOfficial"/>, official art counts toward neither limit; <paramref name="maxBytes"/> 0 sets none.
	/// </summary>
	public static string? OverLimit(IEnumerable<(GalleryEntry Entry, long Bytes)> gallery, long adding, uint maxImages, uint maxMegabytes, bool skipOfficial)
	{
		var counted = gallery.Where(g => !(skipOfficial && g.Entry.Official)).ToList();
		var maxBytes = (long)maxMegabytes * 1_048_576;
		var used = counted.Sum(g => g.Bytes);
		return (counted.Count >= maxImages, maxBytes > 0 && used + adding > maxBytes) switch
		{
			(true, _) => $"A gallery holds at most {maxImages} images. Remove one before adding another.",
			(_, true) => $"This gallery's images take {Megabytes(used)} of its {maxMegabytes} MB, and this one needs {Megabytes(adding)}. Remove an image or add a smaller one.",
			_ => null,
		};
	}

	private static string Megabytes(long bytes) =>
		string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1_048_576.0:0.#} MB");

	/// <summary>
	/// The caption, tags and warning an owner sent, tidied: trimmed, blank as null, tags lower-cased and
	/// each once. Too long, or too many tags, is refused with what to change rather than cut short.
	/// </summary>
	public static Result<GalleryEntry> Clean(GalleryEntry entry)
	{
		var caption = Blank(entry.Caption);
		var warning = Blank(entry.Warning);
		var tags = (entry.Tags ?? [])
			.Select(t => t.Trim().ToLowerInvariant())
			.Where(t => t.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		return (caption, warning, tags) switch
		{
			({ Length: > MaxCaptionLength }, _, _) => new Error<string>($"Keep a caption to {MaxCaptionLength} characters."),
			(_, { Length: > MaxWarningLength }, _) => new Error<string>($"Keep a warning to {MaxWarningLength} characters."),
			(_, _, { Count: > MaxTags }) => new Error<string>($"Give an image at most {MaxTags} tags."),
			_ when tags.Any(t => t.Length > MaxTagLength) => new Error<string>($"Keep each tag to {MaxTagLength} characters."),
			_ => entry with { Caption = caption, Warning = warning, Tags = tags.Count == 0 ? null : tags },
		};
	}

	private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

	/// <summary>
	/// The image type a file's first bytes say it is, whatever the browser declared: PNG, JPEG, GIF or
	/// WebP, the types the gallery takes. Anything else is not found.
	/// </summary>
	public static Found<string> SniffImageType(ReadOnlySpan<byte> head) => head switch
	{
		[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
		[0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
		[(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => "image/gif",
		[(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
		_ => new NotFound(),
	};

	/// <summary>How many bytes <see cref="SniffImageType"/> reads.</summary>
	public const int SniffLength = 12;

	/// <summary>The icon's URL, the banner's URL and the icon's caption; empty where there is none.</summary>
	public static ImageMirror Mirror(IReadOnlyList<GalleryEntry> entries)
	{
		var icon = entries.FirstOrDefault(e => e.IsIcon);
		var banner = entries.FirstOrDefault(e => e.IsBanner);
		return new ImageMirror(icon?.Url ?? string.Empty, banner?.Url ?? string.Empty, icon?.Caption ?? string.Empty);
	}
}
