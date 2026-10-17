using System.Net;
using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for the package manager admin API (<c>api/packages</c>).
/// </summary>
/// <remarks>
/// Calls use <see cref="ApiCall"/>'s exception boundary; plugin state changes retain the same boundary
/// while distinguishing the typed dependency-blocker response from ordinary failures.
/// These methods used to have no <c>catch</c> between them: an unreachable server threw
/// <see cref="HttpRequestException"/> straight out into the render loop, so the package pages went
/// blank where every other admin page shows the reason.
/// </remarks>
public class PackagesAdminService(IHttpClientFactory httpClientFactory, ServerInfoService serverInfo, AccountAuthService? accountAuth = null)
	: IDisposable
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Unsaved batch choices live only in this browser session's memory.</summary>
	public PackageBatchDraft Batch { get; } = new(accountAuth);

	public void Dispose()
	{
		admissionToken = null;
		Batch.Dispose();
	}

	private string? admissionToken;

	public Task<ApiResult<PackageBatchPlanResponse>> PlanBatchAsync(PackageBatchPlanRequest request) =>
		Batch.AdmissionBlocked
			? Task.FromResult<ApiResult<PackageBatchPlanResponse>>(new ApiFailure(ApiFailureKind.Unexpected,
				"Resolve the submitted batch's outcome before reviewing another batch."))
			: Client.PostApiAsync<PackageBatchPlanRequest, PackageBatchPlanResponse>(
				"api/packages/batches/plan", request, "The server returned no batch plan.");

	public async Task<ApiResult<PackageBatchRunResponse>> ApplyBatchAsync(string reviewToken)
	{
		if (Batch.AdmissionPending || (Batch.AdmissionUnknown && admissionToken != reviewToken))
			return new ApiFailure(ApiFailureKind.Unexpected, "A submitted batch's outcome is still unresolved.");
		var revision = Batch.Revision;
		var authority = Batch.AuthorityRevision;
		var recovering = Batch.AdmissionUnknown;
		admissionToken = reviewToken;
		Batch.SetAdmissionState(pending: true, unknown: false);
		var result = await Changing(Client.PostApiAsync<PackageBatchApplyRequest, PackageBatchRunResponse>(
			"api/packages/batches/apply", new(reviewToken), "The batch outcome is unknown. Refresh actual state before retrying."));
		if (authority != Batch.AuthorityRevision)
		{
			if (admissionToken == reviewToken) admissionToken = null;
			return result;
		}
		var unknown = result is ApiFailure failure && (recovering
			|| failure.Status is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
				or HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.Gone));
		if (revision == Batch.Revision && result is PackageBatchRunResponse run) Batch.Run = run;
		if (!unknown) admissionToken = null;
		Batch.SetAdmissionState(pending: false, unknown);
		return result;
	}

	/// <summary>Recovers an uncertain admission with its original actor-bound, idempotent review token.</summary>
	public Task<ApiResult<PackageBatchRunResponse>> RecoverBatchAdmissionAsync() =>
		Batch.AdmissionUnknown && admissionToken is { } token
			? ApplyBatchAsync(token)
			: Task.FromResult<ApiResult<PackageBatchRunResponse>>(new ApiFailure(ApiFailureKind.Unexpected, "No uncertain admission is available to recover."));

	/// <summary>An explicit refresh is required before abandoning an admission whose outcome cannot be recovered.</summary>
	public async Task<ApiResult<Success>> RefreshUnknownBatchAdmissionAsync()
	{
		if (!Batch.AdmissionUnknown || Batch.AdmissionPending)
			return new ApiFailure(ApiFailureKind.Unexpected, "No uncertain admission is available to refresh.");
		var revision = Batch.Revision;
		var authority = Batch.AuthorityRevision;
		var installed = GetInstalledAsync();
		var plugins = GetPluginsAsync();
		await Task.WhenAll(installed, plugins);
		if (await installed is ApiFailure packageFailure) return packageFailure;
		if (await plugins is ApiFailure pluginFailure) return pluginFailure;
		if (revision != Batch.Revision || authority != Batch.AuthorityRevision
			|| Batch.AdmissionPending || !Batch.AdmissionUnknown)
			return new ApiFailure(ApiFailureKind.Unexpected, "The draft changed during the refresh. Refresh actual state again.");
		admissionToken = null;
		Batch.SetAdmissionState(pending: false, unknown: false);
		return new Success();
	}

	public async Task<ApiResult<PackageBatchRunResponse>> GetBatchRunAsync(string id, CancellationToken cancellationToken = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"api/packages/batches/{Uri.EscapeDataString(id)}");
		var result = await Client.SendApiAsync<PackageBatchRunResponse>(request,
			"The batch outcome is unknown. Refresh actual state before retrying.", cancellationToken);
		if (result is not PackageBatchRunResponse { State: PackageBatchRunState.Pending or PackageBatchRunState.Running })
			serverInfo.Refresh();
		return result;
	}


	/// <summary>
	/// A call that may install or remove packages: whatever its answer, the portal asks the server again which
	/// applications are on, so its navigation follows what actually changed.
	/// </summary>
	private async Task<T> Changing<T>(Task<T> call)
	{
		var result = await call;
		serverInfo.Refresh();
		return result;
	}

	public Task<ApiResult<IReadOnlyList<InstalledPackageDto>>> GetInstalledAsync() =>
		Client.GetApiAsync<IReadOnlyList<InstalledPackageDto>>(
			"api/packages", "The server returned no package list.");

	public Task<ApiResult<IReadOnlyList<RevisionDto>>> GetRevisionsAsync(string id) =>
		Client.GetApiAsync<IReadOnlyList<RevisionDto>>(
			$"api/packages/{Uri.EscapeDataString(id)}/revisions", "The server returned no revisions.");

	public Task<ApiResult<PackageRollbackResult>> RollbackAsync(string id, int revision) =>
		Changing(Client.PostApiAsync<object?, PackageRollbackResult>(
			$"api/packages/{Uri.EscapeDataString(id)}/rollback/{revision}", null,
			"The rollback ran but the server described nothing."));

	public Task<ApiResult<Success>> UninstallAsync(string id, bool force) =>
		Changing(Client.DeleteApiAsync($"api/packages/{Uri.EscapeDataString(id)}?force={force}"));

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

	/// <summary>The README an installed package was applied with.</summary>
	public Task<ApiResult<ReadmeResponse>> GetInstalledReadmeAsync(string id) =>
		Client.GetApiAsync<ReadmeResponse>(
			$"api/packages/{Uri.EscapeDataString(id)}/readme", "That package has no readme.");

	public Task<ApiResult<PackageRepoSnapshot>> BrowseAsync(string remote) =>
		Client.GetApiAsync<PackageRepoSnapshot>(
			$"api/packages/remotes/{Uri.EscapeDataString(remote)}/browse", "The server returned no repository listing.");

	public Task<ApiResult<PlanResponse>> PlanAsync(PlanRequest request) =>
		Client.PostApiAsync<PlanRequest, PlanResponse>(
			"api/packages/plan", request, "The server returned no plan.");

	public Task<ApiResult<ApplyResponse>> ApplyAsync(ApplyRequest request) =>
		Changing(Client.PostApiAsync<ApplyRequest, ApplyResponse>(
			"api/packages/apply", request, "The apply ran but the server described nothing."));

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

	/// <summary>Every plugin the server found, and whether this server installs plugin packages.</summary>
	public Task<ApiResult<PluginsResponse>> GetPluginsAsync() =>
		Client.GetApiAsync<PluginsResponse>("api/packages/plugins", "The server returned no plugin list.");

	/// <summary>Turns plugin <paramref name="id"/> on or off; dependencies requiring other changes must be reviewed as a batch.</summary>
	public Task<PluginStateChangeResult> SetPluginEnabledAsync(string id, bool enabled) =>
		Changing(SetPluginStateAsync(id, enabled));

	private async Task<PluginStateChangeResult> SetPluginStateAsync(string id, bool enabled)
	{
		try
		{
			using var response = await Client.PostAsync(
				$"api/packages/plugins/{Uri.EscapeDataString(id)}/{(enabled ? "enable" : "disable")}", null);
			var body = await response.Content.ReadAsStringAsync();
			if (response.IsSuccessStatusCode)
			{
				try
				{
					return JsonSerializer.Deserialize<PluginChangeResponse>(body, JsonSerializerOptions.Web) is { } changed
						? changed : new ApiFailure(ApiFailureKind.Unexpected, "The server changed the plugin but described nothing.");
				}
				catch (JsonException ex) { return ApiFailure.Malformed(ex, response.StatusCode); }
			}
			if (response.StatusCode == HttpStatusCode.Conflict)
			{
				try
				{
					var blockers = JsonSerializer.Deserialize<PluginStateBlockers>(body, JsonSerializerOptions.Web);
					if (blockers is { Packages: not null, Plugins: not null }
						&& (blockers.Packages.Count > 0 || blockers.Plugins.Count > 0)) return blockers;
				}
				catch (JsonException) { }
			}
			return ApiFailure.FromStatus(response.StatusCode, body);
		}
		catch (Exception ex) { return ApiFailure.Transport(ex); }
	}

	/// <summary>Stages an uploaded plugin package (a .zip) for review; nothing is installed yet.</summary>
	public async Task<ApiResult<PluginUploadResponse>> UploadPluginAsync(Stream archive, string fileName)
	{
		using var content = new MultipartFormDataContent();
		content.Add(new StreamContent(archive), "file", fileName);
		return await Client.PostContentApiAsync<PluginUploadResponse>(
			"api/packages/plugins/upload", content, "The server staged nothing.");
	}

	/// <summary>Restarts the game engine; connections stay open.</summary>
	public Task<ApiResult<Success>> RestartServerAsync() =>
		Client.PostApiAsync("api/server/restart");
}
