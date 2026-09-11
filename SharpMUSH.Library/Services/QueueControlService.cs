using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public sealed record QueueInspectionScope(CapabilityActor Actor, IReadOnlySet<string> Scopes);

public interface IQueueControlService
{
	Task<bool> CanAccessLegacyAsync(AnySharpObject actor, long pid, bool mutate, CancellationToken ct = default);
	Task<QueueInspectionScope?> GetInspectionScopeAsync(CapabilityActor actor, CancellationToken ct = default);
	Task<bool> CanInspectAsync(QueueInspectionScope scope, DBRef? owner, DBRef? source, CancellationToken ct = default);
	Task<IReadOnlyList<QueueEntrySnapshot>> ListAsync(CapabilityActor actor, CancellationToken ct = default);
	Task<IReadOnlyList<QueueEntrySnapshot>> ListAsync(CapabilityActor actor, int limit, CancellationToken ct = default);
	Task<QueueControlResult> ChangeAsync(CapabilityActor actor, long pid, bool resume, string reason = "", CancellationToken ct = default);
}

/// <summary>Shared game and portal gates. No executable text or register values leave this API.</summary>
public sealed class QueueControlService(ITaskScheduler scheduler, IAdministrativeCapabilityService capabilities,
	IMediator mediator, IPermissionService permissions) : IQueueControlService
{
	/// <summary>Existing Penn game permissions for PID operations, independent of account role grants.</summary>
	public async Task<bool> CanAccessLegacyAsync(AnySharpObject actor, long pid, bool mutate, CancellationToken ct = default)
	{
		using var requestScope = ExecutionBudget.EnterLinked(ct);
		ct = ExecutionBudget.CurrentToken;
		var entry = scheduler.GetQueueEntry(pid);
		if (entry is null) return false;
		if (mutate ? await actor.IsWizard() || await actor.HasPower("HALT")
			: await actor.IsPriv() || await actor.HasPower("SEE_QUEUE")) return true;
		if (entry.Source is not { IsObjid: true } source) return false;
		return await mediator.Send(new GetObjectNodeQuery(source), ct) is AnySharpObject target
			&& target.Object().DBRef == source
			&& await permissions.Controls(actor, target).AsTask().WaitAsync(ExecutionBudget.CurrentToken);
	}

	/// <summary>Stops after the requested number of visible entries, without materializing the full ledger.</summary>
	public async Task<IReadOnlyList<QueueEntrySnapshot>> ListAsync(CapabilityActor actor, int limit, CancellationToken ct = default)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 101);
		var scope = await GetInspectionScopeAsync(actor, ct);
		if (scope is null) return [];
		var visible = new List<QueueEntrySnapshot>(limit);
		foreach (var entry in scheduler.EnumerateQueueEntries())
		{
			ct.ThrowIfCancellationRequested();
			if (!await CanInspectAsync(scope, entry.Owner, entry.Source, ct)) continue;
			visible.Add(entry);
			if (visible.Count == limit) break;
		}
		return visible;
	}

	public async Task<IReadOnlyList<QueueEntrySnapshot>> ListAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		var scope = await GetInspectionScopeAsync(actor, ct);
		if (scope is null) return [];
		var visible = new List<QueueEntrySnapshot>();
		foreach (var entry in scheduler.GetQueueEntries())
		{
			ct.ThrowIfCancellationRequested();
			if (!await CanInspectAsync(scope, entry.Owner, entry.Source, ct)) continue;
			visible.Add(entry);
		}
		return visible;
	}

	/// <summary>Request-local inspection context, created from fresh persisted roles by trusted entry points.</summary>
	public async Task<QueueInspectionScope?> GetInspectionScopeAsync(CapabilityActor actor, CancellationToken ct = default)
		=> await ValidActor(actor, ct) ? new(actor, await capabilities.GetGrantedScopesAsync(actor, ct)) : null;

	public async Task<bool> CanInspectAsync(QueueInspectionScope scope, DBRef? owner, DBRef? source, CancellationToken ct = default)
	{
		using var requestScope = ExecutionBudget.EnterLinked(ct);
		ct = ExecutionBudget.CurrentToken;
		ct.ThrowIfCancellationRequested();
		var own = owner == scope.Actor.ActiveCharacter;
		return scope.Scopes.Contains(own ? PortalPermission.QueueInspectOwn : PortalPermission.QueueInspect)
			&& (!own || await ControlsCurrentSource(scope.Actor, owner, source, ct));
	}

	public async Task<QueueControlResult> ChangeAsync(CapabilityActor actor, long pid, bool resume, string reason = "", CancellationToken ct = default)
	{
		using var requestScope = ExecutionBudget.EnterLinked(ct);
		ct = ExecutionBudget.CurrentToken;
		ct.ThrowIfCancellationRequested();
		if (!await ValidActor(actor, ct)) return QueueControlResult.NotFound;
		var entry = scheduler.GetQueueEntry(pid);
		if (entry is null) return QueueControlResult.NotFound;
		var own = entry.Owner == actor.ActiveCharacter;
		// An explicit child deny wins even when the parent scope remains granted.
		var scopes = await capabilities.GetGrantedScopesAsync(actor, ct);
		if (!scopes.Contains(own ? PortalPermission.QueueControlOwn : PortalPermission.QueueControl)) return QueueControlResult.NotFound;
		if (own && !await ControlsCurrentSource(actor, entry.Owner, entry.Source, ct)) return QueueControlResult.NotFound;
		ct.ThrowIfCancellationRequested();
		return resume ? await scheduler.ResumePending(pid) : await scheduler.PausePending(pid, reason);
	}

	private async Task<bool> ValidActor(CapabilityActor actor, CancellationToken ct)
		=> actor.ActiveCharacter is { IsObjid: true } active && actor.Executor == active
			&& await capabilities.GetGameActorAsync(active, ct) == actor;

	private async Task<bool> ControlsCurrentSource(CapabilityActor actor, DBRef? owner, DBRef? sourceIdentity, CancellationToken ct)
	{
		if (sourceIdentity is not { IsObjid: true } source) return false;
		if (await mediator.Send(new GetObjectNodeQuery(actor.ActiveCharacter!.Value), ct) is not AnySharpObject player
			|| player is not SharpPlayer character || character.Object.DBRef != actor.ActiveCharacter
			|| await mediator.Send(new GetObjectNodeQuery(source), ct) is not AnySharpObject target
			|| target.Object().DBRef != source) return false;
		if ((await target.Object().Owner.WithCancellation(ct)).Object.DBRef != owner) return false;
		return await permissions.Controls(player, target).AsTask().WaitAsync(ExecutionBudget.CurrentToken);
	}

}
