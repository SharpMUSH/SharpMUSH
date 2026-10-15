using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Logging;
using SharpMUSH.Server.Services;
using SharpMUSH.Server.Authentication;
using System.Text.Json;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Character profile gallery. Image bytes are stored by <see cref="IWikiAssetService"/> (the shared
/// file store); gallery composition (order, captions, icon, banner) is kept as a <c>PROFILE`GALLERY</c>
/// JSON attribute on the character — backend-agnostic, no extra DB schema. Every write also mirrors the
/// icon, the banner and the icon's caption into the standard <c>IMAGE</c>, <c>IMAGE`BANNER</c> and
/// <c>IMAGE`ALT</c> attributes (spec §2), so softcode and OOB payloads read the same picture. Edits
/// require <c>wiki.edit</c>, as the biography beside the gallery does, and then the requester's account to
/// own the character, whichever of its characters is acting, or the acting character to control it
/// (staff), enforced by <see cref="IPermissionService"/>. An upload fires <c>GALLERY`ADD</c> and a report
/// <c>GALLERY`REPORT</c> on the event handler, so the game decides what a new picture or a report does.
///
/// Routes:
///   GET    /api/profile/{name}/gallery          — list gallery entries (anonymous)
///   POST   /api/profile/{name}/gallery          — upload an image (wiki.edit; owner/staff); <c>?use=banner</c> or
///                                                 <c>?use=avatar</c> makes it the banner or the avatar
///   PUT    /api/profile/{name}/gallery          — replace order/captions/tags/warnings/icon/banner (wiki.edit; owner/staff)
///   POST   /api/profile/{name}/gallery/library — add a file from the shared image library (wiki.edit; owner/staff)
///   POST   /api/profile/{name}/gallery/{assetId}/share — put an uploaded image in the shared library (wiki.edit; owner/staff)
///   DELETE /api/profile/{name}/gallery/{assetId} — remove an image (wiki.edit; owner/staff); the file goes with it
///                                                 unless it is shared
///   POST   /api/profile/{name}/gallery/{assetId}/report — report an image to staff (signed in, playing a character)
///   GET    /api/gallery/reports                 — whether the game takes reports (anonymous)
/// </summary>
[ApiController]
[Route("api/profile/{name}/gallery")]
public class GalleryController(
	IWikiAssetService assetService,
	IMediator mediator,
	IAttributeService attributeService,
	IPermissionService permissionService,
	IVisibleWorldProjection projection,
	IAccountService accounts,
	IEventService events,
	IAdministrativeCapabilityService capabilities,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<GalleryController> logger) : ControllerBase
{
	private const string GalleryAttribute = GalleryWriter.GalleryAttribute;

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

		return await WithSharingAsync((await ReadGalleryAsync(character)).OrderBy(e => e.Order), ct);
	}

	/// <summary>The entries with <see cref="GalleryEntry.Shared"/> read from each file, as every answer carries them.</summary>
	private async Task<List<GalleryEntry>> WithSharingAsync(IEnumerable<GalleryEntry> entries, CancellationToken ct)
	{
		var shared = new List<GalleryEntry>();
		foreach (var entry in entries)
		{
			shared.Add(entry with { Shared = await assetService.FindAsync(entry.AssetId, ct) is WikiAsset { Private: false } });
		}
		return shared;
	}

	[HttpPost]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	[RequestSizeLimit(10_485_760)]
	public async Task<IActionResult> Upload(string name, IFormFile file, [FromQuery] string? use, CancellationToken ct)
	{
		if (ParseUse(use) is not { } role)
		{
			return BadRequest(new { error = $"'{use}' is not a use for an image. Use 'banner' or 'avatar'." });
		}

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

		// The declared type is the browser's word; the stored type is what the bytes are.
		if (await SniffAsync(file, ct) is not string contentType)
		{
			return StatusCode(StatusCodes.Status415UnsupportedMediaType,
				new { error = $"{file.FileName} is not a PNG, JPEG, GIF or WebP image." });
		}

		var limit = options.CurrentValue.Limit.MaxGalleryImages;
		if ((await ReadGalleryAsync(character)).Count >= limit)
		{
			return Conflict(new { error = $"A gallery holds at most {limit} images. Remove one before adding another." });
		}

		// Never default a missing identity to God (#1): reject so uploads cannot be
		// misattributed or bypass identity-based controls downstream.
		var uploaderDbref = User.GetActingCharacter()?.ToString();
		if (string.IsNullOrEmpty(uploaderDbref))
			return Unauthorized("Missing character identity.");
		// An upload belongs to this gallery until its owner shares it, so removing it deletes the file.
		await using var content = file.OpenReadStream();
		return await assetService.SaveAsync(file.FileName, contentType, content, uploaderDbref, isPrivate: true, ct) switch
		{
			WikiAsset asset => await AddToGalleryAsync(name, character, asset, role, uploaderDbref),
			Error<string> saveError => StatusCode(StatusCodes.Status500InternalServerError, new { error = saveError.Value }),
		};
	}

	/// <summary>The image type the file's first bytes name; not found for anything else.</summary>
	private static async Task<Found<string>> SniffAsync(IFormFile file, CancellationToken ct)
	{
		var head = new byte[GalleryRules.SniffLength];
		await using var stream = file.OpenReadStream();
		var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
		return GalleryRules.SniffImageType(head.AsSpan(0, read));
	}

	/// <summary>What an upload is for: a gallery image, the banner, or the avatar.</summary>
	private enum UploadRole { Gallery, Banner, Avatar }

	/// <summary>The role <c>?use=</c> names; null for a word that is not one.</summary>
	private static UploadRole? ParseUse(string? use) => use?.Trim().ToLowerInvariant() switch
	{
		null or "" => UploadRole.Gallery,
		"banner" => UploadRole.Banner,
		"avatar" => UploadRole.Avatar,
		_ => null,
	};

	/// <summary>
	/// Append a stored image to the character's gallery. Uploaded as the banner or the avatar, it takes
	/// that place from whichever image held it; a plain upload is the avatar only when it is the first.
	/// </summary>
	private async Task<IActionResult> AddToGalleryAsync(string name, AnySharpObject character, WikiAsset asset, UploadRole role, string uploaderDbref)
	{
		var entries = (await ReadGalleryAsync(character))
			.Select(e => e with
			{
				IsIcon = e.IsIcon && role != UploadRole.Avatar,
				IsBanner = e.IsBanner && role != UploadRole.Banner,
			})
			.ToList();
		entries.Add(new GalleryEntry(
			AssetId: asset.Id,
			FileName: asset.FileName,
			Url: $"/api/wiki-assets/{asset.Id}/{asset.FileName}",
			Caption: null,
			Order: entries.Count == 0 ? 0 : entries.Max(e => e.Order) + 1,
			IsIcon: role == UploadRole.Avatar || (role == UploadRole.Gallery && entries.Count == 0),
			IsBanner: role == UploadRole.Banner));

		var normalized = GalleryRules.Normalize(entries);
		var write = await WriteGalleryAsync(character, normalized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		logger.LogInformation("Gallery image added to {Character}: asset={Asset} by={Uploader}", LogSanitizer.Sanitize(name), asset.Id, LogSanitizer.Sanitize(uploaderDbref));
		var added = normalized.First(e => e.AssetId == asset.Id);
		await events.TriggerEventAsync(SharpEvents.GalleryAdd, User.GetActingCharacter(),
			character.Object().DBRef.ToString(), asset.Id, added.Url, role.ToString().ToLowerInvariant());
		return Ok(await WithSharingAsync(normalized, HttpContext.RequestAborted));
	}

	/// <summary>
	/// Adds a file from the shared image library to the gallery, without copying it: a picture already on
	/// the wiki or in another gallery is used, not uploaded again. Only a file the caller finds in the
	/// library (<see cref="WikiAssetLibrary.Finds"/>), and only an image a gallery takes.
	/// </summary>
	[HttpPost("library")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> AddFromLibrary(string name, [FromBody] GalleryLibraryPick pick, CancellationToken ct)
	{
		if (ParseUse(pick.Use) is not { } role)
			return BadRequest(new ApiErrorDto($"'{pick.Use}' is not a use for an image. Use 'banner' or 'avatar'."));

		var (character, allowed) = await ResolveAndAuthorizeAsync(name, ct);
		if (character is null) return NotFound();
		if (!allowed) return Forbid();

		var viewer = await LibraryViewer.ResolveAsync(User, capabilities, ct);
		if (await assetService.FindAsync(pick.AssetId, ct) is not WikiAsset asset
				|| !WikiAssetLibrary.Finds(asset, viewer.Scopes, viewer.Dbref))
			return NotFound(new ApiErrorDto("No such file in the image library."));
		if (!AllowedContentTypes.Contains(asset.ContentType))
			return StatusCode(StatusCodes.Status415UnsupportedMediaType,
				new ApiErrorDto($"{asset.FileName} is not a PNG, JPEG, GIF or WebP image."));

		var gallery = await ReadGalleryAsync(character);
		if (gallery.Any(e => e.AssetId == asset.Id))
			return Conflict(new ApiErrorDto($"{asset.FileName} is already in this gallery."));
		var limit = options.CurrentValue.Limit.MaxGalleryImages;
		if (gallery.Count >= limit)
			return Conflict(new ApiErrorDto($"A gallery holds at most {limit} images. Remove one before adding another."));
		if (viewer.Dbref is null)
			return Unauthorized("Missing character identity.");

		return await AddToGalleryAsync(name, character, asset, role, viewer.Dbref);
	}

	/// <summary>
	/// Puts an image uploaded to this gallery in the shared image library, so a wiki page or another gallery
	/// can use it. It cannot be taken back: once shared, removing it from this gallery keeps the file. An
	/// image with a content warning is refused, since the library would show it uncovered.
	/// </summary>
	[HttpPost("{assetId}/share")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> Share(string name, string assetId, CancellationToken ct)
	{
		var (character, allowed) = await ResolveAndAuthorizeAsync(name, ct);
		if (character is null) return NotFound();
		if (!allowed) return Forbid();
		var gallery = await ReadGalleryAsync(character);
		if (gallery.FirstOrDefault(e => e.AssetId == assetId) is not { } entry)
			return NotFound();
		// The library shows a picture as it is; a warning is this gallery's and would not cover it there.
		if (entry.Warning is not null)
			return Conflict(new ApiErrorDto("An image with a content warning stays out of the library. Take the warning off to share it."));
		if (await assetService.FindAsync(assetId, ct) is not WikiAsset asset)
			return NotFound();

		if (asset.Private && await assetService.SetSharingAsync(assetId, isPrivate: false, asset.Lock, ct) is NotFound _)
			return NotFound();
		logger.LogInformation("Gallery image shared on {Character}: asset={Asset}", LogSanitizer.Sanitize(name), assetId);
		return Ok(await WithSharingAsync(gallery.OrderBy(e => e.Order), ct));
	}

	[HttpPut]
	[Authorize(Policy = PortalPermission.WikiEdit)]
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

		var kept = new List<GalleryEntry>();
		foreach (var (sent, i) in entries.Where(e => existing.ContainsKey(e.AssetId)).DistinctBy(e => e.AssetId).Select((e, i) => (e, i)))
		{
			var named = existing[sent.AssetId] with
			{
				Caption = sent.Caption,
				Order = i,
				IsIcon = sent.IsIcon,
				IsBanner = sent.IsBanner,
				Tags = sent.Tags,
				Warning = sent.Warning,
			};
			switch (GalleryRules.Clean(named))
			{
				case GalleryEntry clean: kept.Add(clean); break;
				case Error<string> refused: return BadRequest(new ApiErrorDto(refused.Value));
			}
		}

		var sanitized = GalleryRules.Normalize(kept);
		var write = await WriteGalleryAsync(character, sanitized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		return Ok(await WithSharingAsync(sanitized, ct));
	}

	[HttpDelete("{assetId}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
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

		// Removing the icon promotes the first image that is not the banner; removing the banner leaves none.
		var normalized = GalleryRules.Normalize(entries);
		var write = await WriteGalleryAsync(character, normalized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		// A shared file may be on a wiki page or in another gallery; only one kept to this gallery goes.
		if (await assetService.FindAsync(assetId, ct) is WikiAsset { Private: true })
			await assetService.DeleteAsync(assetId);
		return Ok(await WithSharingAsync(normalized, ct));
	}

	/// <summary>
	/// Whether the game takes reports: the event handler has a <c>GALLERY`REPORT</c> attribute. The portal
	/// offers Report only then, so a report never goes nowhere.
	/// </summary>
	[HttpGet("~/api/gallery/reports")]
	[AllowAnonymous]
	public async Task<GalleryReporting> Reporting() => new(await events.HasHandlerAsync(SharpEvents.GalleryReport));

	/// <summary>
	/// Reports an image to the game's staff: fires <c>GALLERY`REPORT</c> with the reporter's character as
	/// %#. SharpMUSH keeps no report of its own; the handler decides what one does (open a job, tell a
	/// channel).
	/// </summary>
	[HttpPost("{assetId}/report")]
	[Authorize]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Report(string name, string assetId, [FromBody] GalleryReportRequest request, CancellationToken ct)
	{
		var reason = request.Reason?.Trim() ?? "";
		if (reason.Length == 0)
			return BadRequest(new ApiErrorDto("Say what is wrong with the image."));
		if (reason.Length > GalleryRules.MaxReportLength)
			return BadRequest(new ApiErrorDto($"Keep a report to {GalleryRules.MaxReportLength} characters."));
		if (await ResolveViewerAsync(ct) is not { } reporter)
			return Conflict(new ApiErrorDto("Choose a character to act as before reporting an image."));
		if (await ResolveCharacterAsync(name, ct) is not { } character)
			return NotFound();
		if ((await ReadGalleryAsync(character)).FirstOrDefault(e => e.AssetId == assetId) is not { } entry)
			return NotFound();
		if (!await events.HasHandlerAsync(SharpEvents.GalleryReport))
			return StatusCode(StatusCodes.Status501NotImplemented, new ApiErrorDto("This game does not take reports of images."));

		await events.TriggerEventAsync(SharpEvents.GalleryReport, reporter.Object().DBRef,
			character.Object().DBRef.ToString(), entry.AssetId, entry.Url, reason, entry.Caption ?? "");
		logger.LogInformation("Gallery image reported on {Character}: asset={Asset} by={Reporter}",
			LogSanitizer.Sanitize(name), entry.AssetId, reporter.Object().DBRef);
		return NoContent();
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

		// A character on the caller's own account is theirs to edit, whichever character they are playing;
		// the wiki.edit policy on every write has already been met.
		if (AccountSessionAuthenticationHandler.TryGetAccount(User, out var accountId, out _)
				&& await accounts.GetAccountForCharacterAsync(character.Object().DBRef, ct) is { Id: { Length: > 0 } owner }
				&& string.Equals(owner, accountId, StringComparison.Ordinal))
			return (character, true);

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
	/// Writes the gallery and mirrors it into the standard image attributes, all or nothing
	/// (<see cref="GalleryWriter"/>): a failed mirror puts the gallery back, since the portal and softcode
	/// would otherwise disagree about which picture is the character's.
	/// </summary>
	private Task<Result<Success>> WriteGalleryAsync(AnySharpObject character, IReadOnlyList<GalleryEntry> entries) =>
		GalleryWriter.WriteAsync(new CharacterAttributes(attributeService, character), entries, logger);

	/// <summary>The character's own attributes, read and written as itself.</summary>
	private sealed class CharacterAttributes(IAttributeService attributes, AnySharpObject character) : GalleryWriter.IStore
	{
		public async ValueTask<MString?> ReadAsync(string attribute) =>
			await attributes.GetAttributeAsync(character, character, attribute, IAttributeService.AttributeMode.Read, parent: false)
				is SharpAttribute[] { Length: > 0 } path
				? path[^1].Value
				: null;

		public ValueTask<Result<Success>> SetAsync(string attribute, MString value) =>
			attributes.SetAttributeAsync(character, character, attribute, value);

		public ValueTask<Result<Success>> ClearAsync(string attribute) =>
			attributes.ClearAttributeAsync(character, character, attribute, IAttributeService.AttributePatternMode.Exact);
	}
}
