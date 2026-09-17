using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the sitelock rule list (glob and CIDR host patterns).</summary>
public class SitelockService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<Dictionary<string, string[]>>> GetSitelockRulesAsync() =>
		Client.GetApiAsync<Dictionary<string, string[]>>("api/sitelock", "The server returned no sitelock rules.");

	public Task<ApiResult<Success>> AddSitelockRuleAsync(string hostPattern, string[] accessRules) =>
		Client.PostApiAsync($"api/sitelock/{Uri.EscapeDataString(hostPattern)}", accessRules);

	public Task<ApiResult<Success>> DeleteSitelockRuleAsync(string hostPattern) =>
		Client.DeleteApiAsync($"api/sitelock/{Uri.EscapeDataString(hostPattern)}");
}
