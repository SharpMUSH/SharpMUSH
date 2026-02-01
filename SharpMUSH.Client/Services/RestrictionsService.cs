using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class RestrictionsService
{
	private readonly IHttpClientFactory _httpClientFactory;

	public RestrictionsService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	private HttpClient CreateClient() => _httpClientFactory.CreateClient("api");

	// Command Restrictions
	public async Task<Dictionary<string, string[]>?> GetCommandRestrictionsAsync()
	{
		var client = CreateClient();
		return await client.GetFromJsonAsync<Dictionary<string, string[]>>("api/restrictions/commands");
	}

	public async Task<bool> AddCommandRestrictionAsync(string commandName, string[] restrictions)
	{
		var client = CreateClient();
		var response = await client.PostAsJsonAsync($"api/restrictions/commands/{commandName}", restrictions);
		return response.IsSuccessStatusCode;
	}

	public async Task<bool> DeleteCommandRestrictionAsync(string commandName)
	{
		var client = CreateClient();
		var response = await client.DeleteAsync($"api/restrictions/commands/{commandName}");
		return response.IsSuccessStatusCode;
	}

	// Function Restrictions
	public async Task<Dictionary<string, string[]>?> GetFunctionRestrictionsAsync()
	{
		var client = CreateClient();
		return await client.GetFromJsonAsync<Dictionary<string, string[]>>("api/restrictions/functions");
	}

	public async Task<bool> AddFunctionRestrictionAsync(string functionName, string[] restrictions)
	{
		var client = CreateClient();
		var response = await client.PostAsJsonAsync($"api/restrictions/functions/{functionName}", restrictions);
		return response.IsSuccessStatusCode;
	}

	public async Task<bool> DeleteFunctionRestrictionAsync(string functionName)
	{
		var client = CreateClient();
		var response = await client.DeleteAsync($"api/restrictions/functions/{functionName}");
		return response.IsSuccessStatusCode;
	}
}
