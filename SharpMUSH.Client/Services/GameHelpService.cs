namespace SharpMUSH.Client.Services;

/// <summary>A corpus index: its front-page entry rendered to HTML, plus every topic name in it.</summary>
public record GameHelpIndex(string Corpus, string? Topic, string? Html, IReadOnlyList<string> Topics);

/// <summary>
/// The result of asking for one topic. <see cref="Topic"/> is set when exactly one entry answered;
/// <see cref="Candidates"/> is non-empty when several did; both are empty on a miss.
/// </summary>
public record GameHelpEntry(
	string Corpus,
	string RequestedTopic,
	string? Topic,
	string? Markdown,
	string? Html,
	IReadOnlyList<string> Candidates)
{
	/// <summary>True when nothing in the corpus answered to the requested topic.</summary>
	public bool IsMiss => Topic is null && Candidates.Count == 0;
}

/// <summary>
/// Reads the server's shipped help files over <c>/api/help</c>.
/// </summary>
/// <remarks>
/// This is deliberately not the wiki. The portal's help pages used to render wiki slugs, which meant
/// a fresh game answered "This page does not exist yet" to every visitor while the same topics were
/// being served perfectly well over telnet. The server resolves topics here with the very
/// <c>IHelpTopicResolver</c> the <c>help</c> command uses, so the two surfaces agree by construction.
/// <para>
/// Note the separate <see cref="HelpService"/>: that one indexes <c>mush-defs.json</c> for the
/// softcode editor's function drawer, and has nothing to do with the helpfile corpus.
/// </para>
/// </remarks>
public sealed class GameHelpService(IHttpClientFactory httpClientFactory, ILogger<GameHelpService> logger)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Loads the general help index.</summary>
	public Task<ApiResult<GameHelpIndex>> GetIndexAsync() => GetIndexAsync("api/help");

	/// <summary>
	/// Loads the administrator help index. A reader who may not read it gets the server's
	/// <see cref="ApiFailureKind.Forbidden"/>.
	/// </summary>
	public Task<ApiResult<GameHelpIndex>> GetAdminIndexAsync() => GetIndexAsync("api/help/admin");

	/// <summary>Resolves one topic in the general corpus.</summary>
	public Task<ApiResult<GameHelpEntry>> GetEntryAsync(string topic) => GetEntryAsync("api/help/entry", topic);

	/// <summary>Resolves one topic in the administrator corpus.</summary>
	public Task<ApiResult<GameHelpEntry>> GetAdminEntryAsync(string topic) => GetEntryAsync("api/help/admin/entry", topic);

	private async Task<ApiResult<GameHelpIndex>> GetIndexAsync(string route)
	{
		var result = await Client.GetApiAsync<GameHelpIndex>(route, "The server returned no help index.");

		if (result is ApiFailure failure)
			logger.LogWarning("Failed to load the help index from {Route}: {Reason}", route, failure.Message);

		return result;
	}

	private async Task<ApiResult<GameHelpEntry>> GetEntryAsync(string route, string topic)
	{
		// Topic names include '@mail', 'getting started', '%#' and '#-1 exception'. Escaping them as a
		// query value is the only encoding that survives all of them intact.
		var result = await Client.GetApiAsync<GameHelpEntry>(
			$"{route}?topic={Uri.EscapeDataString(topic)}", "The server returned no help entry.");

		switch (result)
		{
			// A miss is a documented answer, not a failure: the page says "no such topic" rather than
			// "something went wrong".
			case ApiFailure { Kind: ApiFailureKind.NotFound }:
				return new GameHelpEntry(string.Empty, topic, null, null, null, []);
			case ApiFailure failure:
				logger.LogWarning("Failed to load help topic {Topic}: {Reason}", topic, failure.Message);
				return failure;
			default:
				return result;
		}
	}
}
