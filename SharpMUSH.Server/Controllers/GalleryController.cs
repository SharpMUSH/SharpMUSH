using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Logging;
using SharpMUSH.Server.Services;
using MarkupString;
using SharpMUSH.Server.Authentication;
using System.Text.Json;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Character profile gallery. Image bytes are stored by <see cref="IWikiAssetService"/> (the shared
/// file store); gallery composition (order, captions, icon, banner) is kept as a <c>PROFILE`GALLERY</c>
/// JSON attribute on the character — backend-agnostic, no extra DB schema. Every write also mirrors the
/// icon, the banner and the icon's caption into the standard <c>IMAGE</c>, <c>IMAGE`BANNER</c> and
/// <c>IMAGE`ALT</c> attributes (spec §2), so softcode and OOB payloads read the same picture. Edits
/// require the requester to control the character (owner) or be staff (Wizard/Royalty), enforced by
/// <see cref="IPermissionService"/>.
///
/// Routes:
///   GET    /api/profile/{name}/gallery          — list gallery entries (anonymous)
///   POST   /api/profile/{name}/gallery          — upload an image (owner/staff)
///   PUT    /api/profile/{name}/gallery          — replace order/captions/icon/banner (owner/staff)
///   DELETE /api/profile/{name}/gallery/{assetId} — remove an image (owner/staff)
/// </summary>
[ApiController]
[Route("api/profile/{name}/gallery")]
public class GalleryController(
	IWikiAssetService assetService,
	IMediator mediator,
	IAttributeService attributeService,
	IPermissionService permissionService,
	IVisibleWorldProjection projection,
	ILogger<GalleryController> logger) : ControllerBase
{
	private const string GalleryAttribute = "PROFILE`GALLERY";

	private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/png", "image/jpeg", "image/gif", "image/webp"
	};

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
		var uploaderDbref = User.GetActingCharacter()?.ToString();
		if (string.IsNullOrEmpty(uploaderDbref))
			return Unauthorized("Missing character identity.");
		await using var content = file.OpenReadStream();
		return await assetService.SaveAsync(file.FileName, file.ContentType, content, uploaderDbref, ct) switch
		{
			WikiAsset asset => await AddToGalleryAsync(name, character, asset, uploaderDbref),
			Error<string> saveError => StatusCode(StatusCodes.Status500InternalServerError, new { error = saveError.Value }),
		};
	}

	/// <summary>
	/// Append a stored image to the character's gallery, as its icon when it is the first.
	/// </summary>
	private async Task<IActionResult> AddToGalleryAsync(string name, AnySharpObject character, WikiAsset asset, string uploaderDbref)
	{
		var entries = (await ReadGalleryAsync(character)).ToList();
		entries.Add(new GalleryEntry(
			AssetId: asset.Id,
			FileName: asset.FileName,
			Url: $"/api/wiki-assets/{asset.Id}/{asset.FileName}",
			Caption: null,
			Order: entries.Count == 0 ? 0 : entries.Max(e => e.Order) + 1,
			IsIcon: entries.Count == 0));

		var normalized = GalleryRules.Normalize(entries);
		var write = await WriteGalleryAsync(character, normalized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		logger.LogInformation("Gallery image added to {Character}: asset={Asset} by={Uploader}", LogSanitizer.Sanitize(name), asset.Id, LogSanitizer.Sanitize(uploaderDbref));
		return Ok(normalized);
	}

	[HttpPut]
	[Authorize]
	public async Task<IActionResult> Replace(string name, [FromBody] List<GalleryEntry> entries, CancellationToken ct)
	{
		var (character, allowed) = await ResolveAndAuthorizeAsync(name, ct);
		if (character is null) return NotFound();
		if (!allowed) return Forbid();

		// Keep only entries whose assets still exist in this character's gallery, each once, with the
		// stored file name and URL (a client names the order, captions and flags, never where a file is),
		// in the order sent; then one icon and at most one banner.
		var existing = (await ReadGalleryAsync(character)).ToDictionary(e => e.AssetId, StringComparer.Ordinal);
		if (existing.Count == 0)
		{
			// Nothing stored and so nothing a client could name: writing would only clear a hand-set IMAGE.
			return Ok(Array.Empty<GalleryEntry>());
		}

		var sanitized = GalleryRules.Normalize(entries
			.Where(e => existing.ContainsKey(e.AssetId))
			.DistinctBy(e => e.AssetId)
			.Select((e, i) => existing[e.AssetId] with { Caption = e.Caption, Order = i, IsIcon = e.IsIcon, IsBanner = e.IsBanner }));

		var write = await WriteGalleryAsync(character, sanitized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
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

		// Removing the icon promotes the new first image; removing the banner leaves none.
		var normalized = GalleryRules.Normalize(entries);
		var write = await WriteGalleryAsync(character, normalized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		await assetService.DeleteAsync(assetId);
		return Ok(normalized);
	}

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

	/// <summary>
	/// The character whose control over the profile is being tested, re-checked against its current
	/// account link. Editing somebody's gallery turns on this answer, so it uses the shared rule.
	/// </summary>
	private ValueTask<AnySharpObject?> ResolveViewerAsync(CancellationToken ct) =>
		User.ResolveExecutorAsync(projection, ct);

	private async Task<IReadOnlyList<GalleryEntry>> ReadGalleryAsync(AnySharpObject character)
	{
		var result = await attributeService.GetAttributeAsync(
			character, character, GalleryAttribute, IAttributeService.AttributeMode.Read, parent: false);
		if (result is not SharpAttribute[] gallery)
		{
			return [];
		}

		var json = gallery.Last().Value.ToString();
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

	/// <summary>
	/// Writes the gallery, then mirrors it into the standard image attributes. A mirror that fails is
	/// reported like a failed gallery write: the portal and softcode would otherwise disagree about
	/// which picture is the character's.
	/// </summary>
	private async Task<Result<Success>> WriteGalleryAsync(AnySharpObject character, IReadOnlyList<GalleryEntry> entries)
	{
		var json = JsonSerializer.Serialize(entries);
		if (await attributeService.SetAttributeAsync(character, character, GalleryAttribute, MarkupText.Plain(json)) is Error<string> failed)
		{
			return failed;
		}

		// Parents before children: sets run IMAGE first, clears run it last, so a branch attribute is
		// never written under a missing parent or cleared out from under a child.
		var mirror = GalleryRules.Mirror(entries);
		(string Attribute, string Value)[] mirrored = [("IMAGE", mirror.Image), ("IMAGE`BANNER", mirror.Banner), ("IMAGE`ALT", mirror.Alt)];
		var sets = mirrored.Where(m => m.Value.Length > 0);
		var clears = mirrored.Where(m => m.Value.Length == 0).Reverse();
		foreach (var (attribute, value) in sets.Concat(clears))
		{
			var result = value.Length == 0
				? await attributeService.ClearAttributeAsync(character, character, attribute, IAttributeService.AttributePatternMode.Exact)
				: await attributeService.SetAttributeAsync(character, character, attribute, MarkupText.Plain(value));
			if (result is Error<string> error)
			{
				logger.LogWarning("Mirroring the gallery into {Attribute} on {Dbref} failed: {Error}", attribute, character.Object().DBRef, error.Value);
				return error;
			}
		}

		return new Success();
	}
}
