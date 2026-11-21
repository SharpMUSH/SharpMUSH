using System.Net;
using System.Net.Http.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The portal's casefiles (<c>/api/casefiles</c>): private wiki categories built around a plot, read and changed as
/// the tab's acting character. A refusal comes back as the server's reason, ready to show.
/// </summary>
public sealed class CasefileClient(IHttpClientFactory httpClientFactory, ILogger<CasefileClient> logger)
{
	private HttpClient Http => httpClientFactory.CreateClient("api");

	/// <summary>The casefiles the acting character is in; an empty list when there are none or the server did not answer.</summary>
	public async Task<Result<IReadOnlyList<CasefileSummaryDto>>> ListAsync()
	{
		try
		{
			return await Http.GetFromJsonAsync<List<CasefileSummaryDto>>("api/casefiles") ?? [];
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Listing casefiles failed");
			return new Error<string>(ex.Message);
		}
	}

	/// <summary>One casefile; <c>NotFound</c> when there is none or the acting character is not in it.</summary>
	public async Task<FoundResult<CasefileDto>> GetAsync(string key)
	{
		try
		{
			var response = await Http.GetAsync($"api/casefiles/{Uri.EscapeDataString(key)}");
			if (response.StatusCode == HttpStatusCode.NotFound) return new NotFound();
			response.EnsureSuccessStatusCode();
			return await response.Content.ReadFromJsonAsync<CasefileDto>() is { } casefile
				? casefile
				: new Error<string>("The server sent no casefile.");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Reading a casefile failed");
			return new Error<string>(ex.Message);
		}
	}

	/// <summary>Opens a casefile.</summary>
	public Task<Result<CasefileDto>> CreateAsync(string title, string briefing)
		=> SendAsync(() => Http.PostAsJsonAsync("api/casefiles", new CreateCasefileRequest(title, briefing)));

	/// <summary>
	/// Lets someone in, changes their level, or with a null level takes them out; <c>NotFound</c> when the acting
	/// character took themselves out, so the casefile is no longer theirs to see.
	/// </summary>
	public async Task<FoundResult<CasefileDto>> SetMemberAsync(string key, string who, string? level)
	{
		try
		{
			var response = await Http.PutAsJsonAsync($"api/casefiles/{Uri.EscapeDataString(key)}/members",
				new SetCasefileMemberRequest(who, level));
			if (response.StatusCode == HttpStatusCode.NoContent) return new NotFound();
			if (!response.IsSuccessStatusCode) return new Error<string>(await ReasonAsync(response));
			return await response.Content.ReadFromJsonAsync<CasefileDto>() is { } casefile
				? casefile
				: new Error<string>("The server sent no casefile.");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Changing a casefile's members failed");
			return new Error<string>(ex.Message);
		}
	}

	/// <summary>Posts a development; success carries nothing, since the page reads the log again.</summary>
	public async Task<Result<bool>> PostAsync(string key, string title, string text)
	{
		try
		{
			var response = await Http.PostAsJsonAsync($"api/casefiles/{Uri.EscapeDataString(key)}/developments",
				new PostCasefileDevelopmentRequest(title, text));
			return response.IsSuccessStatusCode ? true : new Error<string>(await ReasonAsync(response));
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Posting a development failed");
			return new Error<string>(ex.Message);
		}
	}

	private async Task<Result<CasefileDto>> SendAsync(Func<Task<HttpResponseMessage>> send)
	{
		try
		{
			var response = await send();
			if (!response.IsSuccessStatusCode) return new Error<string>(await ReasonAsync(response));
			return await response.Content.ReadFromJsonAsync<CasefileDto>() is { } casefile
				? casefile
				: new Error<string>("The server sent no casefile.");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Changing a casefile failed");
			return new Error<string>(ex.Message);
		}
	}

	private static async Task<string> ReasonAsync(HttpResponseMessage response)
	{
		try
		{
			var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
			if (body?.Error is { Length: > 0 } error) return error;
		}
		catch (System.Text.Json.JsonException)
		{
			// A body that is not JSON says nothing more than its status code.
		}

		return $"The server refused ({(int)response.StatusCode}).";
	}

	private sealed record ErrorBody(string? Error);
}
