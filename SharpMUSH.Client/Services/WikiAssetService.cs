using SharpMUSH.Library.DiscriminatedUnions;
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
	DateTimeOffset UploadedAt,
	bool Private = false,
	string? Lock = null);

/// <summary>
/// One file in the shared image library (<c>GET api/wiki-assets/library</c>).
/// </summary>
/// <param name="Lock">The permission a player needs to find it; null for everyone.</param>
/// <param name="CanManage">The caller uploaded it or holds <c>media.admin</c>, so may change its lock.</param>
public record LibraryAssetInfo(
	string Id,
	string FileName,
	string Url,
	string ContentType,
	long SizeBytes,
	DateTimeOffset UploadedAt,
	string? Lock,
	bool CanManage);

/// <summary>The body of <c>PUT api/wiki-assets/{id}/sharing</c>.</summary>
public record AssetSharingRequest(bool Shared, string? Lock);

/// <summary>
/// Client-side service for wiki image/file assets. Uploads, lists and deletes
/// go through the server REST API (POST/GET/DELETE /api/wiki-assets).
/// </summary>
public class WikiAssetService(IHttpClientFactory httpClientFactory, ILogger<WikiAssetService> logger)
{
	/// <summary>
	/// Uploads an asset via multipart form data.
	/// Returns the uploaded asset info, or the <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<UploadedAssetInfo>> UploadAsync(
		Stream content,
		string fileName,
		string contentType)
	{
		using var form = new MultipartFormDataContent();
		using var streamContent = new StreamContent(content);
		streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
		form.Add(streamContent, "file", fileName);

		var result = await httpClientFactory.CreateClient("api")
			.PostContentApiAsync<UploadedAssetInfo>("api/wiki-assets", form, "Server returned an empty response.");

		if (result is ApiFailure failure)
			logger.LogError("UploadAsync failed for fileName={FileName}: {Reason}", fileName, failure.Message);

		return result;
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
	/// The shared image library, newest first: the files the caller finds, whose names contain
	/// <paramref name="search"/> when one is given.
	/// </summary>
	public Task<ApiResult<List<LibraryAssetInfo>>> LibraryAsync(string? search = null, int skip = 0, int take = 100) =>
		httpClientFactory.CreateClient("api").GetApiAsync<List<LibraryAssetInfo>>(
			$"api/wiki-assets/library?skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}",
			"The server returned no image library.");

	/// <summary>
	/// Shares a file kept to one gallery, or sets the permission a player needs to find it in the library
	/// (null for everyone). Answers with the file as the library now shows it.
	/// </summary>
	public Task<ApiResult<LibraryAssetInfo>> SetSharingAsync(string id, bool shared, string? lockPermission) =>
		httpClientFactory.CreateClient("api").PutApiAsync<AssetSharingRequest, LibraryAssetInfo>(
			$"api/wiki-assets/{Uri.EscapeDataString(id)}/sharing", new AssetSharingRequest(shared, lockPermission),
			"The server returned no file.");

	/// <summary>
	/// Deletes an asset: <see cref="Success"/> once the server confirmed it, or the
	/// <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<Success>> DeleteAsync(string id)
	{
		var result = await httpClientFactory.CreateClient("api").DeleteApiAsync($"api/wiki-assets/{Uri.EscapeDataString(id)}");

		if (result is ApiFailure failure)
			logger.LogError("DeleteAsync failed for id={Id}: {Reason}", id, failure.Message);

		return result;
	}
}
