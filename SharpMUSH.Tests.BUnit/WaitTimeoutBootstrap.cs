using Bunit;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// Raises bUnit's default wait from one second to ten. A wait returns as soon as its condition
/// holds, so a passing test pays nothing for the longer limit; only a wait that is going to fail
/// takes longer to say so. One second is shorter than a render chain behind a faked HTTP answer
/// takes on a CI runner whose cores the parallel suite already has busy: the wait gave up after
/// its first check with the page's answer still queued.
/// </summary>
public static class WaitTimeoutBootstrap
{
	[Before(TestSession)]
	public static void Configure() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);
}
