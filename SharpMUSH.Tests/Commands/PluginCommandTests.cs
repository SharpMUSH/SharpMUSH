using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@plugin</c> against the live server: who may use it, and what it says about a plugin it does not know.</summary>
public class PluginCommandTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "PluginWiz");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "PluginMortal");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _wizard, _mortal })
			await ConnectionService.Disconnect(player.Handle);
	}

	private Task<string> As(TestIsolationHelpers.TestPlayer player, string command) => CmdAs(player.DbRef, player.Handle, command);

	[Test]
	public async Task AWizardListsPlugins()
	{
		await Assert.That(await As(_wizard, "@plugin")).Contains("Plugins");
	}

	[Test]
	public async Task AnUnknownPluginIsNamed()
	{
		var unknown = TestIsolationHelpers.GenerateUniqueName("noplugin").ToLowerInvariant();

		await Assert.That(await As(_wizard, $"@plugin/disable {unknown}")).Contains($"There is no plugin '{unknown}'");
		await Assert.That(await As(_wizard, $"@plugin {unknown}")).Contains($"There is no plugin '{unknown}'");
		await Assert.That(await As(_wizard, "@plugin/enable")).Contains("Usage: @plugin/enable <plugin>");
	}

	[Test]
	public async Task AMortalCannotUseIt()
	{
		await Assert.That(await As(_mortal, "@plugin")).DoesNotContain("Plugins");
	}
}
