using System.Net;
using System.Net.Http.Json;

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
	/// <summary>Loads the general help index, or <see langword="null"/> if the server would not answer.</summary>
	public Task<GameHelpIndex?> GetIndexAsync() => GetIndexAsync("api/help");

	/// <summary>Loads the administrator help index. Returns <see langword="null"/> for a reader who may not read it.</summary>
	public Task<GameHelpIndex?> GetAdminIndexAsync() => GetIndexAsync("api/help/admin");

	/// <summary>Resolves one topic in the general corpus.</summary>
	public Task<GameHelpEntry?> GetEntryAsync(string topic) => GetEntryAsync("api/help/entry", topic);

	/// <summary>Resolves one topic in the administrator corpus.</summary>
	public Task<GameHelpEntry?> GetAdminEntryAsync(string topic) => GetEntryAsync("api/help/admin/entry", topic);

	private async Task<GameHelpIndex?> GetIndexAsync(string route)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<GameHelpIndex>(route);
		}
		catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
		{
			logger.LogWarning(ex, "Failed to load the help index from {Route}.", route);
			return null;
		}
	}

	private async Task<GameHelpEntry?> GetEntryAsync(string route, string topic)
	{
		// Topic names include '@mail', 'getting started', '%#' and '#-1 exception'. Escaping them as a
		// query value is the only encoding that survives all of them intact.
		var url = $"{route}?topic={Uri.EscapeDataString(topic)}";

		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync(url);

			// A miss is a documented answer, not a transport failure: the page says "no such topic"
			// rather than "something went wrong".
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				return await response.Content.ReadFromJsonAsync<GameHelpEntry>()
					?? new GameHelpEntry(string.Empty, topic, null, null, null, []);
			}

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Help lookup for {Topic} returned {Status}.", topic, response.StatusCode);
				return null;
			}

			return await response.Content.ReadFromJsonAsync<GameHelpEntry>();
		}
		catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
		{
			logger.LogWarning(ex, "Failed to load help topic {Topic}.", topic);
			return null;
		}
	}
}
