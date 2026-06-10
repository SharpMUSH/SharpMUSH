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

			var saved = await service.SaveAsync("picture.png", "image/png", Bytes(payload), "#42");
			await Assert.That(saved.IsT0).IsTrue();

			var asset = saved.AsT0;
			await Assert.That(asset.FileName).IsEqualTo("picture.png");
			await Assert.That(asset.ContentType).IsEqualTo("image/png");
			await Assert.That(asset.SizeBytes).IsEqualTo((long)payload.Length);
			await Assert.That(asset.Sha256).IsEqualTo(expectedSha);
			await Assert.That(asset.UploaderDbref).IsEqualTo("#42");

			var opened = await service.OpenAsync(asset.Id);
			await Assert.That(opened.IsT0).IsTrue();

			var (meta, stream) = opened.AsT0;
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
			var saved = await service.SaveAsync("gone.png", "image/png", Bytes("xyz"), "#1");
			var id = saved.AsT0.Id;

			var deleted = await service.DeleteAsync(id);
			await Assert.That(deleted.IsT0).IsTrue();

			var opened = await service.OpenAsync(id);
			await Assert.That(opened.IsT1).IsTrue();

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
			var saved = await service.SaveAsync("../../etc/pa$$ wd.png", "image/png", Bytes("data"), "#1");
			await Assert.That(saved.IsT0).IsTrue();
			await Assert.That(saved.AsT0.FileName).IsEqualTo("pa___wd.png");
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
			await Assert.That(opened.IsT1).IsTrue();

			var deleted = await service.DeleteAsync(unknown);
			await Assert.That(deleted.IsT1).IsTrue();

			// Path-traversal-shaped ids must also be rejected, not probed on disk.
			var traversal = await service.OpenAsync("../../etc/passwd");
			await Assert.That(traversal.IsT1).IsTrue();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}
}
