using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using System.Security.Claims;
using System.Text;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <see cref="WikiAssetController"/> using the real
/// <see cref="FileSystemWikiAssetService"/> over a per-test temp directory:
/// upload whitelisting (415), SVG script rejection (400), serve content type, delete.
/// </summary>
public class WikiAssetControllerTests
{
	private static (WikiAssetController Controller, DirectoryInfo Root) MakeController()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		var controller = new WikiAssetController(
			new FileSystemWikiAssetService(root.FullName),
			NullLogger<WikiAssetController>.Instance);

		var claims = new List<Claim> { new(GameHub.CharacterDbrefClaim, "#42") };
		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
			}
		};
		return (controller, root);
	}

	private static FormFile MakeFormFile(byte[] bytes, string fileName, string contentType)
	{
		var stream = new MemoryStream(bytes);
		return new FormFile(stream, 0, stream.Length, "file", fileName)
		{
			Headers = new HeaderDictionary(),
			ContentType = contentType,
		};
	}

	[Test]
	public async Task Upload_Png_Returns201WithUrl()
	{
		var (controller, root) = MakeController();
		try
		{
			var file = MakeFormFile([1, 2, 3, 4], "shot.png", "image/png");

			var result = await controller.Upload(file, CancellationToken.None);

			await Assert.That(result).IsTypeOf<CreatedResult>();
			var created = (CreatedResult)result;
			var dto = (WikiAssetController.UploadedAssetDto)created.Value!;
			await Assert.That(dto.FileName).IsEqualTo("shot.png");
			await Assert.That(dto.ContentType).IsEqualTo("image/png");
			await Assert.That(dto.SizeBytes).IsEqualTo(4L);
			await Assert.That(dto.Url).IsEqualTo($"/api/wiki-assets/{dto.Id}/shot.png");
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Upload_TextPlain_Returns415()
	{
		var (controller, root) = MakeController();
		try
		{
			var file = MakeFormFile("hello"u8.ToArray(), "note.txt", "text/plain");

			var result = await controller.Upload(file, CancellationToken.None);

			await Assert.That(result).IsTypeOf<ObjectResult>();
			await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status415UnsupportedMediaType);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Upload_SvgWithScript_Returns400()
	{
		var (controller, root) = MakeController();
		try
		{
			var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>";
			var file = MakeFormFile(Encoding.UTF8.GetBytes(svg), "evil.svg", "image/svg+xml");

			var result = await controller.Upload(file, CancellationToken.None);

			await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Upload_SvgWithEventHandler_Returns400_ButCleanSvgSucceeds()
	{
		var (controller, root) = MakeController();
		try
		{
			var evil = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle onload = \"steal()\" r=\"1\"/></svg>";
			var evilResult = await controller.Upload(
				MakeFormFile(Encoding.UTF8.GetBytes(evil), "evil.svg", "image/svg+xml"), CancellationToken.None);
			await Assert.That(evilResult).IsTypeOf<BadRequestObjectResult>();

			var clean = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"1\"/></svg>";
			var cleanResult = await controller.Upload(
				MakeFormFile(Encoding.UTF8.GetBytes(clean), "clean.svg", "image/svg+xml"), CancellationToken.None);
			await Assert.That(cleanResult).IsTypeOf<CreatedResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Serve_ExistingAsset_ReturnsFileStreamWithStoredContentType()
	{
		var (controller, root) = MakeController();
		try
		{
			var upload = await controller.Upload(MakeFormFile([9, 8, 7], "pic.webp", "image/webp"), CancellationToken.None);
			var dto = (WikiAssetController.UploadedAssetDto)((CreatedResult)upload).Value!;

			var result = await controller.Serve(dto.Id, "pic.webp", CancellationToken.None);

			await Assert.That(result).IsTypeOf<FileStreamResult>();
			var file = (FileStreamResult)result;
			await Assert.That(file.ContentType).IsEqualTo("image/webp");

			using var memory = new MemoryStream();
			await file.FileStream.CopyToAsync(memory);
			await Assert.That(memory.ToArray()).IsEquivalentTo(new byte[] { 9, 8, 7 });

			var headers = controller.ControllerContext.HttpContext.Response.Headers;
			await Assert.That(headers.CacheControl.ToString()).IsEqualTo("public, max-age=31536000, immutable");
			await Assert.That(headers.XContentTypeOptions.ToString()).IsEqualTo("nosniff");
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Serve_UnknownAsset_Returns404()
	{
		var (controller, root) = MakeController();
		try
		{
			var result = await controller.Serve(Guid.NewGuid().ToString("N"), "missing.png", CancellationToken.None);
			await Assert.That(result).IsTypeOf<NotFoundResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Delete_ExistingAsset_Returns204_ThenServe404()
	{
		var (controller, root) = MakeController();
		try
		{
			var upload = await controller.Upload(MakeFormFile([1], "bye.gif", "image/gif"), CancellationToken.None);
			var dto = (WikiAssetController.UploadedAssetDto)((CreatedResult)upload).Value!;

			var deleted = await controller.Delete(dto.Id);
			await Assert.That(deleted).IsTypeOf<NoContentResult>();

			var served = await controller.Serve(dto.Id, "bye.gif", CancellationToken.None);
			await Assert.That(served).IsTypeOf<NotFoundResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Delete_UnknownAsset_Returns404()
	{
		var (controller, root) = MakeController();
		try
		{
			var result = await controller.Delete(Guid.NewGuid().ToString("N"));
			await Assert.That(result).IsTypeOf<NotFoundResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}
}
