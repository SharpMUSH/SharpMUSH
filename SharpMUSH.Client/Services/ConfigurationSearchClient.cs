using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>Reads the private, metadata-only option catalog afresh for each search opening.</summary>
public class ConfigurationSearchClient(IHttpClientFactory clients)
{
	public virtual Task<ApiResult<IReadOnlyList<ConfigurationSearchEntry>>> GetAsync()
		=> clients.CreateClient("api").GetApiAsync<IReadOnlyList<ConfigurationSearchEntry>>(
			"api/application-configuration/options", "Configuration search is unavailable.");
}
