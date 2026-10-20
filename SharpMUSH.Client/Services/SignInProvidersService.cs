using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the sign-in services staff set up. A client secret goes up and never comes back.</summary>
public class SignInProvidersService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<List<SignInProviderSettings>>> ListAsync() =>
		Client.GetApiAsync<List<SignInProviderSettings>>("api/admin/sign-in-providers", "The server listed no sign-in services.");

	/// <summary>The address to register with the service for a provider called <paramref name="id"/>.</summary>
	public Task<ApiResult<SignInProviderRedirect>> RedirectUriAsync(string id) =>
		Client.GetApiAsync<SignInProviderRedirect>($"api/admin/sign-in-providers/redirect-uri/{Uri.EscapeDataString(id)}",
			"The server did not give the address to register.");

	public Task<ApiResult<SignInProviderSettings>> SaveAsync(string id, SignInProviderUpdate update) =>
		Client.PutApiAsync<SignInProviderUpdate, SignInProviderSettings>($"api/admin/sign-in-providers/{Uri.EscapeDataString(id)}",
			update, "The server did not say what it saved.");

	public Task<ApiResult<Success>> RemoveAsync(string id) =>
		Client.DeleteApiAsync($"api/admin/sign-in-providers/{Uri.EscapeDataString(id)}");
}
