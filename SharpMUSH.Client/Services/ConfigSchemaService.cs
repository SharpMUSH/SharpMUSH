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
		var pending = _schema;
		if (pending is not null)
		{
			return pending;
		}

		// The slot is taken before the fetch starts, so a fetch that completes synchronously (a fake
		// handler, a cached response) can still forget itself on failure before anyone observes it.
		var completion = new TaskCompletionSource<ApiResult<ConfigurationSchema>>();
		_schema = completion.Task;
		_ = RunAsync(completion);
		return completion.Task;
	}

	private async Task RunAsync(TaskCompletionSource<ApiResult<ConfigurationSchema>> completion)
	{
		try
		{
			var result = await FetchAsync();
			if (result is ApiFailure)
			{
				_schema = null;
			}

			completion.SetResult(result);
		}
		catch (Exception ex)
		{
			_schema = null;
			completion.SetException(ex);
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
