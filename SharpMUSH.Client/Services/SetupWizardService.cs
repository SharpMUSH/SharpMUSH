using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The steps of the first-run wizard after the claim (<c>api/setup/wizard</c>): whether it is still
/// unfinished, and switching the game's optional applications on and off.
/// </summary>
public class SetupWizardService(IHttpClientFactory httpClientFactory, ServerInfoService serverInfo)
{
	private HttpClient Http => httpClientFactory.CreateClient("api");

	public virtual Task<ApiResult<SetupWizardResponse>> GetAsync()
		=> Http.GetApiAsync<SetupWizardResponse>("api/setup/wizard", "The server returned no setup state.");

	/// <summary>
	/// Turns on <paramref name="enabled"/> and off every other optional application. Whatever the answer, the
	/// portal asks the server again what the game has, so its navigation follows what actually changed.
	/// </summary>
	public virtual async Task<ApiResult<SetupWizardResponse>> SetApplicationsAsync(IReadOnlyList<string> enabled)
	{
		var result = await Http.PutApiAsync<SetupApplicationsRequest, SetupWizardResponse>(
			"api/setup/wizard/applications", new SetupApplicationsRequest(enabled), "The server returned no setup state.");
		serverInfo.Refresh();
		return result;
	}

	public virtual Task<ApiResult<Success>> FinishAsync() => Http.PostApiAsync("api/setup/wizard/finish");
}
