using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>The game's answer for each mapped role, set against what a member holds, for each reason the bot looks.</summary>
public class DiscordRolePlanTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private static readonly DiscordRoleMapping Builders = new(500, 900, "Builders", "builder", DiscordRoleMode.Report, Now, "#1:1");
	private static readonly DiscordRoleMapping Staff = new(600, 900, "Staff", "royalty", DiscordRoleMode.Enforce, Now, "#1:1");

	private static DiscordMember Member(params ulong[] roles) => new(7, "ann", roles.ToHashSet());

	private static IReadOnlySet<string> Holds(params string[] slugs) => slugs.ToHashSet(StringComparer.OrdinalIgnoreCase);

	[Test]
	public async Task AChangeInTheGame_GivesAndTakesToMatch()
	{
		var decision = DiscordRolePlan.Changed(Member(600), [Builders, Staff], Holds("builder"));

		await Assert.That(decision.Changes).IsEquivalentTo([new DiscordRoleChange(7, 500, true), new DiscordRoleChange(7, 600, false)]);
		await Assert.That(decision.Drift).IsEmpty();
	}

	[Test]
	public async Task AnAccountThatIsNotActive_HoldsNothing()
	{
		var decision = DiscordRolePlan.Changed(Member(500, 600), [Builders, Staff], held: null);

		await Assert.That(decision.Changes).IsEquivalentTo([new DiscordRoleChange(7, 500, false), new DiscordRoleChange(7, 600, false)]);
	}

	[Test]
	public async Task APass_RepairsALinkedMember_ButLeavesOpenDrift()
	{
		var decision = DiscordRolePlan.Pass(Member(), [Builders, Staff], Holds("builder", "royalty"), linked: true,
			roleId => roleId == 600);

		await Assert.That(decision.Changes).IsEquivalentTo([new DiscordRoleChange(7, 500, true)]);
		await Assert.That(decision.Drift).IsEmpty();
	}

	[Test]
	public async Task APass_ReportsAMemberWhoIsNotLinked_AndTakesBackOnlyAnEnforcedRole()
	{
		var decision = DiscordRolePlan.Pass(Member(500, 600), [Builders, Staff], held: null, linked: false, _ => false);

		await Assert.That(decision.Changes).IsEquivalentTo([new DiscordRoleChange(7, 600, false)]);
		await Assert.That(decision.Drift).IsEquivalentTo([new DiscordDriftSeen(500, 7, "ann", true), new DiscordDriftSeen(600, 7, "ann", true)]);
	}

	[Test]
	public async Task AChangeByHand_IsDrift_AndOnlyAnEnforcedRoleIsPutBack()
	{
		var decision = DiscordRolePlan.Observed(Member(500), [Builders, Staff], Holds("royalty"));

		await Assert.That(decision.Drift).IsEquivalentTo([new DiscordDriftSeen(500, 7, "ann", true), new DiscordDriftSeen(600, 7, "ann", false)]);
		await Assert.That(decision.Changes).IsEquivalentTo([new DiscordRoleChange(7, 600, true)]);
	}

	[Test]
	public async Task TheBotsOwnChange_IsNotDrift()
	{
		var decision = DiscordRolePlan.Observed(Member(500), [Builders, Staff], Holds("builder"));

		await Assert.That(decision.Changes).IsEmpty();
		await Assert.That(decision.Drift).IsEmpty();
	}
}
