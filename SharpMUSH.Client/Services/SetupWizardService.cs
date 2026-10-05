using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The steps of the first-run wizard after the claim (<c>api/setup/wizard</c>): whether it is still
/// unfinished, the game's HTTP and event handlers, and its bundled packages.
/// </summary>
public class SetupWizardService(IHttpClientFactory httpClientFactory, ServerInfoService serverInfo)
{
	private HttpClient Http => httpClientFactory.CreateClient("api");

	public virtual Task<ApiResult<SetupWizardResponse>> GetAsync()
		=> Http.GetApiAsync<SetupWizardResponse>("api/setup/wizard", "The server returned no setup state.");

	/// <summary>
	/// Sets the <paramref name="kind"/> handler (<see cref="HandlerKinds"/>). The portal asks the server again what
	/// the game has afterwards, since the packages built on the handler moved with it.
	/// </summary>
	public virtual async Task<ApiResult<SetupWizardResponse>> SetHandlerAsync(string kind, SetHandlerRequest request)
	{
		var result = await Http.PutApiAsync<SetHandlerRequest, SetupWizardResponse>(
			$"api/setup/wizard/handlers/{Uri.EscapeDataString(kind)}", request, "The server returned no setup state.");
		serverInfo.Refresh();
		return result;
	}

	/// <summary>
	/// Installs <paramref name="installed"/> and removes every other bundled package. Whatever the answer, the
	/// portal asks the server again what the game has, so its navigation follows what actually changed.
	/// </summary>
	public virtual async Task<ApiResult<SetupWizardResponse>> SetPackagesAsync(IReadOnlyList<string> installed)
	{
		var result = await Http.PutApiAsync<SetupPackagesRequest, SetupWizardResponse>(
			"api/setup/wizard/packages", new SetupPackagesRequest(installed), "The server returned no setup state.");
		serverInfo.Refresh();
		return result;
	}

	/// <summary>
	/// The attributes the <paramref name="kind"/> handler's packages would write to object <paramref name="dbref"/>
	/// that it already has, which the install would keep in place of the packages' own.
	/// </summary>
	public virtual Task<ApiResult<List<HandlerClash>>> GetClashesAsync(string kind, int dbref)
		=> Http.GetApiAsync<List<HandlerClash>>(
			$"api/setup/wizard/handlers/{Uri.EscapeDataString(kind)}/clashes?dbref={dbref}", "The server returned no answer.");

	/// <summary>
	/// Writes the starter wiki pages the game does not have yet: Getting Started, Theme, Setting, Policies and the
	/// categories that file them.
	/// </summary>
	public virtual Task<ApiResult<Success>> ApplyStarterWikiAsync() => Http.PostApiAsync("api/setup/wizard/starter-wiki");

	public virtual Task<ApiResult<Success>> FinishAsync() => Http.PostApiAsync("api/setup/wizard/finish");
}
