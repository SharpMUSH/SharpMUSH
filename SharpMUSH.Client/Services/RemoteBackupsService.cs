using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

public class RemoteBackupsService(IHttpClientFactory factory)
{
	private HttpClient Client => factory.CreateClient("api");
	public Task<ApiResult<RemoteBackupStatus>> StatusAsync() =>
		Client.GetApiAsync<RemoteBackupStatus>("api/admin/backups/remote", "The server returned no remote backup status.");
	public Task<ApiResult<RemoteBackupStatus>> ConfigureAsync(RemoteBackupConfigure request) =>
		Client.PutApiAsync<RemoteBackupConfigure, RemoteBackupStatus>("api/admin/backups/remote", request, "The server did not confirm the configuration.");
	public Task<ApiResult<RemoteBackupRun>> RunAsync() =>
		Client.PostApiAsync<object, RemoteBackupRun>("api/admin/backups/remote/run", new { }, "The server did not queue a backup.");
	public Task<ApiResult<RemoteBackupStatus>> AbandonAsync(Guid id) =>
		Client.PostApiAsync<object, RemoteBackupStatus>($"api/admin/backups/remote/{id}/abandon", new { }, "The server did not abandon the unknown job.");

}
