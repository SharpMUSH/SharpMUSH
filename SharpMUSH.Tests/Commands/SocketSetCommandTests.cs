namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@sockset</c>: a player reads and sets options on their own connections; reaching anyone else's takes a wizard,
/// and a descriptor that is not theirs reads as invalid rather than refused.
/// </summary>
public class SocketSetCommandTests : ServerTestBase
{
	private OwnRoomPlayers Players => new(WebAppFactoryArg);

	[Test]
	public async Task NoArgument_ShowsTheConnectionsOwnSettings()
	{
		var player = await Players.PlayerAsync("SocksetShow");

		var shown = (await Players.HeardRunning(player, "@sockset")).Single();

		await Assert.That(shown).Contains("Connection settings");
		await Assert.That(shown).Contains("Prompt Newlines: No");
	}

	[Test]
	public async Task OptionValuePairs_AreSetInTurn()
	{
		var player = await Players.PlayerAsync("SocksetSet");

		await Assert.That(await Players.HeardRunning(player, $"@sockset {player.Handle}=PROMPT_NEWLINES,1,COLORSTYLE"))
			.IsEquivalentTo(["A newline will be sent after a prompt.", "Set what option?"]);
		await Assert.That((await Players.HeardRunning(player, "@sockset")).Single()).Contains("Prompt Newlines: Yes");
	}

	[Test]
	public async Task SomeoneElsesConnection_IsInvalidForAMortal()
	{
		var player = await Players.PlayerAsync("SocksetMortal");
		var other = await Players.PlayerAsync("SocksetOther");

		await Assert.That(await Players.HeardRunning(player, $"@sockset {other.Handle}")).IsEquivalentTo(["Invalid descriptor."]);
		await Assert.That(await Players.HeardRunning(player, $"@sockset {other.Name}")).IsEquivalentTo(["Invalid descriptor."]);
		await Assert.That(await Players.HeardRunning(player, $"@sockset {TestIsolationHelpers.GenerateUniqueHandle()}")).IsEquivalentTo(["Invalid descriptor."]);
	}

	[Test]
	public async Task Wizard_ReachesAnotherPlayersConnection()
	{
		var wizard = await Players.WizardAsync("SocksetWizard");
		var other = await Players.PlayerAsync("SocksetTarget");

		await Assert.That((await Players.HeardRunning(wizard, $"@sockset {other.Name}")).Single()).Contains("Connection settings");
		await Assert.That((await Players.HeardRunning(wizard, $"@sockset {other.Handle}")).Single()).Contains("Connection settings");
	}
}
