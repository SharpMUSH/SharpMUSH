using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The game's OAuth server as the portal uses it: the consent page's question and answer, the apps the account's
/// characters allowed, and, for staff, the apps registered to ask.
/// </summary>
public class ConnectedAppsService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>What the consent page shows for the request in its address.</summary>
	public Task<ApiResult<OAuthAuthorizeView>> CheckAsync(string query) =>
		Client.GetApiAsync<OAuthAuthorizeView>($"api/oauth/authorize{query}", "The server returned no request.");

	public Task<ApiResult<OAuthDecisionView>> DecideAsync(OAuthAuthorizeRequest request, bool approve) =>
		Client.PostApiAsync<OAuthDecisionRequest, OAuthDecisionView>("api/oauth/authorize", new OAuthDecisionRequest(request, approve),
			"The server did not say where to go next.");

	/// <summary>Every scope an app may ask for, a custom permission category's among them.</summary>
	public Task<ApiResult<List<OAuthScopeView>>> ScopesAsync() =>
		Client.GetApiAsync<List<OAuthScopeView>>("api/oauth/scopes", "The server returned no scopes.");

	public Task<ApiResult<List<OAuthGrantView>>> GrantsAsync() =>
		Client.GetApiAsync<List<OAuthGrantView>>("api/oauth/grants", "The server returned no apps.");

	public Task<ApiResult<Success>> RevokeAsync(string grantId) =>
		Client.DeleteApiAsync($"api/oauth/grants/{Uri.EscapeDataString(grantId)}");

	public Task<ApiResult<List<OAuthClientView>>> ClientsAsync() =>
		Client.GetApiAsync<List<OAuthClientView>>("api/oauth/clients", "The server returned no apps.");

	public Task<ApiResult<OAuthClientCreated>> RegisterAsync(OAuthClientEdit edit) =>
		Client.PostApiAsync<OAuthClientEdit, OAuthClientCreated>("api/oauth/clients", edit, "The server did not return the app.");

	public Task<ApiResult<OAuthClientView>> UpdateAsync(string id, OAuthClientEdit edit) =>
		Client.PutApiAsync<OAuthClientEdit, OAuthClientView>($"api/oauth/clients/{Uri.EscapeDataString(id)}", edit,
			"The server did not return the app.");

	public Task<ApiResult<OAuthClientCreated>> ResetSecretAsync(string id) =>
		Client.PostApiAsync<object?, OAuthClientCreated>($"api/oauth/clients/{Uri.EscapeDataString(id)}/secret", null,
			"The server did not return the secret.");

	public Task<ApiResult<Success>> DeleteAsync(string id) =>
		Client.DeleteApiAsync($"api/oauth/clients/{Uri.EscapeDataString(id)}");
}
