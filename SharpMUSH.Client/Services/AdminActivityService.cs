using System.Globalization;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the staff activity reports (<c>api/admin/activity</c>).</summary>
public class AdminActivityService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<ActivitySummary>> SummaryAsync(ActivityOptions options) =>
		Client.GetApiAsync<ActivitySummary>(Url("summary", options), "The server returned no summary.");

	public Task<ApiResult<ActivityDrifting>> DriftingAsync(ActivityOptions options) =>
		Client.GetApiAsync<ActivityDrifting>(Url("drifting", options), "The server returned no drifting list.");

	public Task<ApiResult<ActivityCohorts>> CohortsAsync(ActivityOptions options) =>
		Client.GetApiAsync<ActivityCohorts>(Url("cohorts", options), "The server returned no cohorts.");

	public Task<ApiResult<ActivityRooms>> RoomsAsync(ActivityOptions options) =>
		Client.GetApiAsync<ActivityRooms>(Url("rooms", options), "The server returned no rooms.");

	public Task<ApiResult<ActivityHours>> HoursAsync(ActivityOptions options) =>
		Client.GetApiAsync<ActivityHours>(Url("hours", options), "The server returned no hours.");

	private static string Url(string report, ActivityOptions options) =>
		ApiQuery.Build($"api/admin/activity/{report}",
			("until", options.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
			("days", options.Days.ToString(CultureInfo.InvariantCulture)),
			("zone", options.Zone),
			("limit", options.Limit.ToString(CultureInfo.InvariantCulture)));
}
