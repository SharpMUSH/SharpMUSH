using Microsoft.AspNetCore.Components.Forms;
using SharpMUSH.Library.API;

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
public class GalleryService(IHttpClientFactory httpClientFactory)
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
		return await Client.PostContentApiAsync<List<GalleryEntry>>(url, content, NoGallery);
	}

	/// <summary>Deletes an image and answers with the updated gallery.</summary>
	public Task<ApiResult<List<GalleryEntry>>> DeleteAsync(string name, string assetId) =>
		Client.DeleteApiAsync<List<GalleryEntry>>($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}", NoGallery);

	/// <summary>Replaces order/captions/icon and answers with the gallery as the server sanitized it.</summary>
	public Task<ApiResult<List<GalleryEntry>>> ReplaceAsync(string name, IReadOnlyList<GalleryEntry> items) =>
		Client.PutApiAsync<IReadOnlyList<GalleryEntry>, List<GalleryEntry>>(GalleryUrl(name), items, NoGallery);

	/// <summary>The gallery with <paramref name="assetId"/> as its only icon.</summary>
	public static IReadOnlyList<GalleryEntry> WithIcon(IEnumerable<GalleryEntry> items, string assetId) =>
		[.. items.Select(i => i with { IsIcon = i.AssetId == assetId })];

	/// <summary>The gallery with <paramref name="assetId"/> as its only banner, or none when null.</summary>
	public static IReadOnlyList<GalleryEntry> WithBanner(IEnumerable<GalleryEntry> items, string? assetId) =>
		[.. items.Select(i => i with { IsBanner = i.AssetId == assetId })];

	private static string GalleryUrl(string name) => $"api/profile/{Uri.EscapeDataString(name)}/gallery";
}

/// <summary>What an uploaded image is for: one more gallery image, the profile's banner, or its avatar.</summary>
public enum GalleryUse
{
	Image,
	Banner,
	Avatar,
}
