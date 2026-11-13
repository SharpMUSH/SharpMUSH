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

	/// <summary>Whether this server was given an agent token.</summary>
	public bool Configured => _tokenHash is not null;

	/// <summary>Whether <paramref name="presented"/> is the agent's token, compared in constant time.</summary>
	public bool Authenticates(string? presented) =>
		_tokenHash is not null && presented is { Length: > 0 and <= 512 }
			&& CryptographicOperations.FixedTimeEquals(_tokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(presented)));

	/// <summary>The panel's view of the agent and the current update.</summary>
	public AdminUpdatesStatus Status(int connections, int players)
	{
		lock (_gate)
		{
			ExpireUnclaimed();
			var report = _report;
			return new AdminUpdatesStatus(
				Configured,
				Connected(),
				_seenAt,
				report?.Project,
				report?.Tag,
				report?.Components ?? [],
				report?.AvailableTags ?? [],
				report?.RegistryReadAt,
				report?.Error,
				_job,
				connections,
				players);
		}
	}

	/// <summary>
	/// Records a poll and answers with the update the agent should start, if one waits. An update the
	/// agent reports that this process never queued (the server it replaced queued it) is taken over.
	/// </summary>
	public Found<UpdateAgentJob> Poll(UpdateAgentReport report)
	{
		lock (_gate)
		{
			_report = report;
			_seenAt = time.GetUtcNow();
			if (report.Job is { } reported) Follow(reported);
			ExpireUnclaimed();
			if (_job is not { State: UpdateJobStates.Queued } queued) return new NotFound();
			// The agent is busy with another update: it is handed this one when that one ends.
			if (report.Job is { State: UpdateJobStates.Running }) return new NotFound();
			return new UpdateAgentJob(queued.Id, queued.Components, queued.Tag, queued.SocketNotBefore);
		}
	}

	/// <summary>Queues an update, after checking it against what the agent last reported.</summary>
	public async ValueTask<Result<UpdateJobStatus>> QueueAsync(AdminUpdateRequest request, string requestedBy)
	{
		UpdateJobStatus job;
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
			_job = job;
		}
		if (job.SocketNotBefore is not null) await broadcast.BroadcastAsync(SocketWarning(request.SocketDelaySeconds));
		return job;
	}

	/// <summary>The broadcast players get before the socket server is replaced.</summary>
	public static string SocketWarning(int delaySeconds) => delaySeconds switch
	{
		<= 0 => "GAME: The connection server is being updated now. You will be disconnected; reconnect in a minute.",
		< 120 => $"GAME: The connection server will be updated in {delaySeconds} seconds. You will be disconnected; reconnect a minute later.",
		_ => $"GAME: The connection server will be updated in {delaySeconds / 60} minutes. You will be disconnected; reconnect a minute later.",
	};

	private Result<Success> Check(AdminUpdateRequest request)
	{
		if (!Configured) return new Error<string>("No update agent is configured for this game.");
		if (!Connected() || _report is not { } report) return new Error<string>("The update agent has not been heard from in the last minute.");
		if (_job is { Active: true }) return new Error<string>("An update is already in progress.");
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
		if (_job is { State: UpdateJobStates.Queued } queued && time.GetUtcNow() - queued.RequestedAt > ClaimWindow)
			_job = queued with
			{
				State = UpdateJobStates.Failed,
				Message = "The update agent did not take the update.",
				FinishedAt = time.GetUtcNow()
			};
	}

	/// <summary>A release tag (<c>1.2.3</c>, <c>1.2.3-rc.1</c>) or one of the moving tags.</summary>
	[GeneratedRegex(@"^(\d{1,4}\.\d{1,4}\.\d{1,6}(-[0-9A-Za-z.]{1,32})?|latest|dev)$")]
	private static partial Regex TagPattern();
}
