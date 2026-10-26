using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>What the bot reads from the game: an account's standing and the roles it holds.</summary>
public class DiscordGameSideTests
{
	private DiscordPluginHarness _harness = null!;

	[Before(Test)]
	public async Task Start() => _harness = await DiscordPluginHarness.StartAsync();

	[After(Test)]
	public async Task Stop() => await _harness.DisposeAsync();

	private void Account(string id, bool active)
		=> _harness.Accounts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = id, Username = id, Status = active ? AccountStatus.Active : AccountStatus.Disabled });

	[Test]
	public async Task Standing_IsActiveBannedOrInactive()
	{
		Account("active", true);
		Account("banned", false);
		Account("closed", false);
		_harness.Accounts.GetBanAsync("banned", Arg.Any<CancellationToken>())
			.Returns(new AccountBan("banned", "spam", null, DiscordPluginHarness.Now, null));
		var standings = _harness.Get<IDiscordStandings>();

		await Assert.That(await standings.StandingAsync("active", CancellationToken.None)).IsEqualTo(DiscordStanding.Active);
		await Assert.That(await standings.StandingAsync("banned", CancellationToken.None)).IsEqualTo(DiscordStanding.Banned);
		await Assert.That(await standings.StandingAsync("closed", CancellationToken.None)).IsEqualTo(DiscordStanding.Inactive);
		await Assert.That(await standings.StandingAsync("gone", CancellationToken.None)).IsEqualTo(DiscordStanding.Inactive);
	}

	[Test]
	public async Task HeldRoles_AreTheActiveAccountsRolesAndNoneOtherwise()
	{
		Account("active", true);
		Account("closed", false);
		_harness.Capabilities.GetContextAsync(Arg.Is<CapabilityActor>(actor => actor.AccountId == "active"), Arg.Any<CancellationToken>())
			.Returns(new PermissionContext([new SharpRole { Slug = "builder", Name = "Builder" }], new Dictionary<string, PermissionState>(), false));
		var holders = _harness.Get<IDiscordRoleHolders>();

		var held = await holders.HeldAsync("active", CancellationToken.None);

		await Assert.That(held).IsNotNull();
		await Assert.That(held!.Contains("BUILDER")).IsTrue();
		await Assert.That(await holders.HeldAsync("closed", CancellationToken.None)).IsNull();
		await Assert.That(await holders.HeldAsync("gone", CancellationToken.None)).IsNull();
	}

	[Test]
	public async Task AdministrativeRoles_AreThoseAllowingAdministrator()
	{
		_harness.Roles.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([
			new SharpRole { Slug = "council", Name = "Council", Permissions = new() { [PortalPermission.Administrator] = PermissionState.Allow } },
			new SharpRole { Slug = "builder", Name = "Builder", Permissions = new() { [PortalPermission.Administrator] = PermissionState.Deny } },
			new SharpRole { Slug = "player", Name = "Player" },
		]);

		var administrative = await _harness.Get<IDiscordRoleHolders>().AdministrativeAsync(CancellationToken.None);

		await Assert.That(administrative).IsEquivalentTo(["council"]);
	}
}
