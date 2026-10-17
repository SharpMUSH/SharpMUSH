using SharpMUSH.Library.API;
using SharpMUSH.Library.Models;
namespace SharpMUSH.Client.Services;

public sealed class ExternalProviderConfigurationService(IHttpClientFactory clients)
{
	public Task<ApiResult<ExternalProviderStatus>> GetAsync(string provider) => clients.CreateClient("api").GetApiAsync<ExternalProviderStatus>("api/admin/signin-providers/" + Uri.EscapeDataString(provider), "No sign-in provider configuration was returned.");
	public Task<ApiResult<ExternalProviderStatus>> SaveAsync(string provider, ExternalProviderSettings settings, bool acknowledge) => clients.CreateClient("api").PutApiAsync<ExternalProviderSaveRequest, ExternalProviderStatus>("api/admin/signin-providers/" + Uri.EscapeDataString(provider), new(settings, acknowledge), "No sign-in provider configuration was returned.");
}
