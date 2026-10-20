namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Failed logins per address over the last ten minutes, for <c>connect_fail_limit</c> (PennMUSH's
/// <c>check_fails</c> and <c>mark_failed</c>, <c>src/player.c</c>).
/// </summary>
/// <remarks>
/// An attempt counts toward the limit from the moment it is admitted, so attempts made at the same
/// time from one address cannot all pass the check before any of them fails. <see cref="Fail"/> keeps
/// it as a failure; <see cref="End"/> lets it go otherwise.
/// </remarks>
public interface ILoginFailureTracker
{
	/// <summary>
	/// Admits a login attempt from <paramref name="address"/> on <paramref name="handle"/>, or refuses it
	/// when the address's failures in the window and attempts in progress already reach <paramref name="limit"/>.
	/// </summary>
	bool TryBegin(string address, long handle, uint limit);

	/// <summary>Keeps the attempt on <paramref name="handle"/> as a failure and returns its address's count.</summary>
	int Fail(long handle);

	/// <summary>Ends the attempt on <paramref name="handle"/> without a failure, unless <see cref="Fail"/> already ended it.</summary>
	void End(long handle);

	/// <summary>How many failed logins <paramref name="address"/> has in the window.</summary>
	int Count(string address);
}
