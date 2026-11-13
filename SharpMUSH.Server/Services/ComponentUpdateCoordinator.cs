using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>What the update agent reports on every poll (<c>POST api/update-agent/poll</c>).</summary>
/// <param name="Protocol">The agent's protocol version; this server speaks <see cref="ComponentUpdateCoordinator.Protocol"/>.</param>
/// <param name="Project">The Compose project it manages.</param>
/// <param name="Tag">The tag the game follows (<c>SHARPMUSH_IMAGE_TAG</c>), or null when the Compose file fixes it.</param>
/// <param name="Components">Each component it found.</param>
/// <param name="AvailableTags">Release tags on the registry, newest first.</param>
/// <param name="RegistryReadAt">When it last read the registry.</param>
/// <param name="Job">The update it is running or last ran, or null.</param>
/// <param name="Error">Its last error, or null.</param>
public sealed record UpdateAgentReport(
	int Protocol,
	string? Project,
	string? Tag,
	IReadOnlyList<UpdateComponentStatus>? Components,
	IReadOnlyList<string>? AvailableTags,
	DateTimeOffset? RegistryReadAt,
	UpdateAgentJobReport? Job,
	string? Error);

/// <summary>The agent's account of one update.</summary>
public sealed record UpdateAgentJobReport(
	Guid Id,
	IReadOnlyList<string> Components,
	string? Tag,
	string State,
	string? Step,
	string? Message,
	DateTimeOffset? FinishedAt);

/// <summary>An update handed to the agent.</summary>
/// <param name="Id">The update's id; the agent runs each id once.</param>
/// <param name="Components">What to replace, in order.</param>
/// <param name="Tag">The tag to move to, or null.</param>
/// <param name="SocketNotBefore">The earliest the socket server may be replaced.</param>
public sealed record UpdateAgentJob(Guid Id, IReadOnlyList<string> Components, string? Tag, DateTimeOffset? SocketNotBefore);

/// <summary>
/// Hands updates asked for in the portal to the update agent, a sidecar that holds the Docker socket so
/// this process never does. The agent polls; nothing here reaches out to it. State is kept in memory:
/// the server is one of the things an update replaces, and the agent reports the update it is running
/// when the new server first hears from it.
/// </summary>
public sealed partial class ComponentUpdateCoordinator(string? token, TimeProvider time, IGameBroadcastService broadcast)
{
	/// <summary>The agent protocol this server speaks.</summary>
	public const int Protocol = 1;

	/// <summary>How long after its last poll the agent counts as connected.</summary>
	public static readonly TimeSpan ConnectedWindow = TimeSpan.FromMinutes(1);

	/// <summary>How long a queued update waits for the agent to take it before it fails.</summary>
	public static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(2);

	/// <summary>The longest players can be warned before the socket server is replaced.</summary>
	public const int MaxSocketDelaySeconds = 1800;

	private readonly Lock _gate = new();
	private readonly byte[]? _tokenHash = token is { Length: >= 32 } ? SHA256.HashData(Encoding.UTF8.GetBytes(token)) : null;
	private UpdateAgentReport? _report;
	private DateTimeOffset? _seenAt;
	private UpdateJobStatus? _job;
	private string? _notice;
	private Guid? _warning;

	/// <summary>Whether this server was given an agent token.</summary>
	public bool Configured => _tokenHash is not null;

