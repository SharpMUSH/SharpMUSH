using Microsoft.Extensions.Localization;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// Localized byte-size rendering. The unit abbreviations are translated (French counts in octets, so
/// "KB" becomes "ko"), which means the formatter needs a localizer and therefore cannot be a static
/// helper on a page. An extension on the localizer keeps the call sites as short as the raw
/// interpolations they replaced.
/// </summary>
public static class ByteSizeFormat
{
	private const long Kilobyte = 1024L;
	private const long Megabyte = Kilobyte * 1024;
	private const long Gigabyte = Megabyte * 1024;

	/// <summary>Renders <paramref name="bytes"/> in the largest unit that leaves it above 1, to one decimal.</summary>
	public static string FormatBytes(this IStringLocalizer<SharedResource> loc, long bytes) => bytes switch
	{
		< Kilobyte => loc["EnumUnitBytes", bytes],
		< Megabyte => loc["EnumUnitKilobytes", Scaled(bytes, Kilobyte)],
		< Gigabyte => loc["EnumUnitMegabytes", Scaled(bytes, Megabyte)],
		_ => loc["EnumUnitGigabytes", Scaled(bytes, Gigabyte)],
	};

	private static string Scaled(long bytes, long unit) => ((double)bytes / unit).ToString("F1");
}
