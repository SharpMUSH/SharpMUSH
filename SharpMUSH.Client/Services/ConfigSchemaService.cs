using SharpMUSH.Library.API;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class ConfigSchemaService(IHttpClientFactory httpClientFactory)
{
	public async Task<ConfigurationSchema?> GetSchemaAsync()
	{
		try
		{
			// Use the named "api" client which is configured to point to port 8081
			var client = httpClientFactory.CreateClient("api");
			var response = await client.GetFromJsonAsync<ConfigurationResponse>("/api/configuration");

			if (response?.Schema == null)
			{
				return null;
			}

			return response.Schema;
		}
		catch (Exception)
		{
			// TODO: Add logging
			return null;
		}
	}
}
