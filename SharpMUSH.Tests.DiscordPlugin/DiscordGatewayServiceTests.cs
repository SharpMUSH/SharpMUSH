using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>The gateway's state before it reaches Discord: neither case opens a connection.</summary>
public class DiscordGatewayServiceTests
{
	[Test, Timeout(30_000)]
	public async Task WithNoToken_ItWaits(CancellationToken cancellationToken)
	{
		var secrets = Substitute.For<ISecretStore>();
		secrets.GetAsync(SharpMUSH.Plugins.Discord.DiscordPlugin.Token.Name, Arg.Any<CancellationToken>()).Returns(new FoundResult<string>(new NotFound()));
		using var gateway = new DiscordGatewayService(secrets, [], NullLogger<DiscordGatewayService>.Instance);

		await gateway.StartAsync(cancellationToken);
		var state = await SettledAsync(gateway, cancellationToken);
		await gateway.StopAsync(cancellationToken);

		await Assert.That(state.Status).IsEqualTo(DiscordGatewayStatus.NoToken);
	}

	[Test, Timeout(30_000)]
	public async Task Reconnecting_ReadsTheTokenAgain(CancellationToken cancellationToken)
	{
		var secrets = Substitute.For<ISecretStore>();
		secrets.GetAsync(SharpMUSH.Plugins.Discord.DiscordPlugin.Token.Name, Arg.Any<CancellationToken>())
			.Returns(new FoundResult<string>(new NotFound()), new FoundResult<string>("not a bot token"));
		using var gateway = new DiscordGatewayService(secrets, [], NullLogger<DiscordGatewayService>.Instance);

		await gateway.StartAsync(cancellationToken);
		await SettledAsync(gateway, cancellationToken);
		gateway.Reconnect();
		await Assert.That(gateway.State.Status).IsEqualTo(DiscordGatewayStatus.Connecting)
			.Because("whoever asked reads the reconnect as under way, not the state it replaces");
		while (gateway.State.Status is DiscordGatewayStatus.NoToken or DiscordGatewayStatus.Connecting)
		{
			await Task.Delay(10, cancellationToken);
		}

		var state = gateway.State;
		await gateway.StopAsync(cancellationToken);

		await Assert.That(state.Status).IsEqualTo(DiscordGatewayStatus.BadToken);
	}

	private static async Task<DiscordGatewayState> SettledAsync(DiscordGatewayService gateway, CancellationToken cancellationToken)
	{
		while (gateway.State.Status == DiscordGatewayStatus.Connecting)
		{
			await Task.Delay(10, cancellationToken);
		}

		return gateway.State;
	}
}
