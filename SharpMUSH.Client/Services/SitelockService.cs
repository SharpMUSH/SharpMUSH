using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class SitelockService
{
	private readonly IHttpClientFactory _httpClientFactory;

	public SitelockService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	private HttpClient CreateClient() => _httpClientFactory.CreateClient("api");

	public async Task<Dictionary<string, string[]>?> GetSitelockRulesAsync()
	{
		var client = CreateClient();
		return await client.GetFromJsonAsync<Dictionary<string, string[]>>("api/sitelock");
	}

	public async Task<bool> AddSitelockRuleAsync(string hostPattern, string[] accessRules)
	{
		var client = CreateClient();
		var response = await client.PostAsJsonAsync($"api/sitelock/{Uri.EscapeDataString(hostPattern)}", accessRules);
		return response.IsSuccessStatusCode;
	}

	public async Task<bool> DeleteSitelockRuleAsync(string hostPattern)
	{
		var client = CreateClient();
		var response = await client.DeleteAsync($"api/sitelock/{Uri.EscapeDataString(hostPattern)}");
		return response.IsSuccessStatusCode;
	}
}
