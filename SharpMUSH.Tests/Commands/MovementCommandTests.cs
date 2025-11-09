using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class MovementCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask GotoCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask TeleportPreventsLoops()
	{
		// Create a container and an item
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create TeleportBox"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create TeleportItem"));
		
		// Set box as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set TeleportBox=ENTER_OK"));
		
		// Get both objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("get TeleportBox"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get TeleportItem"));
		
		// Put TeleportItem inside TeleportBox
		await Parser.CommandParse(1, ConnectionService, MModule.single("give TeleportBox=TeleportItem"));
		
		// Try to teleport TeleportBox into TeleportItem (should fail with loop error)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@teleport TeleportBox=TeleportItem"));
		
		// Should receive error about containment loop
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("loop")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask HomeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("home"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnterCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("enter #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask LeaveCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("leave"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
