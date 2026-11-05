using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SkiaSharp;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The sizes a wiki page is saved with come from the pictures the game serves: its stored files and its web root.
/// A picture on another site is never fetched.
/// </summary>
public class WikiAssetImageSizesTests
{
	private const string AssetId = "0123456789abcdef0123456789abcdef";

	[Test]
	public async Task AStoredPicture_IsMeasuredFromItsHeader()
	{
		var assets = Substitute.For<IWikiAssetService>();
		assets.OpenAsync(AssetId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Found<OpenedWikiAsset>>(
			new OpenedWikiAsset(Asset("image/png"), new MemoryStream(Encoded(SKEncodedImageFormat.Png, 300, 120)))));

		var size = await Service(new NullFileProvider(), assets).FindAsync($"/api/wiki-assets/{AssetId}/quay.png");

		await Assert.That(size.Expect<ImageSize>()).IsEqualTo(new ImageSize(300, 120));
	}

	[Test]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 256"/>""", 512, 256)]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg" width="64px" height="32" viewBox="0 0 512 256"/>""", 64, 32)]
	[Arguments("""<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg" width="100%" viewBox="0,0,30,10"/>""", 30, 10)]
	public async Task AnSvgUnderTheWebRoot_IsMeasuredFromItsSizeOrViewBox(string svg, int width, int height)
	{
		var root = Directory.CreateTempSubdirectory();
		try
		{
			Directory.CreateDirectory(Path.Combine(root.FullName, "assets"));
			await File.WriteAllTextAsync(Path.Combine(root.FullName, "assets", "Logo.svg"), svg);
			using var files = new PhysicalFileProvider(root.FullName);

			var size = await Service(files, Substitute.For<IWikiAssetService>()).FindAsync("/assets/Logo.svg");

			await Assert.That(size.Expect<ImageSize>()).IsEqualTo(new ImageSize(width, height));
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	[Arguments("https://pictures.example/cat.png")]
	[Arguments("//pictures.example/cat.png")]
	[Arguments("cat.png")]
	[Arguments("/assets/missing.png")]
	[Arguments("/api/wiki-assets/not-an-id/cat.png")]
	public async Task APictureTheGameDoesNotServe_HasNoSize(string url)
	{
		var assets = Substitute.For<IWikiAssetService>();
		assets.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<Found<OpenedWikiAsset>>(new NotFound()));

		var size = await Service(new NullFileProvider(), assets).FindAsync(url);

		await Assert.That(size is NotFound).IsTrue();
		await assets.DidNotReceive().OpenAsync(Arg.Is<string>(id => id != AssetId), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task WithoutAWebRoot_OnlyStoredPicturesAreMeasured()
	{
		var assets = Substitute.For<IWikiAssetService>();
		assets.OpenAsync(AssetId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Found<OpenedWikiAsset>>(
			new OpenedWikiAsset(Asset("image/png"), new MemoryStream(Encoded(SKEncodedImageFormat.Png, 40, 30)))));
		var sizes = new WikiAssetImageSizes(assets);

		await Assert.That((await sizes.FindAsync($"/api/wiki-assets/{AssetId}/quay.png")).Expect<ImageSize>()).IsEqualTo(new ImageSize(40, 30));
		await Assert.That(await sizes.FindAsync("/assets/Logo.svg") is NotFound).IsTrue();
	}

	[Test]
	public async Task AFileThatDoesNotDecode_HasNoSize()
	{
		var assets = Substitute.For<IWikiAssetService>();
		assets.OpenAsync(AssetId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Found<OpenedWikiAsset>>(
			new OpenedWikiAsset(Asset("image/png"), new MemoryStream("not a picture"u8.ToArray()))));

		var size = await Service(new NullFileProvider(), assets).FindAsync($"/api/wiki-assets/{AssetId}/quay.png");

		await Assert.That(size is NotFound).IsTrue();
	}

	private static WikiAssetImageSizes Service(IFileProvider webRoot, IWikiAssetService assets)
	{
		var environment = Substitute.For<IWebHostEnvironment>();
		environment.WebRootFileProvider.Returns(webRoot);
		return new WikiAssetImageSizes(assets, environment);
	}

	private static WikiAsset Asset(string contentType) =>
		new(AssetId, "quay.png", contentType, 0, string.Empty, "#1", DateTimeOffset.UnixEpoch);

	private static byte[] Encoded(SKEncodedImageFormat format, int width, int height)
	{
		using var bitmap = new SKBitmap(width, height);
		bitmap.Erase(SKColors.Red);
		using var data = bitmap.Encode(format, 100);
		return data.ToArray();
	}
}
