using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

public class MiscCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask VerbCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=greet,greets,greeting"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask SweepCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sweep"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask EditCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/DESC=old=new"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep #1=pattern"));

		// Verify that Notify was called at least once (could be "No matching attributes" or a list)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithPrintSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/print #1=pattern"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithWildSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/wild #1=*pattern*"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithRegexpSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/regexp #1=.*pattern.*"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithNocaseSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/nocase #1=PATTERN"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithAttributePattern()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep #1/DESC*=pattern"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask BriefCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("brief"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask WhoCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("who"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask SessionCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("session"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask QuitCommand()
	{
		// Create an isolated player so we don't disconnect the shared handle-1 connection
		var playerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "QuitCmdTest");

		// Register a temporary connection handle for the test player
		const long tempHandle = 999_002L;
		if (ConnectionService.Get(tempHandle) != null)
		{
			await ConnectionService.Disconnect(tempHandle);
		}
		await ConnectionService.Register(tempHandle, "127.0.0.1", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(tempHandle, playerDbRef);

		// Run quit using the temp handle — should disconnect tempHandle, not handle 1
		var preCount = NotifyService.ReceivedCalls().Count();
		await Parser.CommandParse(tempHandle, ConnectionService, MModule.single("quit"));

		var newCalls = NotifyService.ReceivedCalls().Skip(preCount).ToList();
		await Assert.That(newCalls.Any()).IsTrue();

		// The quit command must have disconnected the temp handle
		await Assert.That(ConnectionService.Get(tempHandle)).IsNull();

		// The shared handle 1 must still be alive
		await Assert.That(ConnectionService.Get(1)).IsNotNull();
	}

	[Test]
	public async ValueTask ConnectCommand()
	{
		// Already connected - CONNECT returns "Huh?" and notifies via long handle
		await Parser.CommandParse(1, ConnectionService, MModule.single("connect player password"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<long>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask PromptCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@prompt #1=Enter value:"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask NspromptCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@nsprompt #1=Enter value:"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}
}
