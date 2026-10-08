using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A <see cref="ServerInfoService"/> that returns fixed server facts without any HTTP, so component
/// tests can pin whether the "play as guest" affordance is offered, what game name the brand shows,
/// which optional applications the game has on (a new game's, unless given), the game's portal_logo (none,
/// unless given), and one portal build that
/// never changes. Passes a null factory to the base since the overridden methods never touch it.
/// </summary>
public sealed class StubServerInfoService(bool guestsEnabled, string gameName = "SharpMUSH",
	IReadOnlyList<string>? features = null, string? logo = null) : ServerInfoService(null!)
{
	public override Task<string?> LogoAsync() => Task.FromResult(logo);

	public override Task<bool> GuestLoginsEnabledAsync() => Task.FromResult(guestsEnabled);

	public override Task<string> GameNameAsync() => Task.FromResult(gameName);

	public override Task<bool> HasFeatureAsync(string feature) =>
		Task.FromResult((features ?? GameFeatures.Defaults).Contains(feature, StringComparer.OrdinalIgnoreCase));

	public override Task<string?> BuildIdAsync() => Task.FromResult<string?>("stub-build");

	public override Task<string?> CurrentBuildIdAsync() => Task.FromResult<string?>("stub-build");
}
