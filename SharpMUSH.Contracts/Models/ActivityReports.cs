namespace SharpMUSH.Library.Models;

/// <summary>
/// What an activity report covers. Every report reads the <see cref="Days"/> days that end on
/// <see cref="Until"/>; the rest only matter to the reports that name them.
/// </summary>
/// <param name="Until">The last day counted, in UTC; null is today.</param>
/// <param name="Days">How many days the report window covers (1-366).</param>
/// <param name="Baseline">The drifting list's earlier window, the days before <see cref="Days"/> (1-366).</param>
/// <param name="MinimumDays">The drifting list's floor: days posed in <see cref="Baseline"/> before someone counts as a regular.</param>
/// <param name="Months">How many approval months the cohorts table goes back (1-36).</param>
/// <param name="DeadDays">A room with no pose in this many days is dead (1-3650).</param>
/// <param name="Zone">The IANA zone the hours heatmap is drawn in; null is UTC.</param>
/// <param name="Limit">The most rows a list returns (1-1000).</param>
public sealed record ActivityOptions(
	DateOnly? Until = null,
	int Days = 30,
	int Baseline = 60,
	int MinimumDays = 4,
	int Months = 12,
	int DeadDays = 90,
	string? Zone = null,
	int Limit = 50)
{
	public const int MaxDays = 366;
	public const int MaxMonths = 36;
	public const int MaxDeadDays = 3650;
	public const int MaxLimit = 1000;
}

/// <summary>The game at a glance: this window against the one before it.</summary>
/// <param name="AsOf">The last day counted.</param>
/// <param name="Days">The window's length.</param>
/// <param name="Approved">Approved characters today.</param>
/// <param name="Posed">Approved characters who posed in the window.</param>
/// <param name="PosedBefore">Approved characters who posed in the window before it.</param>
/// <param name="Poses">Poses in the window.</param>
/// <param name="PosesBefore">Poses in the window before it.</param>
/// <param name="Scenes">Scenes started in the window, or null when nothing records scenes.</param>
/// <param name="ScenesBefore">Scenes started in the window before it, or null.</param>
/// <param name="NewApprovals">Characters approved in the window.</param>
/// <param name="RoomsUsed">Rooms with a pose in the window.</param>
/// <param name="RoomsDead">Rooms with no pose in <see cref="ActivityOptions.DeadDays"/> days.</param>
/// <param name="Drifting">How many are on the drifting list.</param>
/// <param name="Peak">The busiest hour of the week in <see cref="Zone"/>, or null with no poses.</param>
/// <param name="Zone">The zone <see cref="Peak"/> is in.</param>
public sealed record ActivitySummary(
	DateOnly AsOf,
	int Days,
	int Approved,
	int Posed,
	int PosedBefore,
	long Poses,
	long PosesBefore,
	int? Scenes,
	int? ScenesBefore,
	int NewApprovals,
	int RoomsUsed,
	int RoomsDead,
	int Drifting,
	ActivityPeak? Peak,
	string Zone);

/// <summary>One hour of the week.</summary>
/// <param name="Weekday">0 is Sunday.</param>
/// <param name="Hour">0-23.</param>
/// <param name="Players">How many characters posed in that hour, on average.</param>
public sealed record ActivityPeak(int Weekday, int Hour, double Players);

/// <summary>Someone who posed regularly and has slowed down or stopped.</summary>
/// <param name="Objid">The character.</param>
/// <param name="Name">Their name now.</param>
/// <param name="LastPose">When they last posed, or null if not since records began.</param>
/// <param name="RecentDays">Days they posed in the window.</param>
/// <param name="BaselineDays">Days they posed in the baseline before it.</param>
/// <param name="RecentPoses">Poses in the window.</param>
/// <param name="BaselinePoses">Poses in the baseline.</param>
public sealed record ActivityDrifter(
	string Objid,
	string Name,
	DateTimeOffset? LastPose,
	int RecentDays,
	int BaselineDays,
	long RecentPoses,
	long BaselinePoses);

/// <summary>The drifting list, the steepest drop first.</summary>
public sealed record ActivityDrifting(
	DateOnly AsOf,
	int Days,
	int Baseline,
	int MinimumDays,
	int Total,
	IReadOnlyList<ActivityDrifter> Players);

/// <summary>The characters approved in one month, and how many kept posing.</summary>
/// <param name="Month">yyyy-MM.</param>
/// <param name="Approved">How many were approved that month.</param>
/// <param name="Active">How many of them posed in the report window.</param>
/// <param name="Retained">
/// For each 30-day period after approval, the share (0-1) who posed in it; null for a period that has
/// not ended for the whole cohort. Six periods.
/// </param>
public sealed record ActivityCohort(string Month, int Approved, int Active, IReadOnlyList<double?> Retained);

/// <summary>Cohorts by approval month, newest first.</summary>
/// <param name="Undated">Approved characters with no approval on record, counted by the month they were created.</param>
public sealed record ActivityCohorts(DateOnly AsOf, int Days, int Undated, IReadOnlyList<ActivityCohort> Cohorts);

/// <summary>One room's use.</summary>
/// <param name="Objid">The room.</param>
/// <param name="Name">Its name now.</param>
/// <param name="Poses">Poses in the window.</param>
/// <param name="Days">Days with a pose in the window.</param>
/// <param name="People">Different characters who posed there in the window.</param>
/// <param name="Scenes">Scenes started there in the window, or null when nothing records scenes.</param>
/// <param name="LastPose">The last pose there, or null if none since records began.</param>
public sealed record ActivityRoom(
	string Objid,
	string Name,
	long Poses,
	int Days,
	int People,
	int? Scenes,
	DateTimeOffset? LastPose);

/// <summary>The busiest rooms in the window, and the rooms nobody has posed in.</summary>
public sealed record ActivityRooms(
	DateOnly AsOf,
	int Days,
	int DeadDays,
	int UsedTotal,
	int DeadTotal,
	IReadOnlyList<ActivityRoom> Used,
	IReadOnlyList<ActivityRoom> Dead);

/// <summary>When people play: the window folded onto one week.</summary>
/// <param name="Zone">The zone the hours are in.</param>
/// <param name="Players">
/// [weekday][hour]: approved characters who posed in that hour, averaged over the times that hour came
/// round in the window. 0 is Sunday. Hours are counted in UTC and moved to <see cref="Zone"/>, so a zone
/// half an hour off UTC files each UTC hour under the local hour it starts in.
/// </param>
public sealed record ActivityHours(
	DateOnly AsOf,
	int Days,
	string Zone,
	IReadOnlyList<IReadOnlyList<double>> Players);
