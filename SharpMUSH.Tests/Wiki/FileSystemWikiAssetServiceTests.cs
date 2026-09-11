using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Server.Services;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="FileSystemWikiAssetService"/>: round-trip storage,
/// listing, deletion, filename sanitization, and not-found handling.
/// Each test uses its own temp directory, removed afterwards.
/// </summary>
public class FileSystemWikiAssetServiceTests
{
	private static (FileSystemWikiAssetService Service, DirectoryInfo Root) MakeService()
	{
		var root = Directory.CreateTempSubdirectory("wiki-assets-test-");
		return (new FileSystemWikiAssetService(root.FullName), root);
	}

	private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

	[Test]
	public async Task SaveAndOpen_RoundTripsBytesAndMetadata()
	{
		var (service, root) = MakeService();
		try
		{
			var payload = "fake png bytes";
			var expectedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

			var asset = await Assert.That((await service.SaveAsync("picture.png", "image/png", Bytes(payload), "#42")).Value).IsTypeOf<WikiAsset>();

			await Assert.That(asset!.FileName).IsEqualTo("picture.png");
			await Assert.That(asset.ContentType).IsEqualTo("image/png");
			await Assert.That(asset.SizeBytes).IsEqualTo((long)payload.Length);
			await Assert.That(asset.Sha256).IsEqualTo(expectedSha);
			await Assert.That(asset.UploaderDbref).IsEqualTo("#42");

			var (meta, stream) = await Assert.That((await service.OpenAsync(asset.Id)).Value)
				.IsTypeOf<(WikiAsset Asset, Stream Content)>();
			await Assert.That(meta).IsEqualTo(asset);

			using var reader = new StreamReader(stream);
			var roundTripped = await reader.ReadToEndAsync();
			await Assert.That(roundTripped).IsEqualTo(payload);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task ListAsync_ReturnsSavedAssets_WithPagination()
	{
		var (service, root) = MakeService();
		try
		{
			await service.SaveAsync("a.png", "image/png", Bytes("aaa"), "#1");
			await service.SaveAsync("b.png", "image/png", Bytes("bbb"), "#1");
			await service.SaveAsync("c.png", "image/png", Bytes("ccc"), "#1");

			var all = await service.ListAsync();
			await Assert.That(all.Count).IsEqualTo(3);

			var page = await service.ListAsync(skip: 1, take: 1);
			await Assert.That(page.Count).IsEqualTo(1);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task DeleteAsync_RemovesAsset()
	{
		var (service, root) = MakeService();
		try
		{
			var saved = await Assert.That((await service.SaveAsync("gone.png", "image/png", Bytes("xyz"), "#1")).Value).IsTypeOf<WikiAsset>();
			var id = saved!.Id;

			var deleted = await service.DeleteAsync(id);
			await Assert.That(deleted.Value).IsTypeOf<None>();

			var opened = await service.OpenAsync(id);
			await Assert.That(opened.Value).IsTypeOf<NotFound>();

			var listed = await service.ListAsync();
			await Assert.That(listed.Count).IsEqualTo(0);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SaveAsync_SanitizesFileName()
	{
		var (service, root) = MakeService();
		try
		{
			var saved = await Assert.That((await service.SaveAsync("../../etc/pa$$ wd.png", "image/png", Bytes("data"), "#1")).Value).IsTypeOf<WikiAsset>();
			await Assert.That(saved!.FileName).IsEqualTo("pa___wd.png");
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SanitizeFileName_HandlesEdgeCases()
	{
		await Assert.That(FileSystemWikiAssetService.SanitizeFileName(@"C:\evil\..\shot.png")).IsEqualTo("shot.png");
		await Assert.That(FileSystemWikiAssetService.SanitizeFileName("normal-file_1.webp")).IsEqualTo("normal-file_1.webp");
		await Assert.That(FileSystemWikiAssetService.SanitizeFileName("///")).IsEqualTo("file");
		await Assert.That(FileSystemWikiAssetService.SanitizeFileName("..")).IsEqualTo("file");
	}

	[Test]
	public async Task OpenAndDelete_UnknownId_ReturnNotFound()
	{
		var (service, root) = MakeService();
		try
		{
			var unknown = Guid.NewGuid().ToString("N");

			var opened = await service.OpenAsync(unknown);
			await Assert.That(opened.Value).IsTypeOf<NotFound>();

			var deleted = await service.DeleteAsync(unknown);
			await Assert.That(deleted.Value).IsTypeOf<NotFound>();

			// Path-traversal-shaped ids must also be rejected, not probed on disk.
			var traversal = await service.OpenAsync("../../etc/passwd");
			await Assert.That(traversal.Value).IsTypeOf<NotFound>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}
}
