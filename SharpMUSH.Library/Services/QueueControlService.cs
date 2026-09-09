using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public interface IQueueControlService
{
	Task<IReadOnlyList<QueueEntrySnapshot>> ListAsync(CapabilityActor actor, CancellationToken ct = default);
	Task<QueueControlResult> ChangeAsync(CapabilityActor actor, long pid, bool resume, string reason = "", CancellationToken ct = default);
}

/// <summary>Shared game and portal gates. No executable text or register values leave this API.</summary>
public sealed class QueueControlService(ITaskScheduler scheduler, IAdministrativeCapabilityService capabilities,
	IMediator mediator, IPermissionService permissions) : IQueueControlService
{
	public Task<IReadOnlyList<QueueEntrySnapshot>> ListAsync(CapabilityActor actor, CancellationToken ct = default)
		=> Task.FromResult<IReadOnlyList<QueueEntrySnapshot>>([]);
	public Task<QueueControlResult> ChangeAsync(CapabilityActor actor, long pid, bool resume, string reason = "", CancellationToken ct = default)
		=> Task.FromResult(QueueControlResult.NotFound);
}
