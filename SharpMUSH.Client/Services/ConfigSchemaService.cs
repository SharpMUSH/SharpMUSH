using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

public class ConfigSchemaService(IHttpClientFactory httpClientFactory, ILogger<ConfigSchemaService> logger)
{
	private Task<ApiResult<ConfigurationSchema>>? _schema;

	/// <summary>The configuration schema the server describes its options with.</summary>
	/// <remarks>
	/// <para>
	/// This used to answer <see langword="null"/> for every failure, and the configuration home page
	/// swallowed that into a count of 0 settings in every group — a server that did not answer looked
	/// like a server with nothing to configure.
	/// </para>
	/// <para>
	/// The schema is the shape of the options, which only changes with a deploy, so a successful fetch
	/// is kept for the session: the config layout, the home and every section page ask for it. A
	/// failure is not kept, so the next page load tries again.
	/// </para>
	/// </remarks>
	public Task<ApiResult<ConfigurationSchema>> GetSchemaAsync()
	{
		if (_schema is { } pending)
		{
			return pending;
		}

		var fetch = FetchAsync();
		_schema = fetch;
		_ = ForgetIfFailedAsync(fetch);
		return fetch;
	}

	/// <summary>
	/// Drops <paramref name="fetch"/> from the slot once it settles as a failure or a fault, so the
	/// next caller fetches again. It runs after the slot is set, so a fetch that completed
	/// synchronously (a fake handler, a cached response) is still forgotten; and it clears the slot
	/// only while it still holds this fetch.
	/// </summary>
	private async Task ForgetIfFailedAsync(Task<ApiResult<ConfigurationSchema>> fetch)
	{
		await ((Task)fetch).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
		if (fetch is { IsCompletedSuccessfully: true, Result: not ApiFailure })
		{
			return;
		}

		if (ReferenceEquals(_schema, fetch))
		{
			_schema = null;
		}
	}

	private async Task<ApiResult<ConfigurationSchema>> FetchAsync()
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
		{
			logger.LogWarning("Failed to load the configuration schema: {Reason}", failed.Message);
		}

		return result;
	}
}
