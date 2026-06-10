using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.Interfaces;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// REST API for wiki image/file assets.
/// Routes:
///   POST   /api/wiki-assets                 — upload an image (authenticated, 10 MB max)
///   GET    /api/wiki-assets/{id}/{fileName} — stream the asset bytes (anonymous, cacheable)
///   GET    /api/wiki-assets?skip&amp;take       — list asset metadata (Wizard+)
///   DELETE /api/wiki-assets/{id}            — delete an asset (Wizard+)
/// </summary>
[ApiController]
[Route("api/wiki-assets")]
public partial class WikiAssetController(
	IWikiAssetService assetService,
	ILogger<WikiAssetController> logger) : ControllerBase
{
	/// <summary>Content types we accept for upload. Everything else gets 415.</summary>
	private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/png",
		"image/jpeg",
		"image/gif",
		"image/webp",
		"image/svg+xml",
	};

	[GeneratedRegex(@"<script|javascript:|on\w+\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex DangerousSvgContent();

	/// <summary>Response body for a successful upload.</summary>
	public record UploadedAssetDto(string Id, string FileName, string Url, long SizeBytes, string ContentType);

	/// <summary>Asset metadata returned by the list endpoint.</summary>
	public record WikiAssetDto(
		string Id,
		string FileName,
		string Url,
		string ContentType,
		long SizeBytes,
		string Sha256,
		string UploaderDbref,
		DateTimeOffset UploadedAt);

	private static string AssetUrl(string id, string fileName) => $"/api/wiki-assets/{id}/{fileName}";

	/// <summary>
	/// POST /api/wiki-assets
	/// Uploads an image asset (multipart form, field name "file"). Only image content
	/// types are accepted; SVGs are scanned for embedded scripting and rejected when unsafe.
	/// </summary>
	[HttpPost]
	[Authorize]
	[RequestSizeLimit(10_485_760)]
	public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
	{
		if (file is null || file.Length == 0)
		{
			return BadRequest(new { error = "No file uploaded." });
		}

		var contentType = file.ContentType;
		if (!AllowedContentTypes.Contains(contentType))
		{
			return StatusCode(StatusCodes.Status415UnsupportedMediaType,
				new { error = $"Content type '{contentType}' is not allowed. Allowed: {string.Join(", ", AllowedContentTypes)}" });
		}

		// SVG is XML and can carry scripts — reject anything that looks active.
		if (string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
		{
			using var reader = new StreamReader(file.OpenReadStream());
			var text = await reader.ReadToEndAsync(ct);
			if (DangerousSvgContent().IsMatch(text))
			{
				return BadRequest(new { error = "SVG contains scripting content and was rejected." });
			}
		}

		var uploaderDbref = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "#1";

		await using var content = file.OpenReadStream();
		var result = await assetService.SaveAsync(file.FileName, contentType, content, uploaderDbref, ct);

		return result.Match<IActionResult>(
			asset =>
			{
				logger.LogInformation("Wiki asset uploaded: id={Id} name={Name} size={Size} by={Uploader}",
					asset.Id, asset.FileName, asset.SizeBytes, uploaderDbref);
				var url = AssetUrl(asset.Id, asset.FileName);
				return Created(url, new UploadedAssetDto(asset.Id, asset.FileName, url, asset.SizeBytes, asset.ContentType));
			},
			err => StatusCode(StatusCodes.Status500InternalServerError, new { error = err.Value }));
	}

	/// <summary>
	/// GET /api/wiki-assets/{id}/{fileName}
	/// Streams the asset with its stored content type. The fileName segment is cosmetic
	/// (assets are looked up by id only); ids are immutable so responses cache forever.
	/// </summary>
	[HttpGet("{id}/{fileName}")]
	[AllowAnonymous]
	public async Task<IActionResult> Serve(string id, string fileName, CancellationToken ct)
	{
		var result = await assetService.OpenAsync(id, ct);
		return result.Match<IActionResult>(
			found =>
			{
				Response.Headers.CacheControl = "public, max-age=31536000, immutable";
				Response.Headers.XContentTypeOptions = "nosniff";
				return File(found.Content, found.Asset.ContentType);
			},
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/wiki-assets?skip=0&amp;take=100
	/// Lists asset metadata, newest first.
	/// </summary>
	[HttpGet]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 100)
	{
		var assets = await assetService.ListAsync(skip, take);
		return Ok(assets.Select(a => new WikiAssetDto(
			a.Id, a.FileName, AssetUrl(a.Id, a.FileName), a.ContentType,
			a.SizeBytes, a.Sha256, a.UploaderDbref, a.UploadedAt)));
	}

	/// <summary>
	/// DELETE /api/wiki-assets/{id}
	/// Deletes an asset and its metadata.
	/// </summary>
	[HttpDelete("{id}")]
	[Authorize(Roles = nameof(PortalRole.Wizard))]
	public async Task<IActionResult> Delete(string id)
	{
		var result = await assetService.DeleteAsync(id);
		return result.Match<IActionResult>(
			_ =>
			{
				logger.LogInformation("Wiki asset deleted: id={Id}", id);
				return NoContent();
			},
			_ => NotFound());
	}
}
