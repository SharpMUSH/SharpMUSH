using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordStandingPlanTests
{
	private static readonly ulong[] Servers = [11, 12];

	[Test]
	public async Task AnActiveLinkedMember_HoldsTheLinkedRole()
	{
		var plan = DiscordStandingPlan.For(DiscordStanding.Active, linked: true, mirrorBans: true, Servers, []);

		await Assert.That(plan.HoldLinked).IsTrue();
		await Assert.That(plan.Ban).IsEmpty();
		await Assert.That(plan.Unban).IsEmpty();
	}

	[Test]
	public async Task ABannedOrInactiveAccount_LosesTheLinkedRole()
	{
		var banned = DiscordStandingPlan.For(DiscordStanding.Banned, linked: true, mirrorBans: false, Servers, []);
		var inactive = DiscordStandingPlan.For(DiscordStanding.Inactive, linked: true, mirrorBans: true, Servers, []);

		await Assert.That(banned.HoldLinked).IsFalse();
		await Assert.That(banned.Ban).IsEmpty().Because("bans are not mirrored");
		await Assert.That(inactive.HoldLinked).IsFalse();
		await Assert.That(inactive.Ban).IsEmpty().Because("only a ban is mirrored, not a plain disable");
	}

	[Test]
	public async Task AnUnlinkedUser_IsLeftToTheUnlink()
	{
		var plan = DiscordStandingPlan.For(DiscordStanding.Active, linked: false, mirrorBans: true, Servers, []);

		await Assert.That(plan.HoldLinked).IsNull();
	}

	[Test]
	public async Task WithBansMirrored_ABannedMember_IsBannedWhereTheBotHasNotBannedThemYet()
	{
		var plan = DiscordStandingPlan.For(DiscordStanding.Banned, linked: true, mirrorBans: true, Servers, [11]);

		await Assert.That(plan.Ban).IsEquivalentTo(new ulong[] { 12 });
		await Assert.That(plan.Unban).IsEmpty();
	}

	[Test]
	public async Task WhenTheBanEnds_TheBotLiftsOnlyItsOwnBans_MirroredOrNot()
	{
		var mirrored = DiscordStandingPlan.For(DiscordStanding.Active, linked: true, mirrorBans: true, Servers, [11]);
		var turnedOff = DiscordStandingPlan.For(DiscordStanding.Active, linked: true, mirrorBans: false, Servers, [11]);

		await Assert.That(mirrored.Unban).IsEquivalentTo(new ulong[] { 11 });
		await Assert.That(turnedOff.Unban).IsEquivalentTo(new ulong[] { 11 });
		await Assert.That(mirrored.Ban).IsEmpty();
	}
}
