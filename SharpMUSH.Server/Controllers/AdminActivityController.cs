using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The staff activity reports, counted from poses and scenes: the same numbers <c>activity()</c> returns.
///
/// Routes (each takes ?until=yyyy-MM-dd&amp;days=&amp;baseline=&amp;minimum=&amp;months=&amp;dead=&amp;zone=&amp;limit=):
///   GET api/admin/activity/summary
///   GET api/admin/activity/drifting
///   GET api/admin/activity/cohorts
///   GET api/admin/activity/rooms
///   GET api/admin/activity/hours
/// </summary>
[ApiController]
[Route("api/admin/activity")]
[Authorize(Policy = PortalPermission.PlayersView)]
public class AdminActivityController(IActivityReportService reports) : ControllerBase
{
	[HttpGet("summary")]
	public async Task<ActionResult<ActivitySummary>> Summary([FromQuery] ActivityQuery query, CancellationToken ct)
		=> Answer(await reports.SummaryAsync(query.Options, ct));

	[HttpGet("drifting")]
	public async Task<ActionResult<ActivityDrifting>> Drifting([FromQuery] ActivityQuery query, CancellationToken ct)
		=> Answer(await reports.DriftingAsync(query.Options, ct));

	[HttpGet("cohorts")]
	public async Task<ActionResult<ActivityCohorts>> Cohorts([FromQuery] ActivityQuery query, CancellationToken ct)
		=> Answer(await reports.CohortsAsync(query.Options, ct));

	[HttpGet("rooms")]
	public async Task<ActionResult<ActivityRooms>> Rooms([FromQuery] ActivityQuery query, CancellationToken ct)
		=> Answer(await reports.RoomsAsync(query.Options, ct));

	[HttpGet("hours")]
	public async Task<ActionResult<ActivityHours>> Hours([FromQuery] ActivityQuery query, CancellationToken ct)
		=> Answer(await reports.HoursAsync(query.Options, ct));

	private ActionResult<T> Answer<T>(Result<T> result) where T : notnull => result switch
	{
		T value => Ok(value),
		Error<string> error => BadRequest(error.Value)
	};

	/// <summary>The query string a report reads; anything left out is the report's default.</summary>
	public sealed record ActivityQuery
	{
		public DateOnly? Until { get; init; }
		public int? Days { get; init; }
		public int? Baseline { get; init; }
		public int? Minimum { get; init; }
		public int? Months { get; init; }
		public int? Dead { get; init; }
		public string? Zone { get; init; }
		public int? Limit { get; init; }

		public ActivityOptions Options
		{
			get
			{
				var defaults = new ActivityOptions();
				return new ActivityOptions(Until, Days ?? defaults.Days, Baseline ?? defaults.Baseline,
					Minimum ?? defaults.MinimumDays, Months ?? defaults.Months, Dead ?? defaults.DeadDays, Zone,
					Limit ?? defaults.Limit);
			}
		}
	}
}
