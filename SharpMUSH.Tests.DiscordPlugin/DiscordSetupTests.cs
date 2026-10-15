using Discord;
using SharpMUSH.Plugins.Discord;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordSetupTests
{
	private const ulong Application = 1234567890123456789;

	[Test]
	public async Task TheInviteAsksForExactlyThePermissionsTheBotUses()
	{
		// Discord's bits: view channel 1<<10, send 1<<11, manage messages 1<<13, history 1<<16, manage roles 1<<28,
		// manage webhooks 1<<29, send in threads 1<<38, create events 1<<44.
		const ulong expected = (1UL << 10) | (1UL << 11) | (1UL << 13) | (1UL << 16) | (1UL << 28) | (1UL << 29)
			| (1UL << 38) | (1UL << 44);

		await Assert.That(DiscordSetup.InviteUrl(Application)).IsEqualTo(
			$"https://discord.com/oauth2/authorize?client_id={Application}&permissions={expected}&scope=bot+applications.commands");
	}

	[Test]
	public async Task BothPrivilegedIntentsAreMissing_WhenNeitherIsOn()
	{
		await Assert.That(DiscordSetup.MissingIntents(default(ApplicationFlags)))
			.IsEquivalentTo(new[] { "Message Content Intent", "Server Members Intent" });
	}

	[Test]
	public async Task TheLimitedFlagsCountAsOn()
	{
		await Assert.That(DiscordSetup.MissingIntents(ApplicationFlags.GatewayMessageContentLimited | ApplicationFlags.GatewayGuildMembersLimited))
			.IsEmpty();
	}

	[Test]
	public async Task WithNoToken_TheFirstStepSendsTheAdminToTheDeveloperPortal()
	{
		var view = DiscordSetup.View(new DiscordGatewayState(DiscordGatewayStatus.NoToken));

		await Assert.That(view.InviteUrl).IsNull();
		await Assert.That(view.Steps.Select(step => step.Done)).IsEquivalentTo(new[] { false, false, false, false, false });
		await Assert.That(view.Steps[0].Link).IsEqualTo(DiscordSetup.DeveloperPortal);
	}

	[Test]
	public async Task AMissingIntent_IsNamedWithTheBotPage()
	{
		var view = DiscordSetup.View(new DiscordGatewayState(DiscordGatewayStatus.IntentsOff, Application, "Bot",
			["Server Members Intent"]));

		await Assert.That(view.Steps[0].Done).IsTrue();
		await Assert.That(view.Steps[1].Done).IsFalse();
		await Assert.That(view.Steps[1].Todo!).Contains("Server Members Intent");
		await Assert.That(view.Steps[1].Link).IsEqualTo($"{DiscordSetup.DeveloperPortal}/{Application}/bot");
	}

	[Test]
	public async Task ConnectedInNoServer_TheLastStepIsTheInvite()
	{
		var view = DiscordSetup.View(new DiscordGatewayState(DiscordGatewayStatus.Connected, Application, "Bot", [], false));

		await Assert.That(view.Steps.Select(step => step.Done)).IsEquivalentTo(new[] { true, true, true, true, false });
		await Assert.That(view.Steps[4].Link).IsEqualTo(DiscordSetup.InviteUrl(Application));
	}

	[Test]
	public async Task ConnectedInAServer_EveryStepIsDone()
	{
		var view = DiscordSetup.View(new DiscordGatewayState(DiscordGatewayStatus.Connected, Application, "Bot", [], false, Guilds: 1));

		await Assert.That(view.Steps.All(step => step.Done)).IsTrue();
	}

	[Test]
	public async Task APublicBot_IsAdvisedToBeMadePrivate()
	{
		var view = DiscordSetup.View(new DiscordGatewayState(DiscordGatewayStatus.Connected, Application, "Bot", [], true, Guilds: 1));

		var step = view.Steps.Single(step => step.Key == "private");
		await Assert.That(step.Done).IsFalse();
		await Assert.That(step.Link).IsEqualTo($"{DiscordSetup.DeveloperPortal}/{Application}/bot");
	}
}
