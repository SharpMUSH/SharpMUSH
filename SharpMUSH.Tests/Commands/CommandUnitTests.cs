using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _player = null!;

	[Before(TUnit.Core.HookType.Test)]
	public async Task CreatePlayer()
	{
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CommandUnit");
	}

	[After(TUnit.Core.HookType.Test)]
	public async Task DisconnectPlayer()
	{
		if (_player is not null)
			await ConnectionService.Disconnect(_player.Handle);
	}

	// A leading ] suppresses argument evaluation.
	[Test]
	[Arguments("]think [add(1,2)]3", "[add(1,2)]3")]
	public async Task Test_NoEval(string str, string expected)
	{
		var executor = _player.DbRef;
		TestDiagnostics.WriteLine("Testing NoEval: {0}", str);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain(str));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage(expected), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("think add(1,2)1",
		"31")]
	[Arguments("think [add(1,2)]2",
		"32")]
	[Arguments("think Command1 Arg;think Command2 Arg",
		"Command1 Arg;think Command2 Arg")]
	public async Task Test(string str, string expected)
	{
		var executor = _player.DbRef;
		TestDiagnostics.WriteLine("Testing: {0}", str);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain(str));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage(expected), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("think add(1,2)4;think add(2,3)5",
		"34",
		"55")]
	[Arguments("think [add(1,2)]6;think add(3,2)7",
		"36",
		"57")]
	[Arguments("think [ansi(hr,red)];think [ansi(hg,green)]",
		"\e[1;31mred\e[0m",
		"\e[1;32mgreen\e[0m")]
	[Arguments("think Command1 Arg;think Command2 Arg",
		"Command1 Arg",
		"Command2 Arg")]
	[Arguments("think Command3 Arg;think Command4 Arg.;",
		"Command3 Arg",
		"Command4 Arg.")]
	public async Task TestSingle(string str, string expected1, string expected2)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		await Parser.CommandListParse(MarkupText.Plain(str));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(_player.DbRef), Arg.Is<OneOf.OneOf<MString, string>>(x
				=> TestHelpers.MessageContains(x, expected1)), TestHelpers.MatchingObject(_player.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(_player.DbRef), Arg.Is<OneOf.OneOf<MString, string>>(x
				=> TestHelpers.MessageContains(x, expected2)), TestHelpers.MatchingObject(_player.DbRef), INotifyService.NotificationType.Announce);
	}
}