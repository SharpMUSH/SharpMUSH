using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
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
	private static (WikiAssetController Controller, DirectoryInfo Root) MakeController() =>
		MakeController(Directory.CreateTempSubdirectory("wiki-asset-controller-test-"), "#42");

	/// <summary>
	/// A controller over <paramref name="root"/> for an account playing <paramref name="dbref"/> and holding
	/// <paramref name="scopes"/>; the game defines one custom permission, <c>lore.secret</c>.
	/// </summary>
	private static (WikiAssetController Controller, DirectoryInfo Root) MakeController(DirectoryInfo root, string dbref, params string[] scopes)
	{
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>())
			.Returns(new HashSet<string>(scopes));
		var roles = Substitute.For<IRoleRegistryService>();
		roles.GetCustomPermissionsAsync(Arg.Any<CancellationToken>())
			.Returns([new CustomPermission("lore.secret", "Lore", "Lore secrets", 0)]);
		var controller = new WikiAssetController(
			new FileSystemWikiAssetService(root.FullName),
			capabilities,
			roles,
			NullLogger<WikiAssetController>.Instance);

		var claims = new List<Claim>
		{
			new(GameHub.CharacterDbrefClaim, dbref),
			new(ClaimTypes.NameIdentifier, $"account-{dbref}"),
		};
		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(claims, AccountSessionAuthenticationHandler.SchemeName))
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

	/// <summary>Stores a file directly, as an upload through the wiki (shared) or a gallery (private) would.</summary>
	private static async Task<WikiAsset> StoreAsync(DirectoryInfo root, string fileName, string uploader, bool isPrivate = false, string? lockPermission = null)
	{
		var service = new FileSystemWikiAssetService(root.FullName);
		var asset = (await service.SaveAsync(fileName, "image/png", new MemoryStream([1, 2, 3]), uploader, isPrivate)).Expect<WikiAsset>();
		return lockPermission is null
			? asset
			: (await service.SetSharingAsync(asset.Id, isPrivate, lockPermission)).Expect<WikiAsset>();
	}

	private static async Task<IReadOnlyList<string>> LibraryNamesAsync(WikiAssetController controller)
	{
		var ok = (OkObjectResult)await controller.Library();
		return [.. ((IEnumerable<WikiAssetController.LibraryAssetDto>)ok.Value!).Select(a => a.FileName).Order()];
	}

	[Test]
	public async Task Library_ListsSharedFiles_NotOnesKeptToAGallery_NorOnesLockedAway()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			await StoreAsync(root, "harbour.png", "#7");
			await StoreAsync(root, "portrait.png", "#7", isPrivate: true);
			await StoreAsync(root, "secret.png", "#7", lockPermission: "lore.secret");
			var (controller, _) = MakeController(root, "#42");

			await Assert.That(await LibraryNamesAsync(controller)).IsEquivalentTo(["harbour.png"]);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Library_ALockedFile_IsFoundByItsPermission_ItsUploader_AndMediaAdmin()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			await StoreAsync(root, "secret.png", "#7", lockPermission: "lore.secret");
			await StoreAsync(root, "portrait.png", "#7", isPrivate: true);

			await Assert.That(await LibraryNamesAsync(MakeController(root, "#42", "lore.secret").Controller)).IsEquivalentTo(["secret.png"]);
			await Assert.That(await LibraryNamesAsync(MakeController(root, "#7").Controller)).IsEquivalentTo(["secret.png"])
				.Because("a file kept to a gallery is not in the library, even for its uploader");
			await Assert.That(await LibraryNamesAsync(MakeController(root, "#42", PortalPermission.MediaAdmin).Controller)).IsEquivalentTo(["secret.png"]);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task Library_Search_NarrowsByFileName()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			await StoreAsync(root, "harbour-day.png", "#7");
			await StoreAsync(root, "forest.png", "#7");
			var (controller, _) = MakeController(root, "#42");

			var ok = (OkObjectResult)await controller.Library(search: "HARBOUR");
			await Assert.That(((IEnumerable<WikiAssetController.LibraryAssetDto>)ok.Value!).Select(a => a.FileName)).IsEquivalentTo(["harbour-day.png"]);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SetSharing_TheUploaderLocksAFile_ToADefinedPermission()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			var asset = await StoreAsync(root, "map.png", "#7");
			var (controller, _) = MakeController(root, "#7");

			var result = await controller.SetSharing(asset.Id, new WikiAssetController.SharingRequest(true, " Lore.Secret "), CancellationToken.None);

			var dto = (WikiAssetController.LibraryAssetDto)((OkObjectResult)result).Value!;
			await Assert.That(dto.Lock).IsEqualTo("lore.secret");
			await Assert.That(await LibraryNamesAsync(MakeController(root, "#42").Controller)).IsEmpty();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SetSharing_SomeoneElse_IsRefused()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			var asset = await StoreAsync(root, "map.png", "#7");
			var (controller, _) = MakeController(root, "#42");

			var result = await controller.SetSharing(asset.Id, new WikiAssetController.SharingRequest(true, PortalPermission.MediaAdmin), CancellationToken.None);
			await Assert.That(result).IsTypeOf<ForbidResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SetSharing_AnUndefinedPermission_IsRefused()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			var asset = await StoreAsync(root, "map.png", "#7");
			var (controller, _) = MakeController(root, "#42", PortalPermission.MediaAdmin);

			var result = await controller.SetSharing(asset.Id, new WikiAssetController.SharingRequest(true, "lore.typo"), CancellationToken.None);
			await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SetSharing_ASharedFile_IsNeverMadePrivateAgain()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			var asset = await StoreAsync(root, "map.png", "#7");
			var (controller, _) = MakeController(root, "#7");

			var result = await controller.SetSharing(asset.Id, new WikiAssetController.SharingRequest(false, null), CancellationToken.None);
			await Assert.That(result).IsTypeOf<ConflictObjectResult>();
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	[Test]
	public async Task SetSharing_SharesAFileKeptToAGallery()
	{
		var root = Directory.CreateTempSubdirectory("wiki-asset-controller-test-");
		try
		{
			var asset = await StoreAsync(root, "portrait.png", "#7", isPrivate: true);
			var (controller, _) = MakeController(root, "#7");

			await controller.SetSharing(asset.Id, new WikiAssetController.SharingRequest(true, null), CancellationToken.None);
			await Assert.That(await LibraryNamesAsync(MakeController(root, "#42").Controller)).IsEquivalentTo(["portrait.png"]);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}
}
