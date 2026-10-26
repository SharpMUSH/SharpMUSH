namespace SharpMUSH.Tests.Commands;

/// <summary><c>@motd</c> sets, lists and clears the messages of the day.</summary>
public class MotdCommandTests : ServerTestBase
{
	private OwnRoomPlayers Players => new(WebAppFactoryArg);

	/// <summary>
	/// The MOTD shown while logins are off, which no test logs in under; the connect MOTD would land in every other
	/// test's login output.
	/// </summary>
	[Test, NotInParallel(["Motd"])]
	public async Task SetsListsAndClearsTheDownMessage()
	{
		var wizard = await Players.WizardAsync("MotdWizard");
		var message = TestIsolationHelpers.GenerateUniqueName("DownMotd");

		await Players.RunAsync(wizard, $"@motd/down {message}");
		await Assert.That(string.Join("\n", await Players.HeardRunning(wizard, "@motd/list"))).Contains($"Down MOTD:    {message}");

		await Players.RunAsync(wizard, "@motd/down/clear");
		await Assert.That(string.Join("\n", await Players.HeardRunning(wizard, "@motd/list"))).DoesNotContain(message);
	}

	[Test, NotInParallel(["Motd"])]
	public async Task Mortal_CannotSetTheDownMessage()
	{
		var mortal = await Players.PlayerAsync("MotdMortal");
		var wizard = await Players.WizardAsync("MotdLister");
		var message = TestIsolationHelpers.GenerateUniqueName("MortalMotd");

		await Players.RunAsync(mortal, $"@motd/down {message}");

		await Assert.That(string.Join("\n", await Players.HeardRunning(wizard, "@motd/list"))).DoesNotContain(message);
	}
}
