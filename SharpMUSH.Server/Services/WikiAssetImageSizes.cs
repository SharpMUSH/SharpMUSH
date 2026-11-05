using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.AspNetCore.Hosting;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SkiaSharp;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The size of a picture this game serves: a wiki asset (<c>/api/wiki-assets/{id}/{name}</c>) or a file under the
/// web root (<c>/assets/Logo.svg</c>). A raster picture's size is read from its header without decoding it; an SVG's
/// from its <c>width</c> and <c>height</c>, or else its <c>viewBox</c>. A picture on another site has no size here.
/// A host that serves no web root (the import worker) measures only the stored files.
/// </summary>
public sealed partial class WikiAssetImageSizes(IWikiAssetService assets, IWebHostEnvironment? environment = null) : IWikiImageSizes
{
	[GeneratedRegex(@"^/api/wiki-assets/(?<id>[0-9a-f]{32})/", RegexOptions.IgnoreCase)]
	private static partial Regex AssetPath();

	[GeneratedRegex(@"^\s*(?<n>\d+(\.\d+)?)\s*(px)?\s*$")]
	private static partial Regex SvgLength();

	public async Task<Found<ImageSize>> FindAsync(string url, CancellationToken ct = default)
	{
		if (AssetPath().Match(url) is { Success: true } match)
		{
			if (await assets.OpenAsync(match.Groups["id"].Value, ct) is not OpenedWikiAsset opened) return new NotFound();
			await using var content = opened.Content;
			return Measure(content, opened.Asset.ContentType);
		}

		// A path on this site; never a URL with a scheme or host ("//other.site/x.png").
		if (environment is null || !url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal)) return new NotFound();
		var path = url.Split('?', '#')[0];
		var file = environment.WebRootFileProvider.GetFileInfo(path);
		if (!file.Exists || file.IsDirectory) return new NotFound();
		await using var stream = file.CreateReadStream();
		return Measure(stream, path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : string.Empty);
	}

	private static Found<ImageSize> Measure(Stream content, string contentType)
	{
		if (string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase)) return MeasureSvg(content);

		using var codec = SKCodec.Create(content);
		return codec?.Info is { Width: > 0, Height: > 0 } info ? new ImageSize(info.Width, info.Height) : new NotFound();
	}

	/// <summary>The root element's width and height in pixels, or else its viewBox's.</summary>
	private static Found<ImageSize> MeasureSvg(Stream content)
	{
		try
		{
			using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
			if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg") return new NotFound();

			if (Pixels(reader.GetAttribute("width")) is { } width && Pixels(reader.GetAttribute("height")) is { } height)
				return Size(width, height);

			return reader.GetAttribute("viewBox")?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries) is [_, _, var w, var h]
				&& double.TryParse(w, CultureInfo.InvariantCulture, out var boxWidth)
				&& double.TryParse(h, CultureInfo.InvariantCulture, out var boxHeight)
				? Size(boxWidth, boxHeight)
				: new NotFound();
		}
		catch (XmlException)
		{
			return new NotFound();
		}
	}

	private static double? Pixels(string? length) =>
		length is not null && SvgLength().Match(length) is { Success: true } match
			? double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture)
			: null;

	private static Found<ImageSize> Size(double width, double height) =>
		width >= 1 && height >= 1 && width <= int.MaxValue && height <= int.MaxValue
			? new ImageSize((int)Math.Round(width), (int)Math.Round(height))
			: new NotFound();
}
