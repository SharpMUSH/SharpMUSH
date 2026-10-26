using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@mail/retract</c> and <c>@mail/stats</c> of another player.</summary>
public class MailRetractAndStatsTests : ServerTestBase
{
	private OwnRoomPlayers Players => new(WebAppFactoryArg);

	[Test]
	public async Task Retract_TakesBackUnreadMailOnce()
	{
		var sender = await Players.PlayerAsync("RetractSender");
		var recipient = await Players.PlayerAsync("RetractRecipient");
		await Players.RunAsync(sender, $"@mail {recipient.Name}=Oops/Never mind");

		await Assert.That(await CmdAs(sender.DbRef, sender.Handle, $"@mail/retract {recipient.Name}=1")).IsEqualTo("1");
		await Assert.That(await CmdAs(sender.DbRef, sender.Handle, $"@mail/retract {recipient.Name}=1")).IsEqualTo("0");
		await Assert.That(await MailCount(recipient)).IsEqualTo("0");
	}

	/// <summary>The list counts the mail the executor sent that player, as <c>@mail/review</c> lists it.</summary>
	[Test]
	public async Task Retract_NeverReachesMailSomeoneElseSent()
	{
		var sender = await Players.PlayerAsync("RetractOwnSender");
		var recipient = await Players.PlayerAsync("RetractOwnRecipient");
		var stranger = await Players.PlayerAsync("RetractStranger", DBRef.Parse(await Eval($"loc(#{recipient.DbRef.Number})")));
		await Players.RunAsync(sender, $"@mail {recipient.Name}=Mine/Not yours");

		await Assert.That(await CmdAs(stranger.DbRef, stranger.Handle, $"@mail/retract {recipient.Name}=1")).IsEqualTo("0");
		await Assert.That(await MailCount(recipient)).IsEqualTo("1");
	}

	[Test]
	public async Task Retract_LeavesMailAlreadyRead()
	{
		var sender = await Players.PlayerAsync("RetractReadSender");
		var recipient = await Players.PlayerAsync("RetractReadRecipient");
		await Players.RunAsync(sender, $"@mail {recipient.Name}=Seen/Already read");
		await Players.RunAsync(recipient, "@mail/read 1");

		await Assert.That(await CmdAs(sender.DbRef, sender.Handle, $"@mail/retract {recipient.Name}=1")).IsEqualTo("0");
		await Assert.That(await MailCount(recipient)).IsEqualTo("1");
	}

	[Test]
	public async Task Stats_OfAnotherPlayerAnywhere_NeedsStaff()
	{
		var mortal = await Players.PlayerAsync("MailStatsMortal");
		var wizard = await Players.WizardAsync("MailStatsWizard");

		await Assert.That(await Players.HeardRunning(mortal, $"@mail/stats {wizard.Name}")).IsEquivalentTo(["Permission denied."]);
		await Assert.That((await Players.HeardRunning(wizard, $"@mail/stats {mortal.Name}")).All(line => line.StartsWith(mortal.Name)))
			.IsTrue();
	}

	/// <summary>How many messages <paramref name="player"/> holds in every folder.</summary>
	private Task<string> MailCount(TestIsolationHelpers.TestPlayer player)
		=> EvalAs(player.DbRef, "mail()");
}
