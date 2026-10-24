using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NSubstitute;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SkiaSharp;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The game's WebP pictures as PNG, for MXP and Pueblo clients (Axmud reads only bmp, gif, jpg and png) and
/// the terminal renderer.
/// </summary>
public class PngPicturesTests
{
	[Test]
	[Arguments("/assets/presets/objects/god.webp", "/pictures/png/assets/presets/objects/god.webp.png")]
	[Arguments("/api/wiki-assets/abc/pic.WEBP", "/pictures/png/api/wiki-assets/abc/pic.WEBP.png")]
	[Arguments("/assets/logo.png", "/assets/logo.png")]
	[Arguments("/assets/logo.JPG", "/assets/logo.JPG")]
	[Arguments("/assets/logo.gif", "/assets/logo.gif")]
	[Arguments("/assets/logo", "/assets/logo")]
	[Arguments("/assets/logo.webp?v=2", "/assets/logo.webp?v=2")]
	[Arguments("https://pictures.example/cat.webp", "https://pictures.example/cat.webp")]
	[Arguments("//pictures.example/cat.webp", "//pictures.example/cat.webp")]
	[Arguments("cat.webp", "cat.webp")]
	[Arguments("/pictures/png/assets/a.webp.png", "/pictures/png/assets/a.webp.png")]
	public async Task AnUnreadablePictureOfTheGames_IsAddressedAsPng(string source, string expected) =>
		await Assert.That(PngPictures.Readable(source)).IsEqualTo(expected);

	[Test]
	[Arguments("/pictures/png/assets/a.webp.png", "/assets/a.webp")]
	[Arguments("/pictures/png/.png", null)]
	[Arguments("/pictures/png/assets/a.webp", null)]
	[Arguments("/assets/a.webp.png", null)]
	public async Task AConvertedAddress_NamesItsPicture(string path, string? expected) =>
		await Assert.That(PngPictures.Original(path)).IsEqualTo(expected);

	[Test]
	public async Task AWebpFile_IsServedAsPngOfTheSameSize()
	{
		var root = Directory.CreateTempSubdirectory("png-pictures-");
		try
		{
			Directory.CreateDirectory(Path.Join(root.FullName, "assets"));
			await File.WriteAllBytesAsync(Path.Join(root.FullName, "assets", "cat.webp"), Encoded(SKEncodedImageFormat.Webp, 6, 4));
			using var files = new PhysicalFileProvider(root.FullName);
			using var service = Service(files);

			var png = await service.ConvertAsync(PngPictures.Original(PngPictures.Readable("/assets/cat.webp"))!, CancellationToken.None);

			await Assert.That(png).IsNotNull();
			using var decoded = SKBitmap.Decode(png!);
			await Assert.That(png!.AsSpan(0, 4).ToArray()).IsEquivalentTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
			await Assert.That((decoded.Width, decoded.Height)).IsEqualTo((6, 4));
			await Assert.That(decoded.GetPixel(0, 0)).IsEqualTo(SKColors.Red);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task ALargePicture_IsServedNoLargerThanTheLimit()
	{
		var png = PngPictureService.Convert(Encoded(SKEncodedImageFormat.Webp, 1942, 809));

		using var decoded = SKBitmap.Decode(png!);
		await Assert.That((decoded.Width, decoded.Height)).IsEqualTo((PngPictureService.MaxServedSide, 427));
	}

	[Test]
	[Arguments("/assets/missing.webp")]
	[Arguments("/assets/not-a-picture.webp")]
	[Arguments("/../outside.webp")]
	public async Task NoPicture_IsNull(string path)
	{
		var root = Directory.CreateTempSubdirectory("png-pictures-");
		try
		{
			Directory.CreateDirectory(Path.Join(root.FullName, "assets"));
			await File.WriteAllTextAsync(Path.Join(root.FullName, "assets", "not-a-picture.webp"), "hello");
			using var files = new PhysicalFileProvider(root.FullName);
			using var service = Service(files);

			await Assert.That(await service.ConvertAsync(path, CancellationToken.None)).IsNull();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	private static PngPictureService Service(IFileProvider webRoot)
	{
		var environment = Substitute.For<IWebHostEnvironment>();
		environment.WebRootFileProvider.Returns(webRoot);
		return new PngPictureService(environment, Substitute.For<IWikiAssetService>());
	}

	private static byte[] Encoded(SKEncodedImageFormat format, int width, int height)
	{
		using var bitmap = new SKBitmap(width, height);
		bitmap.Erase(SKColors.Red);
		using var data = bitmap.Encode(format, 100);
		return data.ToArray();
	}
}
