namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Failed logins per address over the last ten minutes, for <c>connect_fail_limit</c> (PennMUSH's
/// <c>check_fails</c> and <c>mark_failed</c>, <c>src/player.c</c>).
/// </summary>
public interface ILoginFailureTracker
{
	/// <summary>Records a failed login from <paramref name="address"/> and returns its count in the window, this one included.</summary>
	int Record(string address);

	/// <summary>How many failed logins <paramref name="address"/> has made in the window.</summary>
	int Count(string address);
}
