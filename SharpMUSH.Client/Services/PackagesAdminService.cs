using System.Net.Http.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for the package manager admin API (<c>api/packages</c>).
/// </summary>
public class PackagesAdminService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	// ── Installed / dashboard ────────────────────────────────────────────────

	public async Task<IReadOnlyList<InstalledPackageDto>> GetInstalledAsync() =>
		await Client.GetFromJsonAsync<IReadOnlyList<InstalledPackageDto>>("api/packages") ?? [];

	public async Task<IReadOnlyList<RevisionDto>> GetRevisionsAsync(string id) =>
		await Client.GetFromJsonAsync<IReadOnlyList<RevisionDto>>($"api/packages/{Uri.EscapeDataString(id)}/revisions") ?? [];

	public async Task<(PackageRollbackResult? Result, string? Error)> RollbackAsync(string id, int revision)
	{
		var response = await Client.PostAsync($"api/packages/{Uri.EscapeDataString(id)}/rollback/{revision}", null);
		return response.IsSuccessStatusCode
			? (await response.Content.ReadFromJsonAsync<PackageRollbackResult>(), null)
			: (null, await response.Content.ReadAsStringAsync());
	}

	public async Task<string?> UninstallAsync(string id, bool force)
	{
		var response = await Client.DeleteAsync($"api/packages/{Uri.EscapeDataString(id)}?force={force}");
		return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync();
	}

	public async Task<PackageUpdateInfo?> CheckForUpdateAsync(string id)
	{
		var response = await Client.GetAsync($"api/packages/{Uri.EscapeDataString(id)}/update");
		return response.IsSuccessStatusCode
			? await response.Content.ReadFromJsonAsync<PackageUpdateInfo>()
			: null;
	}

	// ── Remotes & community ──────────────────────────────────────────────────

	public async Task<IReadOnlyList<PackageRemoteRecord>> GetRemotesAsync() =>
		await Client.GetFromJsonAsync<IReadOnlyList<PackageRemoteRecord>>("api/packages/remotes") ?? [];

	public async Task<string?> UpsertRemoteAsync(RemoteRequest request)
	{
		var response = await Client.PostAsJsonAsync("api/packages/remotes", request);
		return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync();
	}

	public async Task DeleteRemoteAsync(string name) =>
		await Client.DeleteAsync($"api/packages/remotes/{Uri.EscapeDataString(name)}");

	public async Task<CommunityReposResponse?> GetCommunityReposAsync() =>
		await Client.GetFromJsonAsync<CommunityReposResponse>("api/packages/community");

	public async Task<ReadmeResponse?> GetCommunityReadmeAsync(string url)
	{
		var response = await Client.GetAsync($"api/packages/community/readme?url={Uri.EscapeDataString(url)}");
		return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ReadmeResponse>() : null;
	}

	public async Task<ReadmeResponse?> GetRemoteReadmeAsync(string remote, string? path = null, string? version = null)
	{
		var query = new List<string>();
		if (!string.IsNullOrEmpty(path)) query.Add($"path={Uri.EscapeDataString(path)}");
		if (!string.IsNullOrEmpty(version)) query.Add($"version={Uri.EscapeDataString(version)}");
		var suffix = query.Count > 0 ? $"?{string.Join('&', query)}" : "";
		var response = await Client.GetAsync(
			$"api/packages/remotes/{Uri.EscapeDataString(remote)}/readme{suffix}");
		return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ReadmeResponse>() : null;
	}

	// ── Browse / plan / apply ────────────────────────────────────────────────

	public async Task<(PackageRepoSnapshot? Snapshot, string? Error)> BrowseAsync(string remote)
	{
		var response = await Client.GetAsync($"api/packages/remotes/{Uri.EscapeDataString(remote)}/browse");
		return response.IsSuccessStatusCode
			? (await response.Content.ReadFromJsonAsync<PackageRepoSnapshot>(), null)
			: (null, await response.Content.ReadAsStringAsync());
	}

	public async Task<(PlanResponse? Plan, string? Error)> PlanAsync(PlanRequest request)
	{
		var response = await Client.PostAsJsonAsync("api/packages/plan", request);
		return response.IsSuccessStatusCode
			? (await response.Content.ReadFromJsonAsync<PlanResponse>(), null)
			: (null, await response.Content.ReadAsStringAsync());
	}

	public async Task<(ApplyResponse? Result, string? Error)> ApplyAsync(ApplyRequest request)
	{
		var response = await Client.PostAsJsonAsync("api/packages/apply", request);
		return response.IsSuccessStatusCode
			? (await response.Content.ReadFromJsonAsync<ApplyResponse>(), null)
			: (null, await response.Content.ReadAsStringAsync());
	}

	// ── Authoring ────────────────────────────────────────────────────────────

	public async Task<(PackageAuthoringScan? Scan, string? Error)> AuthorScanAsync(IReadOnlyList<string> objids)
	{
		var response = await Client.PostAsJsonAsync("api/packages/author/scan", objids);
		return response.IsSuccessStatusCode
			? (await response.Content.ReadFromJsonAsync<PackageAuthoringScan>(), null)
			: (null, await response.Content.ReadAsStringAsync());
	}

	public async Task<(string? Yaml, string? Error)> AuthorExportAsync(PackageAuthoringRequest request)
	{
		var response = await Client.PostAsJsonAsync("api/packages/author/export", request);
		var content = await response.Content.ReadAsStringAsync();
		return response.IsSuccessStatusCode ? (content, null) : (null, content);
	}
}
