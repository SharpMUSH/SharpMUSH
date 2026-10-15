using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Activity;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The staff activity reports: what <see cref="IActivityLedger"/> counted, read against the approved
/// characters and the rooms that exist now. Behind <c>activity()</c> and <c>api/admin/activity</c>.
/// </summary>
public interface IActivityReportService
{
	ValueTask<Result<ActivitySummary>> SummaryAsync(ActivityOptions options, CancellationToken ct = default);

	ValueTask<Result<ActivityDrifting>> DriftingAsync(ActivityOptions options, CancellationToken ct = default);

	ValueTask<Result<ActivityCohorts>> CohortsAsync(ActivityOptions options, CancellationToken ct = default);

	ValueTask<Result<ActivityRooms>> RoomsAsync(ActivityOptions options, CancellationToken ct = default);

	ValueTask<Result<ActivityHours>> HoursAsync(ActivityOptions options, CancellationToken ct = default);
}

/// <inheritdoc cref="IActivityReportService"/>
public sealed class ActivityReportService(
	IMediator mediator,
	IActivityLedger ledger,
	IAccountService accounts,
	IEnumerable<ActivitySource> sources,
	TimeProvider clock) : IActivityReportService
{
	public ValueTask<Result<ActivitySummary>> SummaryAsync(ActivityOptions options, CancellationToken ct = default)
		=> BuildAsync(options, (data, o, zone) => ActivityReports.Summary(data, o, zone), ct);

	public ValueTask<Result<ActivityDrifting>> DriftingAsync(ActivityOptions options, CancellationToken ct = default)
		=> BuildAsync(options, (data, o, _) => ActivityReports.Drifting(data, o), ct);

	public ValueTask<Result<ActivityCohorts>> CohortsAsync(ActivityOptions options, CancellationToken ct = default)
		=> BuildAsync(options, (data, o, _) => ActivityReports.Cohorts(data, o), ct);

	public ValueTask<Result<ActivityRooms>> RoomsAsync(ActivityOptions options, CancellationToken ct = default)
		=> BuildAsync(options, (data, o, _) => ActivityReports.Rooms(data, o), ct);

	public ValueTask<Result<ActivityHours>> HoursAsync(ActivityOptions options, CancellationToken ct = default)
		=> BuildAsync(options, (data, o, zone) => ActivityReports.Hours(data, o, zone), ct);

	/// <summary>Checks <paramref name="options"/>, then gathers the data and works the report out.</summary>
	private async ValueTask<Result<T>> BuildAsync<T>(ActivityOptions options,
		Func<ActivityData, ActivityOptions, TimeZoneInfo, T> report, CancellationToken ct)
		=> Check(options) switch
		{
			TimeZoneInfo zone => report(await GatherAsync(options, ct), options, zone),
			Error<string> error => error
		};

	/// <summary>The zone the options name, or why the options cannot be used.</summary>
	public static Result<TimeZoneInfo> Check(ActivityOptions options)
	{
		if (options.Days is < 1 or > ActivityOptions.MaxDays)
			return new Error<string>($"days must be from 1 to {ActivityOptions.MaxDays}.");
		if (options.Baseline is < 1 or > ActivityOptions.MaxDays)
			return new Error<string>($"baseline must be from 1 to {ActivityOptions.MaxDays}.");
		if (options.MinimumDays < 1 || options.MinimumDays > options.Baseline)
			return new Error<string>("minimum must be from 1 to the baseline's length.");
		if (options.Months is < 1 or > ActivityOptions.MaxMonths)
			return new Error<string>($"months must be from 1 to {ActivityOptions.MaxMonths}.");
		if (options.DeadDays is < 1 or > ActivityOptions.MaxDeadDays)
			return new Error<string>($"dead must be from 1 to {ActivityOptions.MaxDeadDays}.");
		if (options.Limit is < 1 or > ActivityOptions.MaxLimit)
			return new Error<string>($"limit must be from 1 to {ActivityOptions.MaxLimit}.");
		if (string.IsNullOrWhiteSpace(options.Zone)) return TimeZoneInfo.Utc;
		return TimeZoneInfo.TryFindSystemTimeZoneById(options.Zone, out var zone)
			? zone
			: new Error<string>($"{options.Zone} is not a time zone.");
	}

	private async ValueTask<ActivityData> GatherAsync(ActivityOptions options, CancellationToken ct)
	{
		// What was counted since the last flush belongs in the report.
		await ledger.FlushAsync(ct);

		var asOf = options.Until ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
		var window = await mediator.Send(new GetActivityQuery(ActivityReports.FirstDayNeeded(asOf, options), asOf.DayNumber), ct);
		var approvals = await ApprovalDaysAsync(ct);

		var approved = new List<ActivityCharacter>();
		await foreach (var player in mediator.CreateStream(new GetAllPlayersQuery(), ct))
		{
			var obj = player.Object;
			if (!await new AnySharpObject(player).IsApproved()) continue;
			approved.Add(new ActivityCharacter(obj.Key, obj.DBRef.ToString(), obj.Name, obj.CreationTime,
				approvals.TryGetValue(obj.Key, out var day) ? day : null));
		}

		var rooms = new List<ActivityPlace>();
		await foreach (var room in mediator.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { Types = ["ROOM"] }), ct))
		{
			rooms.Add(new ActivityPlace(room.Key, room.DBRef.ToString(), room.Name, room.CreationTime));
		}

		return new ActivityData(asOf, approved, rooms, window, sources.Any(s => s.Name == ActivitySource.Scenes));
	}

	/// <summary>
	/// The first day the audit log shows each character given the <c>approved</c> role, by dbref number: on
	/// the character itself, or on its account (which approves every character the account holds now).
	/// </summary>
	private async ValueTask<Dictionary<long, DateOnly>> ApprovalDaysAsync(CancellationToken ct)
	{
		var days = new Dictionary<long, DateOnly>();
		string? before = null;
		do
		{
			var page = await mediator.Send(new GetAuditEntriesQuery(new AuditFilter(Action: AuditActions.RoleAssign,
				Before: before, Limit: AuditFilter.MaxLimit)), ct);
			foreach (var entry in page.Entries)
			{
				if (entry.Details != Authorization.BuiltInRoles.ApprovedSlug || entry.Target is not { } target) continue;
				var day = DateOnly.FromDateTime(entry.At.UtcDateTime);
				foreach (var dbref in await ApprovedCharactersAsync(target, ct))
				{
					// Newest first, so the last one seen is the earliest.
					days[dbref] = day;
				}
			}

			before = page.Next;
		} while (before is not null);

		return days;
	}

	private async ValueTask<IEnumerable<long>> ApprovedCharactersAsync(AuditTarget target, CancellationToken ct)
		=> target.Kind switch
		{
			AuditTargetKinds.Character when DBRef.TryParse(target.Id, out var dbref) && dbref is { } found => [found.Number],
			AuditTargetKinds.Account => (await accounts.GetCharactersAsync(target.Id, ct)).Select(p => (long)p.Object.Key),
			_ => []
		};
}
