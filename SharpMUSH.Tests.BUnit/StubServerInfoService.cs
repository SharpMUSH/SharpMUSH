using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A <see cref="ServerInfoService"/> that returns a fixed guest-logins flag without any HTTP,
/// so component tests can pin whether the "play as guest" affordance is offered. Passes a null
/// factory to the base since the overridden method never touches it.
/// </summary>
public sealed class StubServerInfoService(bool guestsEnabled) : ServerInfoService(null!)
{
	public override Task<bool> GuestLoginsEnabledAsync() => Task.FromResult(guestsEnabled);
}
