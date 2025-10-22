using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class BuildingCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask CreateObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Test Object"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask CreateObjectWithCost()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Test Object=10"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	public async ValueTask NameObject()
	{
		// Create an object first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Rename Test"));
		
		// Rename it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@name #4=New Name"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DigRoom()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Test Room"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<DBRef>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DigRoomWithExits()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Room With Exits=In;I,Out;O"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<DBRef>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(DigRoom))]
	[Skip("Not Yet Implemented")]
	public async ValueTask OpenExit()
	{
		// Create a room first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Destination Room"));
		
		// Open an exit
		await Parser.CommandParse(1, ConnectionService, MModule.single("@open Test Exit=#5"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	[Skip("Not Yet Implemented")]
	public async ValueTask LinkExit()
	{
		// Create room and exit
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Link Test Room"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@open Link Exit"));
		
		// Link them
		await Parser.CommandParse(1, ConnectionService, MModule.single("@link Link Exit=#6"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	[Skip("Not Yet Implemented")]
	public async ValueTask CloneObject()
	{
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Clone Source"));
		
		// Clone it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@clone #7"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	public async ValueTask SetParent()
	{
		// Create two objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Parent Object"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Child Object"));
		
		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single("@parent #9=#8"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	[Skip("Not Yet Implemented")]
	public async ValueTask ChownObject()
	{
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Chown Test"));
		
		// Change ownership (to self in this case)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chown #10=#1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	[Skip("Not Yet Implemented")]
	public async ValueTask ChzoneObject()
	{
		// Create objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zone Object"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zoned Object"));
		
		// Set zone
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone #12=#11"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	[Skip("Not Yet Implemented")]
	public async ValueTask RecycleObject()
	{
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Recycle Test"));
		
		// Recycle it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@recycle #13"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(OpenExit))]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnlinkExit()
	{
		// Create and link an exit
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Unlink Room"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@open Unlink Exit=#14"));
		
		// Unlink it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unlink Unlink Exit"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SetFlag()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=MONITOR"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.WithCancellation(CancellationToken.None);

		await Assert.That(flags.Any(x => x.Name == "MONITOR" || x.Name == "DEBUG")).IsTrue();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask LockObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@lock #1=me"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnlockObject()
	{
		// Lock first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@lock #1=me"));
		
		// Then unlock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unlock #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
