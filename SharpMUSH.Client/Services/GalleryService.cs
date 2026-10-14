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
	public async Task<ApiResult<List<GalleryEntry>>> UploadAsync(string name, IBrowserFile file, GalleryUse use = GalleryUse.Image)
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

		var url = use switch
		{
			GalleryUse.Banner => $"{GalleryUrl(name)}?use=banner",
			GalleryUse.Avatar => $"{GalleryUrl(name)}?use=avatar",
			_ => GalleryUrl(name),
		};
		return Written(await Client.PostContentApiAsync<List<GalleryEntry>>(url, content, NoGallery));
	}

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

	/// <summary>A write the server took may have moved the avatar, which every avatar on screen shows.</summary>
	private ApiResult<List<GalleryEntry>> Written(ApiResult<List<GalleryEntry>> result)
	{
		if (result is List<GalleryEntry>) directory.PicturesChanged();
		return result;
	}

	/// <summary>The gallery with <paramref name="assetId"/> as its only icon.</summary>
	public static IReadOnlyList<GalleryEntry> WithIcon(IEnumerable<GalleryEntry> items, string assetId) =>
		[.. items.Select(i => i with { IsIcon = i.AssetId == assetId })];

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
	public static IReadOnlyList<GalleryEntry> WithDetails(IEnumerable<GalleryEntry> items, string assetId, string? caption, string? tags, string? warning) =>
		[.. items.Select(i => i.AssetId != assetId
			? i
			: i with
			{
				Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
				Tags = ParseTags(tags) is { Count: > 0 } parsed ? parsed : null,
				Warning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim(),
			})];

	/// <summary>Tags typed as a comma-separated list: trimmed, lower-cased, each once.</summary>
	public static IReadOnlyList<string> ParseTags(string? text) =>
		[.. (text ?? string.Empty).Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)];

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
