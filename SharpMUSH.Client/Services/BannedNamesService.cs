using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class BannedNamesService
{
	private readonly IHttpClientFactory _httpClientFactory;

	public BannedNamesService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	private HttpClient CreateClient() => _httpClientFactory.CreateClient("api");

	public async Task<string[]?> GetBannedNamesAsync()
	{
		var client = CreateClient();
		return await client.GetFromJsonAsync<string[]>("api/bannednames");
	}

	public async Task<bool> AddBannedNameAsync(string name)
	{
		var client = CreateClient();
		var response = await client.PostAsJsonAsync("api/bannednames", name);
		return response.IsSuccessStatusCode;
	}

	public async Task<bool> DeleteBannedNameAsync(string name)
	{
		var client = CreateClient();
		var response = await client.DeleteAsync($"api/bannednames/{Uri.EscapeDataString(name)}");
		return response.IsSuccessStatusCode;
	}
}
