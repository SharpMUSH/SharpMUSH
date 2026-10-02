using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Uploads a PennMUSH database for the server to convert, and follows the conversion.
/// </summary>
/// <remarks>
/// Every call used to answer <see langword="null"/> or <see langword="false"/> for a failure, or
/// throw, and the import page logged it and carried on: a refused upload left the form as it was
/// with nothing said, a conversion the server no longer knew about polled forever over a blank card,
/// and a refused cancel reset the page as though the conversion had stopped. The server words each
/// refusal ("No file uploaded", "Session not found or already completed"); these carry it.
/// </remarks>
public class DatabaseConversionService(ILogger<DatabaseConversionService> logger, IHttpClientFactory httpClient)
{
	private HttpClient Client => httpClient.CreateClient("api");

	/// <summary>
	/// Uploads the object database and, when given, the maildb (sent as <c>mailFile</c>) and the chatdb
	/// (sent as <c>chatFile</c>). Answers the id of the conversion session it started.
	/// </summary>
	public async Task<ApiResult<string>> UploadDatabaseAsync(Stream fileStream, string fileName, Stream? mailStream = null,
		string? mailFileName = null, Stream? chatStream = null, string? chatFileName = null, Stream? configStream = null,
		string? configFileName = null)
	{
		using var content = new MultipartFormDataContent();
		content.Add(new StreamContent(fileStream), "file", fileName);
		if (configStream is not null)
		{
			content.Add(new StreamContent(configStream), "configFile", configFileName ?? "mush.cnf");
		}

		if (mailStream is not null)
		{
			content.Add(new StreamContent(mailStream), "mailFile", mailFileName ?? "maildb");
		}

		if (chatStream is not null)
		{
			content.Add(new StreamContent(chatStream), "chatFile", chatFileName ?? "chatdb");
		}

		var response = await Client.PostContentApiAsync<UploadResponse>(
			"/api/databaseconversion/upload", content, "The server started no conversion.");

		ApiResult<string> result = response switch
		{
			UploadResponse { SessionId: { Length: > 0 } sessionId } => sessionId,
			UploadResponse => new ApiFailure(ApiFailureKind.Unexpected, "The server started no conversion."),
			ApiFailure failure => failure
		};

		if (result is ApiFailure failed)
			logger.LogError("Uploading {FileName} for conversion failed: {Reason}", fileName, failed.Message);

		return result;
	}

	/// <summary>
	/// How far the conversion has got. <see cref="ApiFailureKind.NotFound"/> means the server has no such
	/// session — it restarted, or the session was never started — and polling again will not change that.
	/// </summary>
	public Task<ApiResult<ConversionProgress>> GetProgressAsync(string sessionId) =>
		Client.GetApiAsync<ConversionProgress>(
			$"/api/databaseconversion/progress/{Uri.EscapeDataString(sessionId)}", "The server returned no progress.");

	/// <summary>
	/// The finished conversion's result. <see cref="ApiFailureKind.NotFound"/> also covers a conversion
	/// that has not stored its result yet, which can trail 100% progress by a moment.
	/// </summary>
	public Task<ApiResult<ConversionResult>> GetResultAsync(string sessionId) =>
		Client.GetApiAsync<ConversionResult>(
			$"/api/databaseconversion/result/{Uri.EscapeDataString(sessionId)}", "The server returned no result.");

	public async Task<ApiResult<Success>> CancelConversionAsync(string sessionId)
	{
		var result = await Client.PostApiAsync($"/api/databaseconversion/cancel/{Uri.EscapeDataString(sessionId)}");

		if (result is ApiFailure failure)
			logger.LogError("Cancelling conversion {SessionId} failed: {Reason}", sessionId, failure.Message);

		return result;
	}

	private class UploadResponse
	{
		public string SessionId { get; set; } = string.Empty;
		public string Message { get; set; } = string.Empty;
	}
}
