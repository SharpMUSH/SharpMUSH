using Microsoft.AspNetCore.Components.Forms;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character gallery. Image bytes upload to the shared file store; gallery composition
/// (order, captions, icon) is read/written via <c>/api/profile/{name}/gallery</c>. Edit operations
/// require the requester to control the character (owner) or be staff — enforced server-side.
/// </summary>
/// <remarks>
/// Every call answers with <see cref="ApiResult{T}"/>. A write answers with the gallery as the server
/// now holds it, so the widget never has to guess what a refused delete left behind.
/// </remarks>
public class GalleryService(IHttpClientFactory httpClientFactory, CharacterDirectoryService directory)
{
	public const long MaxUploadBytes = 10_485_760;

	private const string NoGallery = "The server returned no gallery.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Lists a character's gallery, order-sorted.</summary>
	public Task<ApiResult<List<GalleryEntry>>> ListAsync(string name) =>
		Client.GetApiAsync<List<GalleryEntry>>(GalleryUrl(name), NoGallery);

	/// <summary>
	/// Uploads an image and answers with the updated gallery. As <see cref="GalleryUse.Banner"/> or
	/// <see cref="GalleryUse.Avatar"/> it takes that place from whichever image held it.
	/// </summary>
	/// <param name="consent">The <see cref="GalleryPolicy.ConsentVersion"/> of the terms the uploader accepted; empty when
	/// the game asks for none.</param>
	public async Task<ApiResult<List<GalleryEntry>>> UploadAsync(string name, IBrowserFile file, GalleryUse use = GalleryUse.Image, string consent = "")
	{
		// OpenReadStream throws for a file over the limit before anything is sent. Said here, rather than
		// surfacing as "could not reach the server" from ApiCall's transport catch.
		if (file.Size > MaxUploadBytes)
		{
			return new ApiFailure(ApiFailureKind.Unexpected,
				$"{file.Name} is larger than the {MaxUploadBytes / 1_048_576} MB upload limit.");
		}

		Stream stream;
		try
		{
			stream = file.OpenReadStream(MaxUploadBytes);
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException)
		{
			return new ApiFailure(ApiFailureKind.Unexpected, $"{file.Name} could not be read: {ex.Message}");
		}

		using var content = new MultipartFormDataContent();
		var streamContent = new StreamContent(stream);
		content.Add(streamContent, "file", file.Name);

		// A browser that cannot name the type sends "", which the MediaTypeHeaderValue constructor throws on,
		// out of the widget's click handler. Sent without one, the server refuses it and says which types it takes.
		if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType))
		{
			streamContent.Headers.ContentType = mediaType;
		}

		var query = new List<string>();
		if (use is GalleryUse.Banner) query.Add("use=banner");
		if (use is GalleryUse.Avatar) query.Add("use=avatar");
		if (consent.Length > 0) query.Add($"consent={Uri.EscapeDataString(consent)}");
		var url = query.Count == 0 ? GalleryUrl(name) : $"{GalleryUrl(name)}?{string.Join('&', query)}";
		return Written(await Client.PostContentApiAsync<List<GalleryEntry>>(url, content, NoGallery));
	}

	/// <summary>
	/// Adds a file from the shared image library, without copying it, and answers with the updated gallery.
	/// As <see cref="GalleryUse.Banner"/> or <see cref="GalleryUse.Avatar"/> it takes that place.
	/// </summary>
	public async Task<ApiResult<List<GalleryEntry>>> AddFromLibraryAsync(string name, string assetId, GalleryUse use = GalleryUse.Image, string consent = "") =>
		Written(await Client.PostApiAsync<GalleryLibraryPick, List<GalleryEntry>>($"{GalleryUrl(name)}/library",
			new GalleryLibraryPick(assetId, use switch
			{
				GalleryUse.Banner => "banner",
				GalleryUse.Avatar => "avatar",
				_ => null,
			})
			{ Consent = consent.Length > 0 ? consent : null }, NoGallery));

	/// <summary>Puts an uploaded image in the shared image library, for good, and answers with the gallery.</summary>
	public Task<ApiResult<List<GalleryEntry>>> ShareAsync(string name, string assetId) =>
		Client.PostApiAsync<object?, List<GalleryEntry>>($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}/share", null, NoGallery);

	/// <summary>Deletes an image and answers with the updated gallery.</summary>
	public async Task<ApiResult<List<GalleryEntry>>> DeleteAsync(string name, string assetId) =>
		Written(await Client.DeleteApiAsync<List<GalleryEntry>>($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}", NoGallery));

	/// <summary>Replaces order/captions/icon and answers with the gallery as the server sanitized it.</summary>
	public async Task<ApiResult<List<GalleryEntry>>> ReplaceAsync(string name, IReadOnlyList<GalleryEntry> items) =>
		Written(await Client.PutApiAsync<IReadOnlyList<GalleryEntry>, List<GalleryEntry>>(GalleryUrl(name), items, NoGallery));

	/// <summary>
	/// Reports an image to the game's staff with the reason given. The server fires the game's
	/// <c>GALLERY`REPORT</c> event; what happens next is the game's choice.
	/// </summary>
	public Task<ApiResult<Success>> ReportAsync(string name, string assetId, string reason) =>
		Client.PostApiAsync($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}/report", new GalleryReportRequest(reason));

	private Task<bool>? _reporting;

	/// <summary>
	/// Whether the game takes reports of images, asked once: Report is offered only when a report reaches
	/// someone. A failed read answers no and is asked again next time.
	/// </summary>
	public async Task<bool> ReportsTakenAsync()
	{
		var reporting = _reporting ??= ReadReportingAsync();
		var taken = await reporting;
		if (!taken) _reporting = null;
		return taken;
	}

	private async Task<bool> ReadReportingAsync() =>
		await Client.GetApiAsync<GalleryReporting>("api/gallery/reports", NoGallery) is GalleryReporting { Taken: true };

	/// <summary>
	/// The game's gallery rules and whether the viewer is gallery staff. Read with each page, not kept: the answer
	/// depends on who is signed in, and staff may change the rules at any time.
	/// </summary>
	public Task<ApiResult<GalleryPolicy>> PolicyAsync() =>
		Client.GetApiAsync<GalleryPolicy>("api/gallery/policy", "The server returned no gallery rules.");

	/// <summary>
	/// Whether the viewer may change this gallery: <paramref name="canEdit"/> (owner or staff, as the profile works it
	/// out), unless the game leaves galleries to staff, when only gallery staff may.
	/// </summary>
	public static bool MayChange(bool canEdit, GalleryPolicy? policy) =>
		policy is { StaffOnly: true } ? policy.IsStaff : canEdit;

	/// <summary>Portraits waiting for staff (<c>media.admin</c>).</summary>
	public Task<ApiResult<List<GalleryPortraitSubmission>>> PortraitsAsync() =>
		Client.GetApiAsync<List<GalleryPortraitSubmission>>("api/gallery/portraits", "The server returned no portraits.");

	/// <summary>Staff make an image the portrait, as official art, and answer with the gallery.</summary>
	public async Task<ApiResult<List<GalleryEntry>>> ApprovePortraitAsync(string name, string assetId) =>
		Written(await Client.PostApiAsync<object?, List<GalleryEntry>>($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}/portrait", null, NoGallery));

	/// <summary>Staff turn down a waiting portrait; the image stays in the gallery.</summary>
	public Task<ApiResult<List<GalleryEntry>>> DeclinePortraitAsync(string name, string assetId) =>
		Client.DeleteApiAsync<List<GalleryEntry>>($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}/portrait", NoGallery);

	/// <summary>Staff clear the portrait, so the profile shows the default.</summary>
	public async Task<ApiResult<List<GalleryEntry>>> ClearPortraitAsync(string name) =>
		Written(await Client.DeleteApiAsync<List<GalleryEntry>>($"{GalleryUrl(name)}/portrait", NoGallery));

	/// <summary>A write the server took may have moved the avatar, which every avatar on screen shows.</summary>
	private ApiResult<List<GalleryEntry>> Written(ApiResult<List<GalleryEntry>> result)
	{
		if (result is List<GalleryEntry>) directory.PicturesChanged();
		return result;
	}

	/// <summary>The gallery with <paramref name="assetId"/> as its only icon.</summary>
	public static IReadOnlyList<GalleryEntry> WithIcon(IEnumerable<GalleryEntry> items, string assetId) =>
		[.. items.Select(i => i with { IsIcon = i.AssetId == assetId })];

	/// <summary>The image a write sent to staff as the portrait, when it did: waiting now and not before.</summary>
	public static GalleryEntry? NewlySubmitted(IEnumerable<GalleryEntry> before, IEnumerable<GalleryEntry> after)
	{
		var waiting = before.Where(b => b.PortraitPending).Select(b => b.AssetId).ToHashSet(StringComparer.Ordinal);
		return after.FirstOrDefault(a => a.PortraitPending && !waiting.Contains(a.AssetId));
	}

	/// <summary>The gallery with <paramref name="assetId"/> marked official art, or not.</summary>
	public static IReadOnlyList<GalleryEntry> WithOfficial(IEnumerable<GalleryEntry> items, string assetId, bool official) =>
		[.. items.Select(i => i.AssetId == assetId ? i with { Official = official } : i)];

	/// <summary>The gallery with the request to make <paramref name="assetId"/> the portrait withdrawn.</summary>
	public static IReadOnlyList<GalleryEntry> Withdrawn(IEnumerable<GalleryEntry> items, string assetId) =>
		[.. items.Select(i => i.AssetId == assetId ? i with { PortraitPending = false } : i)];

	/// <summary>The gallery with <paramref name="assetId"/> as its only banner, or none when null.</summary>
	public static IReadOnlyList<GalleryEntry> WithBanner(IEnumerable<GalleryEntry> items, string? assetId) =>
		[.. items.Select(i => i with { IsBanner = i.AssetId == assetId })];

	/// <summary>The gallery with <paramref name="assetId"/> moved <paramref name="delta"/> places, kept inside the list.</summary>
	public static IReadOnlyList<GalleryEntry> Moved(IReadOnlyList<GalleryEntry> items, string assetId, int delta)
	{
		var list = items.ToList();
		var from = list.FindIndex(i => i.AssetId == assetId);
		if (from < 0) return items;
		var to = Math.Clamp(from + delta, 0, list.Count - 1);
		var item = list[from];
		list.RemoveAt(from);
		list.Insert(to, item);
		return [.. list.Select((e, i) => e with { Order = i })];
	}

	/// <summary>The gallery with <paramref name="assetId"/>'s caption, tags and warning replaced; blank clears.</summary>
	public static IReadOnlyList<GalleryEntry> WithDetails(IEnumerable<GalleryEntry> items, string assetId, string? caption, IReadOnlyList<string> tags, string? warning) =>
		[.. items.Select(i => i.AssetId != assetId
			? i
			: i with
			{
				Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
				Tags = CleanTags(tags) is { Count: > 0 } cleaned ? cleaned : null,
				Warning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim(),
			})];

	/// <summary>A tag as the gallery stores it: trimmed and lower-cased.</summary>
	public static string NormalizeTag(string tag) => tag.Trim().ToLowerInvariant();

	/// <summary>The tags an owner gave: each normalized, the empty ones dropped, each once.</summary>
	public static IReadOnlyList<string> CleanTags(IEnumerable<string> tags) =>
		[.. tags.Select(NormalizeTag).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)];

	/// <summary>Every tag the gallery's images carry, in the order they first appear.</summary>
	public static IReadOnlyList<string> AllTags(IEnumerable<GalleryEntry> items) =>
		[.. items.SelectMany(i => i.Tags ?? []).Distinct(StringComparer.Ordinal)];

	private static string GalleryUrl(string name) => $"api/profile/{Uri.EscapeDataString(name)}/gallery";
}

/// <summary>What an uploaded image is for: one more gallery image, the profile's banner, or its avatar.</summary>
public enum GalleryUse
{
	Image,
	Banner,
	Avatar,
}
