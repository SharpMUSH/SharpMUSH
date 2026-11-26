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
/// The game's <see cref="GalleryOptions"/> moderate this, each off by default: terms every upload accepts and keeps
/// (<c>gallery_consent_text</c>), a byte limit beside the image count that may leave official art out, portraits that
/// wait for staff (<c>gallery_portrait_approval</c>; <c>GALLERY`SUBMIT</c>, <c>GALLERY`PORTRAIT</c>), and a lock that
/// leaves every change to staff (<c>gallery_staff_only</c>). Staff here are <c>media.admin</c> holders.
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
///   GET    /api/gallery/policy                  — the game's gallery rules, and whether the caller is staff (anonymous)
///   GET    /api/gallery/portraits               — portraits waiting for approval (media.admin)
///   POST   /api/profile/{name}/gallery/{assetId}/portrait — make an image the portrait, as official art (media.admin)
///   DELETE /api/profile/{name}/gallery/{assetId}/portrait — decline a waiting portrait (media.admin)
///   DELETE /api/profile/{name}/gallery/portrait — clear the portrait back to the default (media.admin; approval on)
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
	IAuthorizationService authorization,
	TimeProvider time,
	ILogger<GalleryController> logger) : ControllerBase
{
	/// <summary>The game's gallery moderation options; a world saved before they existed reads them as off.</summary>
	private GalleryOptions Moderation => options.CurrentValue.Gallery ?? new();

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

		// Which image a player asked for is between them and staff.
		var gallery = (await ReadGalleryAsync(character)).OrderBy(e => e.Order).ToList();
		var told = !gallery.Any(e => e.PortraitPending)
			|| await IsStaffAsync()
			|| (User.Identity?.IsAuthenticated == true && await MayEditAsync(character, ct));
		return await WithSharingAsync(told ? gallery : gallery.Select(e => e with { PortraitPending = false }), ct);
	}

	/// <summary>
	/// The entries with <see cref="GalleryEntry.Shared"/> read from each file, as every answer carries them. The
	/// terms an uploader accepted are staff's record, so only staff are given them.
	/// </summary>
	private async Task<List<GalleryEntry>> WithSharingAsync(IEnumerable<GalleryEntry> entries, CancellationToken ct)
	{
		var staff = await IsStaffAsync();
		var shared = new List<GalleryEntry>();
		foreach (var entry in entries)
		{
			shared.Add(entry with
			{
				Shared = await assetService.FindAsync(entry.AssetId, ct) is WikiAsset { Private: false },
				Consent = staff && entry.Consent is { } consent ? consent with { ByName = await NameOfAsync(consent.By) } : null,
			});
		}
		return shared;
	}

	/// <summary>The current name of the object an objid names, or null when it is gone or the number was reused.</summary>
	private async Task<string?> NameOfAsync(string objid) =>
		DBRef.TryParse(objid, out var parsed) && parsed is { } dbref
		&& await mediator.Send(new GetObjectNodeQuery(dbref)) is AnySharpObject found
		&& found.Object().DBRef.Matches(dbref)
			? found.Object().Name
			: null;

	/// <summary>Whether the caller is gallery staff: holds <c>media.admin</c>.</summary>
	private async Task<bool> IsStaffAsync() =>
		User.Identity?.IsAuthenticated == true
		&& (await authorization.AuthorizeAsync(User, PortalPermission.MediaAdmin)).Succeeded;

	[HttpPost]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	[RequestSizeLimit(10_485_760)]
	public async Task<IActionResult> Upload(string name, IFormFile file, [FromQuery] string? use, [FromQuery] string? consent, CancellationToken ct)
	{
		if (ParseUse(use) is not { } role)
		{
			return BadRequest(new { error = $"'{use}' is not a use for an image. Use 'banner' or 'avatar'." });
		}

		if (await ResolveAccessAsync(name, ct) is not { } access) return NotFound();
		if (!access.MayWrite) return Refused();
		var character = access.Character;

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

		// The terms are checked against the wording the uploader was shown, so a change made while they read is
		// never recorded as accepted.
		var terms = Moderation.ConsentText;
		if (ConsentRefusal(terms, consent) is { } unaccepted)
			return unaccepted;

		if (await OverLimitAsync(await ReadGalleryAsync(character), file.Length, ct) is { } full)
		{
			return Conflict(new ApiErrorDto(full));
		}

		// Never default a missing identity to God (#1): reject so uploads cannot be
		// misattributed or bypass identity-based controls downstream.
		var uploaderDbref = User.GetActingCharacter()?.ToString();
		if (string.IsNullOrEmpty(uploaderDbref))
			return Unauthorized("Missing character identity.");
		var accepted = terms.Length > 0 ? new GalleryConsent(terms, uploaderDbref, time.GetUtcNow()) : null;
		// An upload belongs to this gallery until its owner shares it, so removing it deletes the file.
		await using var content = file.OpenReadStream();
		return await assetService.SaveAsync(file.FileName, contentType, content, uploaderDbref, isPrivate: true, ct) switch
		{
			WikiAsset asset => await AddToGalleryAsync(name, access, asset, role, uploaderDbref, accepted),
			Error<string> saveError => StatusCode(StatusCodes.Status500InternalServerError, new { error = saveError.Value }),
		};
	}

	/// <summary>
	/// Why one more image of <paramref name="adding"/> bytes does not fit the gallery, or null when it does. The
	/// files' sizes are read only when the game sets a byte limit.
	/// </summary>
	private async Task<string?> OverLimitAsync(IReadOnlyList<GalleryEntry> gallery, long adding, CancellationToken ct)
	{
		var moderation = Moderation;
		var sized = new List<(GalleryEntry, long)>();
		foreach (var entry in gallery)
		{
			var bytes = moderation.MaxGalleryMegabytes > 0 && await assetService.FindAsync(entry.AssetId, ct) is WikiAsset asset
				? asset.SizeBytes
				: 0;
			sized.Add((entry, bytes));
		}
		return GalleryRules.OverLimit(sized, adding, options.CurrentValue.Limit.MaxGalleryImages,
			moderation.MaxGalleryMegabytes, moderation.LimitSkipsOfficial);
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
	/// While portraits wait for approval, a player's avatar upload waits instead, and no image becomes the
	/// avatar by being first.
	/// </summary>
	private async Task<IActionResult> AddToGalleryAsync(string name, Access access, WikiAsset asset, UploadRole role, string uploaderDbref, GalleryConsent? consent = null)
	{
		var character = access.Character;
		var approval = Moderation.PortraitApproval;
		var submits = role == UploadRole.Avatar && approval && !access.IsStaff;
		var takesIcon = role == UploadRole.Avatar && !submits;
		var entries = (await ReadGalleryAsync(character))
			.Select(e => e with
			{
				IsIcon = e.IsIcon && !takesIcon,
				IsBanner = e.IsBanner && role != UploadRole.Banner,
				PortraitPending = e.PortraitPending && !submits,
			})
			.ToList();
		entries.Add(new GalleryEntry(
			AssetId: asset.Id,
			FileName: asset.FileName,
			Url: $"/api/wiki-assets/{asset.Id}/{asset.FileName}",
			Caption: null,
			Order: entries.Count == 0 ? 0 : entries.Max(e => e.Order) + 1,
			IsIcon: takesIcon || (!approval && role == UploadRole.Gallery && entries.Count == 0),
			IsBanner: role == UploadRole.Banner)
		{
			Consent = consent,
			PortraitPending = submits,
		});

		var normalized = GalleryRules.Normalize(entries, autoIcon: !approval);
		var write = await WriteGalleryAsync(character, normalized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		logger.LogInformation("Gallery image added to {Character}: asset={Asset} by={Uploader}", LogSanitizer.Sanitize(name), asset.Id, LogSanitizer.Sanitize(uploaderDbref));
		var added = normalized.First(e => e.AssetId == asset.Id);
		await events.TriggerEventAsync(SharpEvents.GalleryAdd, User.GetActingCharacter(),
			character.Object().DBRef.ToString(), asset.Id, added.Url, role.ToString().ToLowerInvariant());
		if (added.PortraitPending)
		{
			await SubmittedAsync(name, character, added);
		}
		return Ok(await WithSharingAsync(normalized, HttpContext.RequestAborted));
	}

	/// <summary>Tells the game a portrait waits for staff: <c>GALLERY`SUBMIT</c>.</summary>
	private async Task SubmittedAsync(string name, AnySharpObject character, GalleryEntry submitted)
	{
		logger.LogInformation("Portrait submitted for {Character}: asset={Asset}", LogSanitizer.Sanitize(name), submitted.AssetId);
		await events.TriggerEventAsync(SharpEvents.GallerySubmit, User.GetActingCharacter(),
			character.Object().DBRef.ToString(), submitted.AssetId, submitted.Url);
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

		if (await ResolveAccessAsync(name, ct) is not { } access) return NotFound();
		if (!access.MayWrite) return Refused();
		var character = access.Character;
		var terms = Moderation.ConsentText;
		if (ConsentRefusal(terms, pick.Consent) is { } unaccepted)
			return unaccepted;

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
		if (await OverLimitAsync(gallery, asset.SizeBytes, ct) is { } full)
			return Conflict(new ApiErrorDto(full));
		if (viewer.Dbref is null)
			return Unauthorized("Missing character identity.");

		var accepted = terms.Length > 0 ? new GalleryConsent(terms, viewer.Dbref, time.GetUtcNow()) : null;
		return await AddToGalleryAsync(name, access, asset, role, viewer.Dbref, accepted);
	}

	/// <summary>
	/// Refuses an addition made without the game's gallery terms, or under wording older than the current one, so a
	/// change made while the uploader read is never recorded as accepted. Null when the terms are off or accepted.
	/// </summary>
	private ObjectResult? ConsentRefusal(string terms, string? consent) =>
		terms.Length == 0 ? null
		: string.IsNullOrEmpty(consent) ? Conflict(new ApiErrorDto("Accept the gallery terms before uploading."))
		: consent != GalleryRules.ConsentVersion(terms)
			? Conflict(new ApiErrorDto("The gallery terms changed after you read them. Read them again before uploading."))
		: null;

	/// <summary>
	/// Puts an image uploaded to this gallery in the shared image library, so a wiki page or another gallery
	/// can use it. It cannot be taken back: once shared, removing it from this gallery keeps the file. An
	/// image with a content warning is refused, since the library would show it uncovered.
	/// </summary>
	[HttpPost("{assetId}/share")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> Share(string name, string assetId, CancellationToken ct)
	{
		if (await ResolveAccessAsync(name, ct) is not { } access) return NotFound();
		if (!access.MayWrite) return Refused();
		var character = access.Character;
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
		if (await ResolveAccessAsync(name, ct) is not { } access) return NotFound();
		if (!access.MayWrite) return Refused();
		var character = access.Character;
		// While portraits wait for approval, a player choosing another avatar asks for it instead; only staff set
		// the avatar or mark official art.
		var asks = Moderation.PortraitApproval && !access.IsStaff;

		// Keep only entries whose assets still exist in this character's gallery, each once, with the
		// stored file name and URL (a client names the order, captions and flags, never where a file is),
		// in the order sent; then one icon and at most one banner.
		var existing = (await ReadGalleryAsync(character)).ToDictionary(e => e.AssetId, StringComparer.Ordinal);
		if (existing.Count == 0)
		{
			// Nothing stored and so nothing a client could name: writing would only clear a hand-set IMAGE.
			return Ok(Array.Empty<GalleryEntry>());
		}

		// A new choice of avatar is the player's request and replaces any earlier one, whatever order they sit in.
		var asked = asks
			? entries.FirstOrDefault(e => e.IsIcon && existing.TryGetValue(e.AssetId, out var held) && !held.IsIcon)?.AssetId
			: null;
		var kept = new List<GalleryEntry>();
		foreach (var (sent, i) in entries.Where(e => existing.ContainsKey(e.AssetId)).DistinctBy(e => e.AssetId).Select((e, i) => (e, i)))
		{
			var stored = existing[sent.AssetId];
			var named = stored with
			{
				Caption = sent.Caption,
				Order = i,
				IsIcon = asks ? stored.IsIcon : sent.IsIcon,
				IsBanner = sent.IsBanner,
				Tags = sent.Tags,
				Warning = sent.Warning,
				Official = access.IsStaff ? sent.Official : stored.Official,
				// A waiting request can be withdrawn by the client, and only a new choice of avatar makes one.
				PortraitPending = asked is not null
					? sent.AssetId == asked
					: stored.PortraitPending && sent.PortraitPending,
			};
			switch (GalleryRules.Clean(named))
			{
				case GalleryEntry clean: kept.Add(clean); break;
				case Error<string> refused: return BadRequest(new ApiErrorDto(refused.Value));
			}
		}

		var sanitized = GalleryRules.Normalize(kept, autoIcon: !Moderation.PortraitApproval);
		var write = await WriteGalleryAsync(character, sanitized);
		if (write is Error<string> error) return StatusCode(StatusCodes.Status500InternalServerError, error.Value);
		if (sanitized.FirstOrDefault(e => e.PortraitPending) is { } waiting && !existing[waiting.AssetId].PortraitPending)
		{
			await SubmittedAsync(name, character, waiting);
		}
		return Ok(await WithSharingAsync(sanitized, ct));
	}

	[HttpDelete("{assetId}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> Delete(string name, string assetId, CancellationToken ct)
	{
		if (await ResolveAccessAsync(name, ct) is not { } access) return NotFound();
		if (!access.MayWrite) return Refused();
		var character = access.Character;

		var entries = (await ReadGalleryAsync(character)).ToList();
		var removed = entries.RemoveAll(e => e.AssetId == assetId) > 0;
		if (!removed)
		{
			return NotFound();
		}

		// Removing the icon promotes the first image that is not the banner, unless staff choose portraits;
		// removing the banner leaves none.
		var normalized = GalleryRules.Normalize(entries, autoIcon: !Moderation.PortraitApproval);
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

	/// <summary>The game's gallery rules, as the portal needs them before a write, and whether the caller is staff.</summary>
	[HttpGet("~/api/gallery/policy")]
	[AllowAnonymous]
	public async Task<GalleryPolicy> Policy()
	{
		var moderation = Moderation;
		var terms = moderation.ConsentText is { Length: > 0 } text ? text : null;
		return new GalleryPolicy(
			ConsentText: terms,
			ConsentVersion: terms is null ? null : GalleryRules.ConsentVersion(terms),
			PortraitApproval: moderation.PortraitApproval,
			StaffOnly: moderation.StaffOnly,
			MaxImages: (int)Math.Min(options.CurrentValue.Limit.MaxGalleryImages, int.MaxValue),
			MaxBytes: moderation.MaxGalleryMegabytes == 0 ? null : (long)moderation.MaxGalleryMegabytes * 1_048_576,
			OfficialExempt: moderation.LimitSkipsOfficial,
			IsStaff: await IsStaffAsync());
	}

	/// <summary>
	/// Every portrait waiting for staff, by character name: one per character, with the portrait it would replace.
	/// Read from each character's gallery, since the gallery is the only record of a submission.
	/// </summary>
	[HttpGet("~/api/gallery/portraits")]
	[Authorize(Policy = PortalPermission.MediaAdmin)]
	public async Task<IReadOnlyList<GalleryPortraitSubmission>> Portraits(CancellationToken ct)
	{
		// Requests left from before approval was turned off are not acted on, so they are not listed.
		if (!Moderation.PortraitApproval) return [];
		var waiting = new List<GalleryPortraitSubmission>();
		await foreach (var player in mediator.CreateStream(new GetAllPlayersQuery()).WithCancellation(ct))
		{
			var gallery = await ReadGalleryAsync(new AnySharpObject(player));
			if (gallery.FirstOrDefault(e => e.PortraitPending) is not { } submitted) continue;
			waiting.Add(new GalleryPortraitSubmission(player.Object.Name, player.Object.DBRef.ToString(), submitted,
				gallery.FirstOrDefault(e => e.IsIcon)));
		}
		return [.. waiting.OrderBy(w => w.Character, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>
	/// Staff make an image the character's portrait and mark it official art: a waiting submission, or any image in
	/// the gallery. Any other waiting submission is settled with it.
	/// </summary>
	[HttpPost("{assetId}/portrait")]
	[Authorize(Policy = PortalPermission.MediaAdmin)]
	public async Task<IActionResult> ApprovePortrait(string name, string assetId, CancellationToken ct)
	{
		if (await ResolveCharacterAsync(name, ct) is not { } character) return NotFound();
		var gallery = await ReadGalleryAsync(character);
		if (gallery.FirstOrDefault(e => e.AssetId == assetId) is not { } chosen) return NotFound();
		if (chosen.Warning is not null)
			return Conflict(new ApiErrorDto("An image with a content warning can't be the portrait. Take the warning off first."));

		return await DecidedAsync(name, character, "approve", chosen, gallery.Select(e => e with
		{
			IsIcon = e.AssetId == assetId,
			Official = e.Official || e.AssetId == assetId,
			PortraitPending = false,
		}), ct);
	}

	/// <summary>Staff turn down a waiting portrait. The image stays in the gallery; the portrait does not change.</summary>
	[HttpDelete("{assetId}/portrait")]
	[Authorize(Policy = PortalPermission.MediaAdmin)]
	public async Task<IActionResult> DeclinePortrait(string name, string assetId, CancellationToken ct)
	{
		if (await ResolveCharacterAsync(name, ct) is not { } character) return NotFound();
		var gallery = await ReadGalleryAsync(character);
		if (gallery.FirstOrDefault(e => e.AssetId == assetId) is not { } declined) return NotFound();
		if (!declined.PortraitPending)
			return Conflict(new ApiErrorDto("That image is not waiting to be the portrait."));

		return await DecidedAsync(name, character, "decline", declined,
			gallery.Select(e => e with { PortraitPending = e.PortraitPending && e.AssetId != assetId }), ct);
	}

	/// <summary>
	/// Staff take the portrait away, so the profile shows the default until they approve another. Only while
	/// portraits wait for approval: otherwise the gallery picks its first image again at once.
	/// </summary>
	[HttpDelete("portrait")]
	[Authorize(Policy = PortalPermission.MediaAdmin)]
	public async Task<IActionResult> ClearPortrait(string name, CancellationToken ct)
	{
		if (!Moderation.PortraitApproval)
			return Conflict(new ApiErrorDto("Players choose their own portraits in this game, so a cleared one would come straight back. Turn on gallery_portrait_approval first."));
		if (await ResolveCharacterAsync(name, ct) is not { } character) return NotFound();
		var gallery = await ReadGalleryAsync(character);
		if (!gallery.Any(e => e.IsIcon))
			return Conflict(new ApiErrorDto("This character has no portrait to clear."));

		return await DecidedAsync(name, character, "clear", null, gallery.Select(e => e with { IsIcon = false }), ct);
	}

	/// <summary>Writes a staff decision on the portrait and tells the game: <c>GALLERY`PORTRAIT</c>.</summary>
	private async Task<IActionResult> DecidedAsync(string name, AnySharpObject character, string action, GalleryEntry? image,
		IEnumerable<GalleryEntry> entries, CancellationToken ct)
	{
		var normalized = GalleryRules.Normalize(entries, autoIcon: !Moderation.PortraitApproval);
		if (await WriteGalleryAsync(character, normalized) is Error<string> error)
			return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorDto(error.Value));
		logger.LogInformation("Portrait {Action} on {Character}: asset={Asset} by={Staff}", action, LogSanitizer.Sanitize(name),
			image?.AssetId ?? "", User.GetActingCharacter());
		await events.TriggerEventAsync(SharpEvents.GalleryPortrait, User.GetActingCharacter(),
			character.Object().DBRef.ToString(), action, image?.AssetId ?? "", image?.Url ?? "");
		return Ok(await WithSharingAsync(normalized, ct));
	}

	private async Task<AnySharpObject?> ResolveCharacterAsync(string name, CancellationToken ct)
	{
		await foreach (var player in mediator.CreateStream(new GetPlayerQuery(name)).WithCancellation(ct))
		{
			return new AnySharpObject(player);
		}
		return null;
	}

	/// <summary>Who is writing a character's gallery: whether they may, and whether they are staff.</summary>
	private sealed record Access(AnySharpObject Character, bool MayWrite, bool IsStaff);

	/// <summary>
	/// The character and what the caller may do to its gallery; null when there is no such character. While
	/// <c>gallery_staff_only</c> is on, staff may change any gallery and nobody else may change one.
	/// </summary>
	private async Task<Access?> ResolveAccessAsync(string name, CancellationToken ct)
	{
		if (await ResolveCharacterAsync(name, ct) is not { } character) return null;
		var staff = await IsStaffAsync();
		return Moderation.StaffOnly
			? new Access(character, staff, staff)
			: new Access(character, await MayEditAsync(character, ct), staff);
	}

	/// <summary>A refused write; the staff lock says so, since the owner would otherwise expect to be let in.</summary>
	private IActionResult Refused() => Moderation.StaffOnly
		? StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto("Only staff change galleries in this game."))
		: Forbid();

	private async Task<bool> MayEditAsync(AnySharpObject character, CancellationToken ct)
	{
		// A character on the caller's own account is theirs to edit, whichever character they are playing;
		// the wiki.edit policy on every write has already been met.
		if (AccountSessionAuthenticationHandler.TryGetAccount(User, out var accountId, out _)
				&& await accounts.GetAccountForCharacterAsync(character.Object().DBRef, ct) is { Id: { Length: > 0 } owner }
				&& string.Equals(owner, accountId, StringComparison.Ordinal))
			return true;

		return await ResolveViewerAsync(ct) is { } viewer && await permissionService.Controls(viewer, character);
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
	/// <remarks>
	/// The caller has already been authorized, so the attributes are written as God (#1): while staff moderate
	/// galleries they are wizard attributes (<see cref="GuardAsync"/>), which the character itself may not write.
	/// </remarks>
	private async Task<Result<Success>> WriteGalleryAsync(AnySharpObject character, IReadOnlyList<GalleryEntry> entries)
	{
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(1))) is not AnySharpObject god)
			return new Error<string>("The gallery could not be written: God (#1) is missing.");
		var store = new CharacterAttributes(attributeService, god, character);
		var written = await GalleryWriter.WriteAsync(store, entries, logger);
		if (written is Success) await GuardAsync(store, god, character);
		return written;
	}

	/// <summary>
	/// Keeps softcode from changing what the portal moderates. While any gallery option is on, <c>PROFILE`GALLERY</c>
	/// carries the <c>wizard</c> attribute flag, so a player cannot forge official art, a waiting portrait or a terms
	/// record; while portraits wait for approval or galleries are locked, so do <c>IMAGE</c>, <c>IMAGE`BANNER</c> and
	/// <c>IMAGE`ALT</c>, so a player cannot set their portrait by hand. With the options off they are the character's
	/// own again.
	/// </summary>
	private async Task GuardAsync(CharacterAttributes store, AnySharpObject god, AnySharpObject character)
	{
		var moderation = Moderation;
		var images = moderation.PortraitApproval || moderation.StaffOnly;
		var gallery = images || moderation.ConsentText.Length > 0 || moderation.MaxGalleryMegabytes > 0 || moderation.LimitSkipsOfficial;
		// A flag needs an attribute to sit on. With no portrait there is no IMAGE, and a player could create one by hand,
		// so an empty one holds the flag; it is the parent of IMAGE`BANNER and IMAGE`ALT, so it guards their creation too.
		// An empty IMAGE left after the options are turned off is removed.
		var root = GalleryWriter.MirroredAttributes[0];
		var image = await store.ReadAsync(root);
		if (images && image is null)
			await store.SetAsync(root, MString.Empty);
		else if (!images && image is { } held && held.Text.Length == 0)
			await store.ClearAsync(root);

		foreach (var (attribute, guarded) in GalleryWriter.MirroredAttributes.Select(a => (a, images)).Prepend((GalleryWriter.GalleryAttribute, gallery)))
		{
			if (await store.ReadAsync(attribute) is null) continue;
			var flagged = guarded
				? await attributeService.SetAttributeFlagAsync(god, character, attribute, "wizard")
				: await attributeService.UnsetAttributeFlagAsync(god, character, attribute, "wizard");
			if (flagged is Error<string> error)
				logger.LogWarning("Could not {Action} the wizard flag on {Attribute} of {Dbref}: {Error}",
					guarded ? "set" : "clear", attribute, character.Object().DBRef, error.Value);
		}
	}

	/// <summary>The character's attributes, read as itself and written by <paramref name="writer"/>.</summary>
	private sealed class CharacterAttributes(IAttributeService attributes, AnySharpObject writer, AnySharpObject character) : GalleryWriter.IStore
	{
		public async ValueTask<MString?> ReadAsync(string attribute) =>
			await attributes.GetAttributeAsync(character, character, attribute, IAttributeService.AttributeMode.Read, parent: false)
				is SharpAttribute[] { Length: > 0 } path
				? path[^1].Value
				: null;

		public ValueTask<Result<Success>> SetAsync(string attribute, MString value) =>
			attributes.SetAttributeAsync(writer, character, attribute, value);

		public ValueTask<Result<Success>> ClearAsync(string attribute) =>
			attributes.ClearAttributeAsync(writer, character, attribute, IAttributeService.AttributePatternMode.Exact);
	}
}
