using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests;

/// <summary>Service overrides every Server test host applies, whichever factory builds it.</summary>
public static class TestHostServices
{
	/// <summary>
	/// Recurring-job tests drive the job document with a clock of their own, and test hosts can share one
	/// world. A background runner fires those jobs on the real clock and rewrites the document, so no test
	/// host runs one. TUnit wraps every hosted service, so a started runner cannot be picked out and
	/// stopped afterwards; it has to be removed before the host is built.
	/// </summary>
	public static void RemoveRecurringJobRunner(IServiceCollection services)
	{
		foreach (var runner in services.Where(descriptor => descriptor.ImplementationType == typeof(RecurringJobRunner)).ToArray())
			services.Remove(runner);
	}

	/// <summary>
	/// <c>@shutdown/reboot</c> and the portal's restart stop the host, and test hosts are shared. A test host
	/// records the request instead.
	/// </summary>
	public static void RecordServerRestarts(IServiceCollection services)
	{
		services.RemoveAll<IServerRestart>();
		services.AddSingleton<IServerRestart, RecordingServerRestart>();
	}
}

/// <summary>An <see cref="IServerRestart"/> that counts requests and stops nothing.</summary>
public sealed class RecordingServerRestart : IServerRestart
{
	private int _requests;

	/// <summary>How many restarts were asked for.</summary>
	public int Requests => Volatile.Read(ref _requests);

	/// <inheritdoc />
	public bool Pending => false;

	/// <inheritdoc />
	public ValueTask<bool> RestartAsync(string requestedBy)
	{
		Interlocked.Increment(ref _requests);
		return ValueTask.FromResult(true);
	}
}
