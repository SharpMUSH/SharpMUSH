using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;

namespace SharpMUSH.Library.Services;

public interface IQueueDiagnosticsService
{
	Task<OneOf<QueueDiagnosticsReport, DiagnosticsError>> InspectAsync(CapabilityActor actor, int limit = 50,
		Guid? beforeCursor = null, CancellationToken ct = default);
	Task<OneOf<Guid, DiagnosticsError>> StartProfileAsync(CapabilityActor actor, int seconds = 60, CancellationToken ct = default);
	Task<OneOf<Success, DiagnosticsError>> StopProfileAsync(CapabilityActor actor, CancellationToken ct = default);
	Task CollectProfilesAsync(CancellationToken ct = default);
}

/// <summary>One fresh authorization boundary for game and portal diagnostic views.</summary>
public sealed class QueueDiagnosticsService(QueueDiagnosticsRecorder recorder, IQueueControlService queues,
	ILogger<QueueDiagnosticsService> logger) : IQueueDiagnosticsService
{
	private readonly SemaphoreSlim _collector = new(1, 1);
	// At most one bounded mailbox batch survives cancellation; do not drain again until it finishes.
	private Dictionary<Guid, QueueProfileSample[]>? _pendingProfileSamples;
	private static bool CanInspect(QueueInspectionScope? scope) => scope is not null
		&& (scope.Scopes.Contains(PortalPermission.QueueInspect) || scope.Scopes.Contains(PortalPermission.QueueInspectOwn));
	private static bool CanProfile(QueueInspectionScope? scope) => CanInspect(scope)
		&& scope!.Scopes.Contains(PortalPermission.DiagnosticsProfile);

	public async Task<OneOf<QueueDiagnosticsReport, DiagnosticsError>> InspectAsync(CapabilityActor actor, int limit = 50,
		Guid? beforeCursor = null, CancellationToken ct = default)
	{
		if (limit is < 1 or > 100 || beforeCursor == Guid.Empty) return DiagnosticsError.InvalidRequest;
		var scope = await queues.GetInspectionScopeAsync(actor, ct);
		if (!CanInspect(scope)) return DiagnosticsError.PermissionDenied;
		IReadOnlyList<SharpMUSH.Library.Models.SchedulerModels.QueueEntrySnapshot> active;
		try { active = await queues.ListAsync(actor, 101, ct); }
		catch (NotSupportedException) { return DiagnosticsError.Unsupported; }
		var current = active.Take(100).Select(entry => new DiagnosticQueueRow(entry.Pid,
			entry.Source?.ToString(), entry.Owner?.ToString(), entry.Kind, entry.State.ToString(), entry.SourceAttribute,
			entry.EnqueuedAt, entry.StartedAt, null, entry.WaitDuration, entry.ExecutionDuration, entry.InvocationCount, null)).ToArray();
		var recent = recorder.Recent();
		long? beforeSequence = null;
		if (beforeCursor is { } cursor)
		{
			var anchor = recent.FirstOrDefault(row => row.Cursor == cursor);
			if (anchor is null || !await queues.CanInspectAsync(scope!, anchor.Owner, anchor.Source, ct))
				return DiagnosticsError.InvalidRequest;
			beforeSequence = anchor.Sequence;
		}
		var history = new List<DiagnosticQueueRow>();
		Guid? next = null;
		foreach (var row in recent)
		{
			if (beforeSequence is { } before && row.Sequence >= before) continue;
			if (!await queues.CanInspectAsync(scope!, row.Owner, row.Source, ct)) continue;
			history.Add(new(row.Pid, row.Source?.ToString(), row.Owner?.ToString(), row.Kind,
				row.Outcome.ToString(), row.SourceAttribute, row.EnqueuedAt, row.StartedAt, row.EndedAt,
				row.WaitDuration, row.ExecutionDuration, row.InvocationCount, row.FailedInvocations));
			if (history.Count == limit) next = row.Cursor;
			if (history.Count > limit) break;
		}
		if (history.Count <= limit) next = null;
		DiagnosticProfileReport? profile = null;
		var registration = recorder.ProfileRegistrations().SingleOrDefault(p => p.Actor == actor);
		if (registration is not null)
		{
			if (!CanProfile(scope)) recorder.StopProfile(registration.Id, discard: true);
			else if (recorder.Profile(registration.Id) is { } snapshot)
			{
				var rows = new List<DiagnosticProfileRow>();
				foreach (var row in snapshot.Aggregates.OrderByDescending(row => row.InclusiveMilliseconds))
				{
					if (!await queues.CanInspectAsync(scope!, row.Owner, row.Source, ct)) continue;
					rows.Add(new(row.Source?.ToString(), row.Owner?.ToString(), row.SourceAttribute, row.Kind.ToString(), row.Name,
						row.Count, row.Failures, row.InclusiveMilliseconds, row.MaximumMilliseconds));
				}
				// Mailbox loss includes samples not yet authorized. Publishing that count would reveal
				// otherwise invisible activity; capacity and sampling limitations are documented instead.
				profile = new(registration.StartedAt, registration.ExpiresAt, snapshot.Recording, rows);
			}
		}
		return new QueueDiagnosticsReport(current, history.Take(limit).ToArray(), profile, CanProfile(scope), next, active.Count > 100);
	}

	public async Task<OneOf<Guid, DiagnosticsError>> StartProfileAsync(CapabilityActor actor, int seconds = 60, CancellationToken ct = default)
	{
		if (seconds is < 1 or > 300) return DiagnosticsError.InvalidDuration;
		if (!CanProfile(await queues.GetInspectionScopeAsync(actor, ct))) return DiagnosticsError.PermissionDenied;
		// A capture must have a usable reporting path before it consumes bounded recorder capacity.
		try { await queues.ListAsync(actor, 1, ct); }
		catch (NotSupportedException) { return DiagnosticsError.Unsupported; }
		var profile = recorder.StartProfile(actor, TimeSpan.FromSeconds(seconds));
		return profile is null ? DiagnosticsError.CapacityExceeded : profile.Id;
	}
	public async Task<OneOf<Success, DiagnosticsError>> StopProfileAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		if (!CanProfile(await queues.GetInspectionScopeAsync(actor, ct))) return DiagnosticsError.PermissionDenied;
		var profile = recorder.ProfileRegistrations().SingleOrDefault(p => p.Actor == actor);
		if (profile is null) return DiagnosticsError.NotFound;
		recorder.StopProfile(profile.Id);
		// Stop fences writers before draining. Await the same collector gate so an in-flight
		// background batch also finishes authorization and publication before success returns.
		await CollectProfilesAsync(ct);
		return new Success();
	}
	public async Task CollectProfilesAsync(CancellationToken ct = default)
	{
		await _collector.WaitAsync(ct);
		try
		{
			var batches = _pendingProfileSamples is null ? 1 : 2;
			for (var batchIndex = 0; batchIndex < batches; batchIndex++)
			{
				var samples = _pendingProfileSamples ??= recorder.DrainProfileSamples().GroupBy(s => s.ProfileId).ToDictionary(g => g.Key, g => g.ToArray());
				foreach (var profile in recorder.ProfileRegistrations())
				{
					try
					{
						var scope = await queues.GetInspectionScopeAsync(profile.Actor, ct);
						if (!CanProfile(scope)) { recorder.StopProfile(profile.Id, discard: true); samples.Remove(profile.Id); continue; }
						if (!samples.TryGetValue(profile.Id, out var batch)) continue;
						var allowed = new List<QueueProfileSample>();
						var visibility = new Dictionary<(DBRef? Owner, DBRef? Source), bool>();
						foreach (var sample in batch)
						{
							var key = (sample.Owner, sample.Source);
							if (!visibility.TryGetValue(key, out var visible))
								visibility[key] = visible = await queues.CanInspectAsync(scope!, sample.Owner, sample.Source, ct);
							if (visible) allowed.Add(sample);
						}
						recorder.ApplyProfileSamples(profile.Id, allowed);
						samples.Remove(profile.Id);
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
					catch
					{
						recorder.StopProfile(profile.Id, discard: true);
						samples.Remove(profile.Id);
						logger.LogWarning("Profiling session ended because authorization could not be refreshed");
					}
				}
				_pendingProfileSamples = null;
			}
		}
		finally { _collector.Release(); }
	}
}
