using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class BuildingCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[DependsOn<GeneralCommandTests>]
	public async ValueTask CreateObject()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CreateObject - Test Object"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));
		
		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObject - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	public async ValueTask CreateObjectWithCost()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CreateObjectWithCost - Test Object=10"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));
		
		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObjectWithCost - Test Object");
	}
	
	[Test]
	[DependsOn(nameof(CreateObjectWithCost))]
	public async ValueTask DoDigForCommandListCheck()
	{
		var newRoom = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dig Bar Room=Exit;ExitAlias,ExitBack;ExitAliasBack"));

		var newDb = DBRef.Parse(newRoom.Message!.ToPlainText()!);
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(),  $"Bar Room created with room number #{newDb.Number}.");
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), $"Linked exit #{newDb.Number+1} to #{newDb.Number}");
		await NotifyService
			.Received(Quantity.Exactly(2))
			.Notify(Arg.Any<DBRef>(), "Trying to link...");
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), $"Linked exit #{newDb.Number+2} to #0");
	}

	// Something is getting created before this one can trigger...
	[Test, DependsOn(nameof(DoDigForCommandListCheck))]
	public async ValueTask DoDigForCommandListCheck2()
	{
		var newRoom = await Parser.CommandListParse(MModule.single("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

		var newDb = DBRef.Parse(newRoom!.Message!.ToPlainText()!);
		
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), $"Foo Room created with room number #{newDb.Number}.");
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), $"Linked exit #{newDb.Number+1} to #{newDb.Number}");
		await NotifyService
			.Received(Quantity.Exactly(4))
			.Notify(Arg.Any<DBRef>(), "Trying to link...");
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), $"Linked exit #{newDb.Number+2} to #0");
	}


	[Test]
	[DependsOn(nameof(DoDigForCommandListCheck2))]
	public async Task DigAndMoveTest()
	{
		if (Parser is null) throw new Exception("Parser is null");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig NewRoom=Forward;F,Backward;B"));
		var initialRoom = (await Parser.FunctionParse(MModule.single("%l")))!.Message!.ToPlainText();
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto Forward"));
		var newRoom = (await Parser.FunctionParse(MModule.single("%l")))!.Message!.ToPlainText();
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto Backward"));
		var finalRoom = (await Parser.FunctionParse(MModule.single("%l")))!.Message!.ToPlainText();

		await Assert.That(initialRoom).Length().IsPositive();
		await Assert.That(initialRoom).IsEqualTo(finalRoom);
		await Assert.That(newRoom).IsNotEqualTo(initialRoom);
	}

	[Test]
	[DependsOn(nameof(DigAndMoveTest))]
	[Skip("Failing Test - Needs Investigation")]
	// 	"#-2 I DON'T KNOW WHICH ONE YOU MEAN"
	public async ValueTask NameObject()
	{
		// Create an object first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DigAndMoveTest - Rename Test"));
		
		// Rename it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@name DigAndMoveTest - Rename Test=DigAndMoveTest - New Name"));

		var newObject = (await Parser.FunctionParse(MModule.single("name(DigAndMoveTest - New Name)")))!.Message!.ToPlainText();
		
		await Assert.That(newObject).IsEqualTo("DigAndMoveTest - New Name");
	}

	[Test]
	[DependsOn(nameof(NameObject))]
	public async ValueTask DigRoom()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig DigRoom - Test Room"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));
		
		await Assert.That(newObject.Object()!.Name).IsEqualTo("DigRoom - Test Room");
	}

	[Test]
	[DependsOn(nameof(DigRoom))]
	public async ValueTask DigRoomWithExits()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Room With Exits=In;I,Out;O"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));
		
		await NotifyService
			.Received()
			.Notify(Arg.Any<DBRef>(), $"Room With Exits created with room number #{newObject.Object()!.DBRef.Number}.");
	}

	[Test]
	[DependsOn(nameof(DigRoomWithExits))]
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
			.Notify(Arg.Any<DBRef>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Linked"),
					str => str.Contains("Linked")
				)));
	}

	[Test]
	[DependsOn(nameof(LinkExit))]
	[Skip("Not Yet Implemented")]
	public async ValueTask CloneObject()
	{
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Clone Source"));
		
		// Clone it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@clone #7"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Cloned"),
					str => str.Contains("Cloned")
				)));
	}

	[Test]
	public async ValueTask ParentSetAndGet()
	{
		// Create two objects
		var parentResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentTestObject"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		
		var childResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ChildTestObject"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);
		
		// Verify both objects exist
		var parentObj = await Mediator.Send(new GetObjectNodeQuery(parentDbRef));
		var childObj = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		await Assert.That(parentObj.IsNone).IsFalse();
		await Assert.That(childObj.IsNone).IsFalse();
		
		// Verify child has no parent initially
		var initialParent = await childObj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(initialParent.IsNone).IsTrue();
		
		// Set parent using @parent command
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));
		
		// Verify parent was set by querying database directly
		var updatedChild = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var setParent = await updatedChild.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(setParent.IsNone).IsFalse();
		await Assert.That(setParent.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ParentSetAndGet))]
	public async ValueTask ParentUnset()
	{
		// Create two objects
		var parentResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentUnsetTest_Parent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		
		var childResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentUnsetTest_Child"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);
		
		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));
		
		// Verify parent was set
		var childWithParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentSet = await childWithParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentSet.IsNone).IsFalse();
		
		// Unset parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}=none"));
		
		// Verify parent was cleared
		var childNoParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentCleared = await childNoParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentCleared.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(CloneObject))]
	[Skip("Not Yet Implemented - replaced by ParentSetAndGet")]
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
	[DependsOn(nameof(SetParent))]
	[Skip("Not Yet Implemented")]
	public async ValueTask ChownObject()
	{
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Chown Test"));
		
		// Change ownership (to self in this case)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chown #10=#1"));

		// Verify command executed without permission error
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("PERMISSION DENIED"),
					str => str.Contains("PERMISSION DENIED")
				)));
	}

	[Test]
	[DependsOn(nameof(ChownObject))]
	public async ValueTask ChzoneObject()
	{
		// Create objects
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zone Object"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zoned Object"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set zone
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Zoned"),
					str => str.Contains("Zoned")
				)), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[DependsOn(nameof(ChzoneObject))]
	[Skip("Not Yet Implemented")]
	public async ValueTask RecycleObject()
	{
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Recycle Test"));
		
		// Recycle it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@recycle #13"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Marked for destruction"),
					str => str.Contains("Marked for destruction")
				)));
	}

	[Test]
	[DependsOn(nameof(RecycleObject))]
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Unlinked"),
					str => str.Contains("Unlinked")
				)));
	}

	[Test]
	public async ValueTask SetFlag()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=MONITOR"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Any(x => x.Name == "MONITOR" || x.Name == "DEBUG")).IsTrue();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask LockObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@lock #1=me"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Locked"),
					str => str.Contains("Locked")
				)));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				msg.Match(
					mstr => mstr.ToString().Contains("Unlocked"),
					str => str.Contains("Unlocked")
				)));
	}
}
