using OneOf;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Information about an uploaded wiki asset, as returned by the server.
/// </summary>
public record UploadedAssetInfo(string Id, string FileName, string Url, long SizeBytes, string ContentType);

/// <summary>
/// Full asset metadata as returned by the admin list endpoint.
/// </summary>
public record WikiAssetInfo(
	string Id,
	string FileName,
	string Url,
	string ContentType,
	long SizeBytes,
	string Sha256,
	string UploaderDbref,
	DateTimeOffset UploadedAt);

/// <summary>
/// Client-side service for wiki image/file assets. Uploads, lists and deletes
/// go through the server REST API (POST/GET/DELETE /api/wiki-assets).
/// </summary>
public class WikiAssetService(IHttpClientFactory httpClientFactory, ILogger<WikiAssetService> logger)
{
	/// <summary>
	/// Uploads an asset via multipart form data.
	/// Returns the uploaded asset info or a string error message.
	/// </summary>
	public async ValueTask<OneOf<UploadedAssetInfo, string>> UploadAsync(
		Stream content,
		string fileName,
		string contentType)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");

			using var form = new MultipartFormDataContent();
			using var streamContent = new StreamContent(content);
			streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
			form.Add(streamContent, "file", fileName);

			var response = await http.PostAsync("api/wiki-assets", form);

			if (response.IsSuccessStatusCode)
			{
				var dto = await response.Content.ReadFromJsonAsync<UploadedAssetInfo>();
				return dto is null
					? OneOf<UploadedAssetInfo, string>.FromT1("Server returned an empty response.")
					: OneOf<UploadedAssetInfo, string>.FromT0(dto);
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Upload failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "UploadAsync failed for fileName={FileName}", fileName);
			return ex.Message;
		}
	}

	/// <summary>
	/// Lists stored assets, newest first. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiAssetInfo>> ListAsync(int skip = 0, int take = 100)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiAssetInfo>>($"api/wiki-assets?skip={skip}&take={take}");
			return dtos ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "ListAsync failed");
			return [];
		}
	}

	/// <summary>
	/// Deletes an asset. Returns true when the server confirmed the deletion.
	/// </summary>
	public async ValueTask<bool> DeleteAsync(string id)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/wiki-assets/{Uri.EscapeDataString(id)}");
			return response.IsSuccessStatusCode;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "DeleteAsync failed for id={Id}", id);
			return false;
		}
	}
}
