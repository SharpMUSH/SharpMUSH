using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

public class ConfigSchemaService(IHttpClientFactory httpClientFactory, ILogger<ConfigSchemaService> logger)
{
	/// <summary>The configuration schema the server describes its options with.</summary>
	/// <remarks>
	/// This used to answer <see langword="null"/> for every failure, and the configuration home page
	/// swallowed that into a count of 0 settings in every group — a server that did not answer looked
	/// like a server with nothing to configure.
	/// </remarks>
	public async Task<ApiResult<ConfigurationSchema>> GetSchemaAsync()
	{
		var response = await httpClientFactory.CreateClient("api")
			.GetApiAsync<ConfigurationResponse>("/api/configuration", "The server returned no configuration.");

		ApiResult<ConfigurationSchema> result = response switch
		{
			ConfigurationResponse { Schema: { } schema } => schema,
			ConfigurationResponse => new ApiFailure(ApiFailureKind.Unexpected,
				"The configuration response carried no schema."),
			ApiFailure failure => failure
		};

		if (result is ApiFailure failed)
			logger.LogWarning("Failed to load the configuration schema: {Reason}", failed.Message);

		return result;
	}
}
