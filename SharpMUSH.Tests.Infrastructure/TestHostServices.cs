using Microsoft.Extensions.DependencyInjection;
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
}
