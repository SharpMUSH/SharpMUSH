namespace SharpMUSH.Tests.Commands;

/// <summary>A command alias whose target names switches runs the command with them.</summary>
public class SwitchAliasTests : ServerTestBase
{
	[Test]
	public async Task AliasRunsCommandWithItsSwitches()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SwitchAlias");
		var alias = "zsa" + Guid.NewGuid().ToString("N")[..12];
		var token = TestIsolationHelpers.GenerateUniqueName("switchalias");
		try
		{
			await Cmd($"@command/alias @pemit/noeval={alias}");
			await CmdAs(actor.DbRef, actor.Handle, $"{alias} me={token} [add(1,1)]");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(token, StringComparison.Ordinal)))
				.IsEquivalentTo([$"{token} [add(1,1)]"]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task TypedSwitchesAddToTheAliasSwitches()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SwitchAliasTyped");
		var alias = "zsa" + Guid.NewGuid().ToString("N")[..12];
		var token = TestIsolationHelpers.GenerateUniqueName("switchaliastyped");
		try
		{
			await Cmd($"@command/alias @pemit/list={alias}");
			await CmdAs(actor.DbRef, actor.Handle, $"{alias}/noeval me={token} [add(1,1)]");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(token, StringComparison.Ordinal)))
				.IsEquivalentTo([$"{token} [add(1,1)]"]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task AliasToAWildcardSwitchIsAccepted()
	{
		var alias = "zsa" + Guid.NewGuid().ToString("N")[..12];

		await Assert.That(await Cmd($"@command/alias @lock/enter={alias}")).IsNotEqualTo("#-1 INVALID ARGUMENTS");
	}

	[Test]
	public async Task AliasToAnUnknownSwitchIsRefused()
	{
		var alias = "zsa" + Guid.NewGuid().ToString("N")[..12];

		await Assert.That(await Cmd($"@command/alias @pemit/nosuchswitch={alias}")).IsEqualTo("#-1 INVALID ARGUMENTS");
	}
}
