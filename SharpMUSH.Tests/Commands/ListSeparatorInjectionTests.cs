using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>Where a player's own <c>%*&lt;|&gt;</c> can reach another object's code.</summary>
public class ListSeparatorInjectionTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _actor = null!;
	private DBRef _commands;
	private string _token = "";

	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	[Before(Test)]
	public async Task CreateActor()
	{
		var room = await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("SepInjRoom")}");
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SepInj", DBRef.Parse(room.Trim()));
		_commands = DBRef.Parse(await Run($"@create {TestIsolationHelpers.GenerateUniqueName("SepInjObj")}"));
		await Run($"@set {_commands}=!no_command");
		await Run($"drop {_commands}");
		_token = TestIsolationHelpers.GenerateUniqueName("si").ToLowerInvariant();
	}

	[After(Test)]
	public async Task DisconnectActor()
	{
		if (_actor is not null) await ConnectionService.Disconnect(_actor.Handle);
	}

	private Task<string> Run(string command) => CmdAs(_actor.DbRef, _actor.Handle, command);

	private List<string> Heard() => Notifications.For(_actor.DbRef)
		.Where(message => message.Contains(_token, StringComparison.Ordinal))
		.ToList();

	/// <summary>
	/// A player's own <c>%*&lt;|&gt;</c> reaches a $-command's arguments as a separator, the way their colour does;
	/// <c>unmark()</c>, <c>secure()</c> and <c>escape()</c> take it off.
	/// </summary>
	[Test]
	public async Task TypedSeparatorsAreTakenOffByUnmark()
	{
		await Run($"&CMD {_commands}=${_token} *:@pemit %#={_token} raw [words(%0,%*<|>)] unmark [words(unmark(%0),%*<|>)] secure [words(secure(%0),%*<|>)] escape [words(escape(%0),%*<|>)]");
		await Run($"{_token} a%*<|>b");
		await Scheduler.SettleForTestsAsync();

		await Assert.That(Heard()).Contains($"{_token} raw 2 unmark 1 secure 1 escape 1");
	}

	/// <summary>The help's example: a list built from typed text, unmarked on the way in, keeps one item per add.</summary>
	[Test]
	public async Task HelpExampleKeepsOneItemPerAdd()
	{
		await Run($"&CMD`ADD {_commands}=${_token}add *:think attrib_set({_commands}/LIST, [v(LIST)]%*<|>[unmark(%0)])");
		await Run($"{_token}add x");
		await Run($"{_token}add a%*<|>b");
		await Scheduler.SettleForTestsAsync();

		await Assert.That(await Eval($"words(rest(get({_commands}/LIST), %*<|>), %*<|>)")).IsEqualTo("2");
	}
}
