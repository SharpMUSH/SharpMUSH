using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.RecurringJobs;

namespace SharpMUSH.Library.Services.RecurringJobs;

public interface IRecurringJobService
{
	Task<RecurringJob[]> ListAsync(CapabilityActor actor, bool all = false, CancellationToken ct = default);
	Task<RecurringJob> CreateAsync(CapabilityActor actor, RecurringJobRequest request, CancellationToken ct = default);
	Task<RecurringJob> ConfigureAsync(CapabilityActor actor, string id, string schedule, string timeZone, bool enabled, CancellationToken ct = default);
	Task DeleteAsync(CapabilityActor actor, string id, CancellationToken ct = default);
	Task InitializeAsync(CancellationToken ct = default);
	Task RunDueAsync(CancellationToken ct = default);
}

public sealed class RecurringJobException(string code, string message) : Exception(message)
{
	public string Code { get; } = code;
}
