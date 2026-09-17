using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for the package manager admin API (<c>api/packages</c>).
/// </summary>
/// <remarks>
/// Every call goes through <see cref="ApiCall"/>, which is also where the exception boundary is.
/// These seventeen methods used to have no <c>catch</c> between them: an unreachable server threw
/// <see cref="HttpRequestException"/> straight out into the render loop, so the package pages went
/// blank where every other admin page shows the reason.
/// </remarks>
public class PackagesAdminService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<IReadOnlyList<InstalledPackageDto>>> GetInstalledAsync() =>
		Client.GetApiAsync<IReadOnlyList<InstalledPackageDto>>(
			"api/packages", "The server returned no package list.");

	public Task<ApiResult<IReadOnlyList<RevisionDto>>> GetRevisionsAsync(string id) =>
		Client.GetApiAsync<IReadOnlyList<RevisionDto>>(
			$"api/packages/{Uri.EscapeDataString(id)}/revisions", "The server returned no revisions.");

	public Task<ApiResult<PackageRollbackResult>> RollbackAsync(string id, int revision) =>
		Client.PostApiAsync<object?, PackageRollbackResult>(
			$"api/packages/{Uri.EscapeDataString(id)}/rollback/{revision}", null,
			"The rollback ran but the server described nothing.");

	public Task<ApiResult<Success>> UninstallAsync(string id, bool force) =>
		Client.DeleteApiAsync($"api/packages/{Uri.EscapeDataString(id)}?force={force}");

	public Task<ApiResult<PackageUpdateInfo>> CheckForUpdateAsync(string id) =>
		Client.GetApiAsync<PackageUpdateInfo>(
			$"api/packages/{Uri.EscapeDataString(id)}/update", "The server returned no update information.");

	public Task<ApiResult<IReadOnlyList<PackageRemoteRecord>>> GetRemotesAsync() =>
		Client.GetApiAsync<IReadOnlyList<PackageRemoteRecord>>(
			"api/packages/remotes", "The server returned no remote list.");

	public Task<ApiResult<Success>> UpsertRemoteAsync(RemoteRequest request) =>
		Client.PostApiAsync("api/packages/remotes", request);

	public Task<ApiResult<Success>> DeleteRemoteAsync(string name) =>
		Client.DeleteApiAsync($"api/packages/remotes/{Uri.EscapeDataString(name)}");

	public Task<ApiResult<CommunityReposResponse>> GetCommunityReposAsync() =>
		Client.GetApiAsync<CommunityReposResponse>(
			"api/packages/community", "The server returned no community repositories.");

	public Task<ApiResult<ReadmeResponse>> GetCommunityReadmeAsync(string url) =>
		Client.GetApiAsync<ReadmeResponse>(
			$"api/packages/community/readme?url={Uri.EscapeDataString(url)}", "That repository has no readme.");

	public Task<ApiResult<ReadmeResponse>> GetRemoteReadmeAsync(
		string remote, string? path = null, string? version = null)
	{
		var query = new List<string>();
		if (!string.IsNullOrEmpty(path)) query.Add($"path={Uri.EscapeDataString(path)}");
		if (!string.IsNullOrEmpty(version)) query.Add($"version={Uri.EscapeDataString(version)}");
		var suffix = query.Count > 0 ? $"?{string.Join('&', query)}" : string.Empty;

		return Client.GetApiAsync<ReadmeResponse>(
			$"api/packages/remotes/{Uri.EscapeDataString(remote)}/readme{suffix}", "That package has no readme.");
	}

	public Task<ApiResult<PackageRepoSnapshot>> BrowseAsync(string remote) =>
		Client.GetApiAsync<PackageRepoSnapshot>(
			$"api/packages/remotes/{Uri.EscapeDataString(remote)}/browse", "The server returned no repository listing.");

	public Task<ApiResult<PlanResponse>> PlanAsync(PlanRequest request) =>
		Client.PostApiAsync<PlanRequest, PlanResponse>(
			"api/packages/plan", request, "The server returned no plan.");

	public Task<ApiResult<ApplyResponse>> ApplyAsync(ApplyRequest request) =>
		Client.PostApiAsync<ApplyRequest, ApplyResponse>(
			"api/packages/apply", request, "The apply ran but the server described nothing.");

	public Task<ApiResult<PackageAuthoringScan>> AuthorScanAsync(IReadOnlyList<string> objids) =>
		Client.PostApiAsync<IReadOnlyList<string>, PackageAuthoringScan>(
			"api/packages/author/scan", objids, "The server returned no scan.");

	/// <summary>
	/// The export endpoint answers with YAML, not JSON, so this one reads the body as text either way
	/// — on success it is the package, on failure it is the reason.
	/// </summary>
	public async Task<ApiResult<string>> AuthorExportAsync(PackageAuthoringRequest request)
	{
		try
		{
			using var response = await Client.PostAsJsonAsync("api/packages/author/export", request);
			var content = await response.Content.ReadAsStringAsync();
			return response.IsSuccessStatusCode
				? content
				: ApiFailure.FromStatus(response.StatusCode, content);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}
}
