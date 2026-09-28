using Microsoft.AspNetCore.Components.Forms;

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
	/// <summary>A gallery image entry; mirrors <c>GalleryController.GalleryEntry</c>.</summary>
	public record GalleryItem(string AssetId, string FileName, string Url, string? Caption, int Order, bool IsIcon);

	public const long MaxUploadBytes = 10_485_760;

	private const string NoGallery = "The server returned no gallery.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Lists a character's gallery, order-sorted.</summary>
	public Task<ApiResult<List<GalleryItem>>> ListAsync(string name) =>
		Client.GetApiAsync<List<GalleryItem>>(GalleryUrl(name), NoGallery);

	/// <summary>Uploads an image and answers with the updated gallery.</summary>
	public async Task<ApiResult<List<GalleryItem>>> UploadAsync(string name, IBrowserFile file)
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
		streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
		content.Add(streamContent, "file", file.Name);

		return await Client.PostContentApiAsync<List<GalleryItem>>(GalleryUrl(name), content, NoGallery);
	}

	/// <summary>Deletes an image and answers with the updated gallery.</summary>
	public Task<ApiResult<List<GalleryItem>>> DeleteAsync(string name, string assetId) =>
		Client.DeleteApiAsync<List<GalleryItem>>($"{GalleryUrl(name)}/{Uri.EscapeDataString(assetId)}", NoGallery);

	/// <summary>Replaces order/captions/icon and answers with the gallery as the server sanitized it.</summary>
	public Task<ApiResult<List<GalleryItem>>> ReplaceAsync(string name, IReadOnlyList<GalleryItem> items) =>
		Client.PutApiAsync<IReadOnlyList<GalleryItem>, List<GalleryItem>>(GalleryUrl(name), items, NoGallery);

	private static string GalleryUrl(string name) => $"api/profile/{Uri.EscapeDataString(name)}/gallery";
}