	/// <summary>Whether <paramref name="presented"/> is the agent's token, compared in constant time.</summary>
	public bool Authenticates(string? presented) =>
		_tokenHash is not null && presented is { Length: > 0 and <= 512 }
			&& CryptographicOperations.FixedTimeEquals(_tokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(presented)));

	/// <summary>The panel's view of the agent and the current update.</summary>
	public AdminUpdatesStatus Status(int connections, int players)
	{
		AdminUpdatesStatus status;
		lock (_gate)
		{
			ExpireUnclaimed();
			var report = _report;
			status = new AdminUpdatesStatus(
				Configured,
				Connected(),
				_seenAt,
				report?.Project,
				report?.Tag,
				report?.Components ?? [],
				report is { Tag: { } tag } ? (report.AvailableTags ?? []).Where(target => MovesForward(tag, RunningVersion(report), target)).ToList() : [],
				report?.RegistryReadAt,
				report?.Error,
				_job,
				connections,
				players);
		}
		Announce();
		return status;
	}

	/// <summary>
	/// Records a poll and answers with the update the agent should start, if one waits. An update the
	/// agent reports that this process never queued (the server it replaced queued it) is taken over.
	/// </summary>
	public Found<UpdateAgentJob> Poll(UpdateAgentReport report)
	{
		Found<UpdateAgentJob> answer;
		lock (_gate)
		{
			_report = report;
			_seenAt = time.GetUtcNow();
			if (report.Job is { } reported) Follow(reported);
			ExpireUnclaimed();
			// A job whose warning to players is still being sent is not handed over yet.
			answer = _job is { State: UpdateJobStates.Queued } queued && queued.Id != _warning
				&& report.Job is not { State: UpdateJobStates.Running }
				? new UpdateAgentJob(queued.Id, queued.Components, queued.Tag, queued.SocketNotBefore)
				: new NotFound();
		}
		Announce();
		return answer;
	}

	/// <summary>Queues an update, after checking it against what the agent last reported.</summary>
	public async ValueTask<Result<UpdateJobStatus>> QueueAsync(AdminUpdateRequest request, string requestedBy)
	{
		UpdateJobStatus job;
		UpdateJobStatus? before;
		lock (_gate)
		{
			ExpireUnclaimed();
			if (Check(request) is Error<string> refused) return refused;
			var now = time.GetUtcNow();
			var components = ServerComponents.InUpdateOrder.Where(request.Components.Contains).ToList();
			job = new UpdateJobStatus(
				Guid.NewGuid(),
				components,
				request.Tag,
				now,
				requestedBy,
				components.Contains(ServerComponents.SocketServer) ? now.AddSeconds(request.SocketDelaySeconds) : null,
				UpdateJobStates.Queued,
				null,
				null,
				null);
			before = _job;
			_job = job;
			if (job.SocketNotBefore is not null) _warning = job.Id;
		}
		if (job.SocketNotBefore is null) return job;
		try
		{
			await broadcast.BroadcastAsync(SocketWarning(request.SocketDelaySeconds));
		}
		catch
		{
			// Players were not warned, so the agent never gets it: the panel goes back to what it showed.
			lock (_gate)
			{
				if (_job?.Id == job.Id) _job = before;
				// Another update may have been queued meanwhile, once this one expired: its warning is its own.
				if (_warning == job.Id) _warning = null;
			}
			throw;
		}
		lock (_gate)
		{
			if (_warning == job.Id) _warning = null;
			// The warning took longer than the agent may wait: the update expired while it was sent.
			if (_job is not { State: UpdateJobStates.Queued } queued || queued.Id != job.Id)
				return new Error<string>("The update agent did not take the update.");
		}
		return job;
	}

	/// <summary>The broadcast players get before the socket server is replaced.</summary>
	public static string SocketWarning(int delaySeconds) => delaySeconds switch
	{
		<= 0 => "GAME: The connection server is being updated now. You will be disconnected; reconnect in a minute.",
		< 120 => $"GAME: The connection server will be updated in {delaySeconds} seconds. You will be disconnected; reconnect a minute later.",
		_ => $"GAME: The connection server will be updated in {delaySeconds / 60} minutes. You will be disconnected; reconnect a minute later.",
	};

	/// <summary>The broadcast players get when an update they were warned about stops before the socket server.</summary>
	public const string SocketCalledOff = "GAME: The connection server update was called off. You will stay connected.";

	/// <summary>
	/// Whether the game may move from the tag it follows to <paramref name="target"/>. A server refuses a world
	/// a newer release wrote, so a move never goes back: not below the running release, and not off <c>dev</c>,
	/// which runs ahead of every release.
	/// </summary>
	/// <param name="current">The tag the game follows.</param>
	/// <param name="runningVersion">The running server's version label, which says what <c>latest</c> is now.</param>
	/// <param name="target">The tag asked for.</param>
	public static bool MovesForward(string current, string? runningVersion, string target)
	{
		if (target == current) return true;
		if (current == "dev") return false;
		// What latest names is only known once pulled: the agent checks the pulled server's version and
		// refuses one that is not newer.
		if (target is "dev" or "latest") return true;
		return (Release.Parse(current) ?? Release.Parse(runningVersion), Release.Parse(target)) is ({ } from, { } to) && to.CompareTo(from) > 0;
	}

	private static string? RunningVersion(UpdateAgentReport report) =>
		report.Components?.FirstOrDefault(status => status.Component == ServerComponents.Server)?.Version;

	/// <summary>A release number; a prerelease sorts below its release (<c>1.0.0-rc.1</c> &lt; <c>1.0.0</c>).</summary>
	private readonly record struct Release(int Major, int Minor, int Patch, string? Prerelease) : IComparable<Release>
	{
		public static Release? Parse(string? tag) => tag is not null && ReleasePattern().Match(tag) is { Success: true } match
			? new Release(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
				match.Groups[4].Success ? match.Groups[4].Value : null)
			: null;

		public int CompareTo(Release other) =>
			(Major, Minor, Patch) != (other.Major, other.Minor, other.Patch)
				? (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch))
				: (Prerelease, other.Prerelease) switch
				{
					(null, null) => 0,
					(null, _) => 1,
					(_, null) => -1,
					var (mine, theirs) => string.CompareOrdinal(mine, theirs),
				};
	}

	private Result<Success> Check(AdminUpdateRequest request)
	{
		if (!Configured) return new Error<string>("No update agent is configured for this game.");
		if (!Connected() || _report is not { } report) return new Error<string>("The update agent has not been heard from in the last minute.");
		if (_job is { Active: true } || report.Job is { State: UpdateJobStates.Running })
			return new Error<string>("An update is already in progress.");
		if (request.Components is not { Count: > 0 }) return new Error<string>("Choose at least one component.");
		if (request.Components.Distinct().Count() != request.Components.Count
			|| request.Components.Any(component => !ServerComponents.InUpdateOrder.Contains(component)))
			return new Error<string>("Unknown component.");
		var found = report.Components ?? [];
		if (request.Components.FirstOrDefault(component => found.All(status => status.Component != component)) is { } missing)
			return new Error<string>($"The update agent did not find the {missing} component.");
		if (request.SocketDelaySeconds is < 0 or > MaxSocketDelaySeconds)
			return new Error<string>($"The warning must be between 0 and {MaxSocketDelaySeconds} seconds.");
		if (request.Tag is null) return new Success();
		if (!TagPattern().IsMatch(request.Tag)) return new Error<string>("That is not a release tag.");
		if (report.Tag is null) return new Error<string>("This game's Compose file fixes its image tags; change them there.");
		if (request.Tag != report.Tag && !(report.AvailableTags ?? []).Contains(request.Tag))
			return new Error<string>($"The registry has no release tagged {request.Tag}.");
		if (!MovesForward(report.Tag, RunningVersion(report), request.Tag))
			return new Error<string>($"The game cannot go back to {request.Tag}: a release refuses a world a newer one wrote.");
		// One tag for every part: a socket server and a renderer from different builds disagree on capabilities.
		if (request.Components.Count != ServerComponents.InUpdateOrder.Count)
			return new Error<string>("Moving to another version replaces every component.");
		return new Success();
	}

	private void Follow(UpdateAgentJobReport reported)
	{
		if (_job is { } current && current.Id == reported.Id)
		{
			_job = current with { State = reported.State, Step = reported.Step, Message = reported.Message, FinishedAt = reported.FinishedAt };
			if (current.Active && reported.State == UpdateJobStates.Failed) CallOff(current);
			return;
		}
		// An update this process did not queue: the server it replaced queued it. A finished one only
		// fills an empty panel; a running one is shown over a finished one, never over one queued here.
		var adopt = _job is null || (reported.State == UpdateJobStates.Running && _job is not { Active: true });
		if (!adopt) return;
		_job = new UpdateJobStatus(reported.Id, reported.Components, reported.Tag, time.GetUtcNow(), "", null,
			reported.State, reported.Step, reported.Message, reported.FinishedAt);
	}

	private bool Connected() => _seenAt is { } seen && time.GetUtcNow() - seen <= ConnectedWindow;

	private void ExpireUnclaimed()
	{
		if (_job is not { State: UpdateJobStates.Queued } queued || time.GetUtcNow() - queued.RequestedAt <= ClaimWindow) return;
		_job = queued with
		{
			State = UpdateJobStates.Failed,
			Message = "The update agent did not take the update.",
			FinishedAt = time.GetUtcNow()
		};
		CallOff(queued);
	}

	/// <summary>Players were told they would be disconnected; tell them they will not be, unless the socket server was reached.</summary>
	private void CallOff(UpdateJobStatus before)
	{
		if (before.SocketNotBefore is not null && before.Step != ServerComponents.SocketServer) _notice = SocketCalledOff;
	}

	/// <summary>Sends a notice queued under the lock, outside it.</summary>
	private void Announce()
	{
		if (Interlocked.Exchange(ref _notice, null) is { } notice) _ = broadcast.BroadcastAsync(notice).AsTask();
	}

	/// <summary>A release tag (<c>1.2.3</c>, <c>1.2.3-rc.1</c>) or one of the moving tags.</summary>
	[GeneratedRegex(@"^(\d{1,4}\.\d{1,4}\.\d{1,6}(-[0-9A-Za-z.]{1,32})?|latest|dev)$")]
	private static partial Regex TagPattern();

	[GeneratedRegex(@"^(\d{1,4})\.(\d{1,4})\.(\d{1,6})(?:-([0-9A-Za-z.]{1,32}))?$")]
	private static partial Regex ReleasePattern();
}
