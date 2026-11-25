using System.Text.Json;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The signed-in player's preferences (<c>api/preferences</c>): the groups installed packages declare, kept on the
/// account or the character the session plays, so they follow the player to any computer. A portal application reads
/// and writes its own group here instead of keeping settings in the browser.
/// </summary>
public sealed class PreferencesService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Every group, with the caller's values.</summary>
	public Task<ApiResult<IReadOnlyList<PreferenceGroupResponse>>> GroupsAsync() =>
		Client.GetApiAsync<IReadOnlyList<PreferenceGroupResponse>>("api/preferences", "The server listed no preferences.");

	/// <summary>One group, <c>&lt;package&gt;.&lt;group&gt;</c>, with the caller's values.</summary>
	public Task<ApiResult<PreferenceGroupResponse>> GroupAsync(string group) =>
		Client.GetApiAsync<PreferenceGroupResponse>($"api/preferences/{Uri.EscapeDataString(group)}", "The server described no group.");

	/// <summary>Sets <paramref name="values"/> in <paramref name="group"/>, all or none, and answers the group as it then is.</summary>
	public Task<ApiResult<PreferenceGroupResponse>> SetAsync(string group, IReadOnlyDictionary<string, JsonElement> values) =>
		Client.PutApiAsync<PreferenceUpdateRequest, PreferenceGroupResponse>($"api/preferences/{Uri.EscapeDataString(group)}",
			new PreferenceUpdateRequest(values), "The server described no group.");

	/// <summary>Returns <paramref name="group"/>, or one key of it, to its defaults.</summary>
	public Task<ApiResult<PreferenceGroupResponse>> ResetAsync(string group, string? key = null) =>
		Client.DeleteApiAsync<PreferenceGroupResponse>(key is null
				? $"api/preferences/{Uri.EscapeDataString(group)}"
				: $"api/preferences/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(key)}",
			"The server described no group.");
}
