using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Helpers;
using MarkupString;
using System.Security.Claims;
using System.Text.Json;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Character profile gallery. Image bytes are stored by <see cref="IWikiAssetService"/> (the shared
/// file store); gallery composition (order, captions, icon) is kept as a <c>PROFILE`GALLERY</c> JSON
/// attribute on the character — backend-agnostic, no extra DB schema. Edits require the requester to
/// control the character (owner) or be staff (Wizard/Royalty), enforced by <see cref="IPermissionService"/>.
///
/// Routes:
///   GET    /api/profile/{name}/gallery          — list gallery entries (anonymous)
///   POST   /api/profile/{name}/gallery          — upload an image (owner/staff)
///   PUT    /api/profile/{name}/gallery          — replace order/captions/icon (owner/staff)
///   DELETE /api/profile/{name}/gallery/{assetId} — remove an image (owner/staff)
/// </summary>
[ApiController]
[Route("api/profile/{name}/gallery")]
public class GalleryController(
	IWikiAssetService assetService,
	IMediator mediator,
	IAttributeService attributeService,
	IPermissionService permissionService,
	ILogger<GalleryController> logger) : ControllerBase
{
	private const string GalleryAttribute = "PROFILE`GALLERY";

	private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/png", "image/jpeg", "image/gif", "image/webp"
	};

	/// <summary>A gallery image entry; mirrors the client model.</summary>
	public record GalleryEntry(string AssetId, string FileName, string Url, string? Caption, int Order, bool IsIcon);

	// ── Reads ────────────────────────────────────────────────────────────────

	[HttpGet]
	[AllowAnonymous]
	public async Task<ActionResult<IReadOnlyList<GalleryEntry>>> List(string name, CancellationToken ct)
	{
		var character = await ResolveCharacterAsync(name, ct);
		if (character is null)
		{
			return NotFound();
		}

		var entries = await ReadGalleryAsync(character);
		return entries.OrderBy(e => e.Order).ToList();
	}

	// ── Writes (owner / staff) ─────────────────────────────────────────────────

	[HttpPost]
	[Authorize]
	[RequestSizeLimit(10_485_760)]
	public async Task<IActionResult> Upload(string name, IFormFile file, CancellationToken ct)
	{
		var (character, allowed) = await ResolveAndAuthorizeAsync(name, ct);
		if (character is null) return NotFound();
		if (!allowed) return Forbid();

		if (file is null || file.Length == 0)
		{
			return BadRequest(new { error = "No file uploaded." });
		}
		if (!AllowedContentTypes.Contains(file.ContentType))
		{
			return StatusCode(StatusCodes.Status415UnsupportedMediaType,
				new { error = $"Content type '{file.ContentType}' is not allowed. Allowed: {string.Join(", ", AllowedContentTypes)}" });
		}

		// Never default a missing identity to God (#1): reject so uploads cannot be
		// misattributed or bypass identity-based controls downstream.
		var uploaderDbref = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(uploaderDbref))
			return Unauthorized("Missing character identity.");
		await using var content = file.OpenReadStream();
		var saved = await assetService.SaveAsync(file.FileName, file.ContentType, content, uploaderDbref, ct);
		if (saved.IsT1)
		{
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = saved.AsT1.Value });
		}

		var asset = saved.AsT0;
		var entries = (await ReadGalleryAsync(character)).ToList();
		entries.Add(new GalleryEntry(
			AssetId: asset.Id,
			FileName: asset.FileName,
			Url: $"/api/wiki-assets/{asset.Id}/{asset.FileName}",
			Caption: null,
			Order: entries.Count == 0 ? 0 : entries.Max(e => e.Order) + 1,
			IsIcon: entries.Count == 0)); // first image becomes the icon by default

		var write = await WriteGalleryAsync(character, entries);
		if (write.IsT1) return StatusCode(StatusCodes.Status500InternalServerError, write.AsT1.Value);
		logger.LogInformation("Gallery image added to {Character}: asset={Asset} by={Uploader}", LogSanitizer.Sanitize(name), asset.Id, LogSanitizer.Sanitize(uploaderDbref));
		return Ok(entries.OrderBy(e => e.Order).ToList());
	}

	[HttpPut]
	[Authorize]
	public async Task<IActionResult> Replace(string name, [FromBody] List<GalleryEntry> entries, CancellationToken ct)
	{
		var (character, allowed) = await ResolveAndAuthorizeAsync(name, ct);
		if (character is null) return NotFound();
		if (!allowed) return Forbid();

		// Keep only entries whose assets still exist in this character's gallery, and enforce a single icon.
		var existing = await ReadGalleryAsync(character);
		var validIds = existing.Select(e => e.AssetId).ToHashSet(StringComparer.Ordinal);
		var sanitized = entries
			.Where(e => validIds.Contains(e.AssetId))
			.Select((e, i) => e with { Order = i })
			.ToList();

		var iconSeen = false;
		for (var i = 0; i < sanitized.Count; i++)
		{
			if (sanitized[i].IsIcon && !iconSeen) { iconSeen = true; }
			else if (sanitized[i].IsIcon) { sanitized[i] = sanitized[i] with { IsIcon = false }; }
		}

		var write = await WriteGalleryAsync(character, sanitized);
		if (write.IsT1) return StatusCode(StatusCodes.Status500InternalServerError, write.AsT1.Value);
		return Ok(sanitized);
	}

	[HttpDelete("{assetId}")]
	[Authorize]
	public async Task<IActionResult> Delete(string name, string assetId, CancellationToken ct)
	{
		var (character, allowed) = await ResolveAndAuthorizeAsync(name, ct);
		if (character is null) return NotFound();
		if (!allowed) return Forbid();

		var entries = (await ReadGalleryAsync(character)).ToList();
		var removed = entries.RemoveAll(e => e.AssetId == assetId) > 0;
		if (!removed)
		{
			return NotFound();
		}

		// If we removed the icon, promote the new first image.
		if (entries.Count > 0 && entries.All(e => !e.IsIcon))
		{
			var first = entries.OrderBy(e => e.Order).First();
			entries[entries.IndexOf(first)] = first with { IsIcon = true };
		}

		var write = await WriteGalleryAsync(character, entries);
		if (write.IsT1) return StatusCode(StatusCodes.Status500InternalServerError, write.AsT1.Value);
		await assetService.DeleteAsync(assetId);
		return Ok(entries.OrderBy(e => e.Order).ToList());
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private async Task<AnySharpObject?> ResolveCharacterAsync(string name, CancellationToken ct)
	{
		await foreach (var player in mediator.CreateStream(new GetPlayerQuery(name)).WithCancellation(ct))
		{
			return new AnySharpObject(player);
		}
		return null;
	}

	private async Task<(AnySharpObject? Character, bool Allowed)> ResolveAndAuthorizeAsync(string name, CancellationToken ct)
	{
		var character = await ResolveCharacterAsync(name, ct);
		if (character is null) return (null, false);

		var viewer = await ResolveViewerAsync(ct);
		if (viewer is null) return (character, false);

		var allowed = await permissionService.Controls(viewer, character);
		return (character, allowed);
	}

	private async Task<AnySharpObject?> ResolveViewerAsync(CancellationToken ct)
	{
		var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(raw)) return null;

		var numberPart = raw.TrimStart('#').Split(':', 2)[0];
		if (!int.TryParse(numberPart, out var number)) return null;

		var result = await mediator.Send(new GetObjectNodeQuery(new DBRef(number, null)), ct);
		return result.IsNone ? null : result.Known;
	}

	private async Task<IReadOnlyList<GalleryEntry>> ReadGalleryAsync(AnySharpObject character)
	{
		var result = await attributeService.GetAttributeAsync(
			character, character, GalleryAttribute, IAttributeService.AttributeMode.Read, parent: false);
		if (!result.IsAttribute)
		{
			return [];
		}

		var json = result.AsAttribute.Last().Value.ToString();
		if (string.IsNullOrWhiteSpace(json))
		{
			return [];
		}

		try
		{
			return JsonSerializer.Deserialize<List<GalleryEntry>>(json,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
		}
		catch (JsonException ex)
		{
			logger.LogWarning(ex, "Corrupt PROFILE`GALLERY on {Dbref}; treating as empty.", character.Object().DBRef);
			return [];
		}
	}

	private async Task<OneOf<Success, Error<string>>> WriteGalleryAsync(AnySharpObject character, IReadOnlyList<GalleryEntry> entries)
	{
		var json = JsonSerializer.Serialize(entries);
		return await attributeService.SetAttributeAsync(character, character, GalleryAttribute, MModule.single(json));
	}
}
