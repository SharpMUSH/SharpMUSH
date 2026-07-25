using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A <see cref="ServerInfoService"/> that returns fixed server facts without any HTTP, so component
/// tests can pin whether the "play as guest" affordance is offered and what game name the brand
/// shows. Passes a null factory to the base since the overridden methods never touch it.
/// </summary>
public sealed class StubServerInfoService(bool guestsEnabled, string gameName = "SharpMUSH") : ServerInfoService(null!)
{
	public override Task<bool> GuestLoginsEnabledAsync() => Task.FromResult(guestsEnabled);

	public override Task<string> GameNameAsync() => Task.FromResult(gameName);
}
