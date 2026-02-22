using SharpMUSH.Library.Services.DatabaseConversion;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class DatabaseConversionService(ILogger<DatabaseConversionService> logger, IHttpClientFactory httpClient)
{
	public async Task<string?> UploadDatabaseAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
	{
		try
		{
			using var content = new MultipartFormDataContent();
			using var streamContent = new StreamContent(fileStream);
			content.Add(streamContent, "file", fileName);

			var response = await httpClient
				.CreateClient("api")
				.PostAsync("/api/databaseconversion/upload", content, cancellationToken);

			response.EnsureSuccessStatusCode();

			var result = await response.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken: cancellationToken);
			return result?.SessionId;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error uploading database file");
			throw;
		}
	}

	public async Task<ConversionProgress?> GetProgressAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		try
		{
			var response = await httpClient
				.CreateClient("api")
				.GetAsync($"/api/databaseconversion/progress/{sessionId}", cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			return await response.Content.ReadFromJsonAsync<ConversionProgress>(cancellationToken: cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error getting conversion progress");
			return null;
		}
	}

	public async Task<ConversionResult?> GetResultAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		try
		{
			var response = await httpClient
				.CreateClient("api")
				.GetAsync($"/api/databaseconversion/result/{sessionId}", cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			return await response.Content.ReadFromJsonAsync<ConversionResult>(cancellationToken: cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error getting conversion result");
			return null;
		}
	}

	public async Task<bool> CancelConversionAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		try
		{
			var response = await httpClient
				.CreateClient("api")
				.PostAsync($"/api/databaseconversion/cancel/{sessionId}", null, cancellationToken);

			return response.IsSuccessStatusCode;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error cancelling conversion");
			return false;
		}
	}

	private class UploadResponse
	{
		public string SessionId { get; set; } = string.Empty;
		public string Message { get; set; } = string.Empty;
	}
}
