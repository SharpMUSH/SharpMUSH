using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Coverage for <see cref="CharacterSwitchService"/>, the account-panel switch of the portal's active
/// character. It is portal-only: it commits the active character and reconnects the game hub so live
/// SignalR rebinds; it never touches the terminals (a terminal's character is fixed at connect).
/// </summary>
public class CharacterSwitchServiceTests : BunitContext
{
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	private (AccountAuthService Auth, IConnectionStateService Connection, CharacterSwitchService Service) Build()
	{
		var auth = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(), JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var connection = Substitute.For<IConnectionStateService>();
		return (auth, connection, new CharacterSwitchService(auth, connection));
	}

	[Test]
	public async Task SwitchAsync_commits_the_active_character()
	{
		var (auth, _, service) = Build();

		await service.SwitchAsync(Beta);

		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
		await Assert.That(auth.ActiveCharacter?.Name).IsEqualTo("Beta");
	}

	[Test]
	public async Task SwitchAsync_reconnects_the_game_hub_so_the_portal_follows_the_active_character()
	{
		var (_, connection, service) = Build();

		await service.SwitchAsync(Beta);

		await connection.Received(1).ReconnectAsync();
	}
}
