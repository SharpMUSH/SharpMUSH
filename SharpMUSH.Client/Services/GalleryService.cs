using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character gallery. Image bytes upload to the shared file store; gallery composition
/// (order, captions, icon) is read/written via <c>/api/profile/{name}/gallery</c>. Edit operations
/// require the requester to control the character (owner) or be staff — enforced server-side.
/// </summary>
public class GalleryService(IHttpClientFactory httpClientFactory, ILogger<GalleryService> logger)
{
	/// <summary>A gallery image entry; mirrors <c>GalleryController.GalleryEntry</c>.</summary>
	public record GalleryItem(string AssetId, string FileName, string Url, string? Caption, int Order, bool IsIcon);

	private const long MaxUploadBytes = 10_485_760;

	/// <summary>Lists a character's gallery, order-sorted; empty on failure.</summary>
	public async Task<IReadOnlyList<GalleryItem>> ListAsync(string name)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var items = await http.GetFromJsonAsync<List<GalleryItem>>($"api/profile/{Uri.EscapeDataString(name)}/gallery");
			return items ?? [];
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load gallery for {Name}.", name);
			return [];
		}
	}

	/// <summary>Uploads an image; returns the updated gallery or <c>null</c> on failure/forbidden.</summary>
	public async Task<IReadOnlyList<GalleryItem>?> UploadAsync(string name, IBrowserFile file)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			using var content = new MultipartFormDataContent();
			var stream = file.OpenReadStream(MaxUploadBytes);
			var streamContent = new StreamContent(stream);
			streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
			content.Add(streamContent, "file", file.Name);

			var response = await http.PostAsync($"api/profile/{Uri.EscapeDataString(name)}/gallery", content);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Gallery upload for {Name} returned {Status}.", name, response.StatusCode);
				return null;
			}
			return await response.Content.ReadFromJsonAsync<List<GalleryItem>>();
		}
		catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
		{
			logger.LogWarning(ex, "Failed to upload gallery image for {Name}.", name);
			return null;
		}
	}

	/// <summary>Deletes an image; returns the updated gallery or <c>null</c> on failure.</summary>
	public async Task<IReadOnlyList<GalleryItem>?> DeleteAsync(string name, string assetId)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/profile/{Uri.EscapeDataString(name)}/gallery/{Uri.EscapeDataString(assetId)}");
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			return await response.Content.ReadFromJsonAsync<List<GalleryItem>>();
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to delete gallery image {Asset} for {Name}.", assetId, name);
			return null;
		}
	}

	/// <summary>Replaces order/captions/icon; returns the sanitized gallery or <c>null</c> on failure.</summary>
	public async Task<IReadOnlyList<GalleryItem>?> ReplaceAsync(string name, IReadOnlyList<GalleryItem> items)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync($"api/profile/{Uri.EscapeDataString(name)}/gallery", items);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			return await response.Content.ReadFromJsonAsync<List<GalleryItem>>();
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to update gallery for {Name}.", name);
			return null;
		}
	}
}
