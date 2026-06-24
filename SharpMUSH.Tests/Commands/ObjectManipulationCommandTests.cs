using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ObjectManipulationCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask GetCommand()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create GetTestObject"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		var getResult = await Parser.CommandParse(1, ConnectionService, MModule.single("get GetTestObject"));

		await Assert.That(getResult).IsNotNull();
	}

	[Test]
	public async ValueTask GetFromContainer()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Container2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create InnerObject2"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Container2=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("get InnerObject2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("give Container2=InnerObject2"));

		var getResult = await Parser.CommandParse(1, ConnectionService, MModule.single("get Container2's InnerObject2"));

		await Assert.That(getResult).IsNotNull();
	}

	[Test]
	public async ValueTask GetNonexistentObject()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("get NonexistentObject12345"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask DropCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DropTestObject"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get DropTestObject"));

		var dropResult = await Parser.CommandParse(1, ConnectionService, MModule.single("drop DropTestObject"));

		await Assert.That(dropResult).IsNotNull();
	}

	[Test]
	public async ValueTask GiveCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create GiveTestObject"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get GiveTestObject"));

		// Create a recipient (needs to be created as player or thing with ENTER_OK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Recipient"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Recipient=ENTER_OK"));

		var giveResult = await Parser.CommandParse(1, ConnectionService, MModule.single("give Recipient=GiveTestObject"));

		await Assert.That(giveResult).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UseCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("use test object"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "I don't see that here.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask InventoryCommand()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("inventory"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask GetPreventsLoops()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Box"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Bag"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Box=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Bag=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("get Box"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Bag"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("give Box=Bag"));

		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("get Bag's Box"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask GivePreventsLoops()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Chest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Sack"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Chest=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Sack=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("get Chest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Sack"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("give Chest=Sack"));

		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("give Sack=Chest"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DestroyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy #100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "I don't see that here.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask NukeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@nuke #100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "I don't see that here.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UndestroyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@undestroy #100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "I don't see that here.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
