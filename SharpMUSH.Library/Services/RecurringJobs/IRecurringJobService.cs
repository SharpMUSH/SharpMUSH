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

	/// <summary>The jobs <paramref name="packageId"/> declared.</summary>
	Task<RecurringJob[]> GetPackageJobsAsync(string packageId, CancellationToken ct = default);

	/// <summary>
	/// Makes <paramref name="packageId"/>'s jobs exactly <paramref name="jobs"/>, matched by ref: a new one
	/// is created enabled, a kept one takes the new definition and keeps whether it is enabled, and one no
	/// longer listed is deleted. Returns the package's jobs as they were, for <see cref="RestorePackageJobsAsync"/>.
	/// </summary>
	Task<RecurringJob[]> SetPackageJobsAsync(string packageId, IReadOnlyList<PackageJobDefinition> jobs, CancellationToken ct = default);

	/// <summary>Puts <paramref name="packageId"/>'s jobs back as <paramref name="jobs"/> exactly: the undo of a package operation.</summary>
	Task RestorePackageJobsAsync(string packageId, IReadOnlyList<RecurringJob> jobs, CancellationToken ct = default);
	Task RunDueAsync(CancellationToken ct = default);
}

public sealed class RecurringJobException(string code, string message) : Exception(message)
{
	public string Code { get; } = code;
}
