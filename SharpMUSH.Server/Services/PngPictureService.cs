using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SkiaSharp;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The game's own pictures converted to PNG (<see cref="SharpMUSH.Library.Markup.PngPictures"/>): a file under
/// the web root, or a wiki asset. Each conversion is kept, up to <see cref="MaxCacheBytes"/> of PNG.
/// </summary>
public sealed class PngPictureService(IWebHostEnvironment environment, IWikiAssetService assets) : IDisposable
{
	/// <summary>The largest picture read; a wiki asset is at most 10 MB.</summary>
	public const int MaxSourceBytes = 16 * 1024 * 1024;

	/// <summary>The largest picture decoded, in pixels each way.</summary>
	public const int MaxDecodedSide = 8192;

	/// <summary>
	/// The longest side a picture is served at. Plenty for a client window; a 1942-pixel seed picture is
	/// 3 MB as PNG at full size and under 1 MB at this one.
	/// </summary>
	public const int MaxServedSide = 1024;

	/// <summary>How many bytes of PNG are kept.</summary>
	public const long MaxCacheBytes = 32L * 1024 * 1024;

	private const string WikiAssetPrefix = "/api/wiki-assets/";

	private readonly MemoryCache _converted = new(new MemoryCacheOptions { SizeLimit = MaxCacheBytes });

	/// <summary>The picture at <paramref name="path"/> as PNG, or null when there is none or it does not decode.</summary>
	public async Task<byte[]?> ConvertAsync(string path, CancellationToken ct)
	{
		if (_converted.TryGetValue(path, out byte[]? held)) return held;

		var source = await ReadAsync(path, ct);
		if (source is null || Convert(source) is not { } png) return null;

		_converted.Set(path, png, new MemoryCacheEntryOptions { Size = png.Length });
		return png;
	}

	private async Task<byte[]?> ReadAsync(string path, CancellationToken ct)
	{
		if (path.StartsWith(WikiAssetPrefix, StringComparison.OrdinalIgnoreCase))
		{
			// /api/wiki-assets/{id}/{fileName}: the file name is cosmetic, as it is to the asset endpoint.
			var id = path[WikiAssetPrefix.Length..].Split('/')[0];
			if (id.Length == 0 || await assets.OpenAsync(id, ct) is not OpenedWikiAsset(_, var content)) return null;
			await using (content) return await ReadBoundedAsync(content, ct);
		}

		var file = environment.WebRootFileProvider.GetFileInfo(path);
		if (!file.Exists || file.IsDirectory || file.Length > MaxSourceBytes) return null;
		await using var stream = file.CreateReadStream();
		return await ReadBoundedAsync(stream, ct);
	}

	private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken ct)
	{
		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		int read;
		while ((read = await stream.ReadAsync(chunk, ct)) > 0)
		{
			if (buffer.Length + read > MaxSourceBytes) return null;
			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}

	/// <summary>
	/// <paramref name="source"/> as PNG, no larger than <see cref="MaxServedSide"/>; the first frame of a moving
	/// picture. Null when it does not decode.
	/// </summary>
	public static byte[]? Convert(byte[] source)
	{
		using var data = SKData.CreateCopy(source);
		using var codec = SKCodec.Create(data);
		if (codec is null) return null;

		var size = codec.Info;
		if (size.Width is <= 0 or > MaxDecodedSide || size.Height is <= 0 or > MaxDecodedSide) return null;

		// An opaque picture stays opaque, so its PNG has no alpha channel to carry.
		var alpha = size.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul;
		using var bitmap = new SKBitmap(size.Width, size.Height, SKColorType.Rgba8888, alpha);
		var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
		if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;

		using var served = Shrunk(bitmap);
		using var png = (served ?? bitmap).Encode(SKEncodedImageFormat.Png, 100);
		return png?.ToArray();
	}

	/// <summary><paramref name="bitmap"/> scaled to <see cref="MaxServedSide"/>, or null when it is no larger.</summary>
	private static SKBitmap? Shrunk(SKBitmap bitmap)
	{
		var longest = Math.Max(bitmap.Width, bitmap.Height);
		if (longest <= MaxServedSide) return null;

		var scale = (double)MaxServedSide / longest;
		var size = bitmap.Info.WithSize(Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
			Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
		return bitmap.Resize(size, new SKSamplingOptions(SKCubicResampler.Mitchell));
	}

	public void Dispose() => _converted.Dispose();
}
