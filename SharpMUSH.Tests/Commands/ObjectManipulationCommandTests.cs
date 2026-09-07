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
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create GetTestObject"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		var getResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get GetTestObject"));

		await Assert.That(getResult).IsNotNull();
	}

	[Test]
	public async ValueTask GetFromContainer()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Container2"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create InnerObject2"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set Container2=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get InnerObject2"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give Container2=InnerObject2"));

		var getResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get Container2's InnerObject2"));

		await Assert.That(getResult).IsNotNull();
	}

	[Test]
	public async ValueTask GetNonexistentObject()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get NonexistentObject12345"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask DropCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create DropTestObject"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get DropTestObject"));

		var dropResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("drop DropTestObject"));

		await Assert.That(dropResult).IsNotNull();
	}

	[Test]
	public async ValueTask GiveCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create GiveTestObject"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get GiveTestObject"));

		// Create a recipient (needs to be created as player or thing with ENTER_OK)
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Recipient"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set Recipient=ENTER_OK"));

		var giveResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give Recipient=GiveTestObject"));

		await Assert.That(giveResult).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UseCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("use test object"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "I don't see that here.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask InventoryCommand()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("inventory"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask GetPreventsLoops()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Box"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Bag"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set Box=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set Bag=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get Box"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get Bag"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give Box=Bag"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get Bag's Box"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask GivePreventsLoops()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Chest"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Sack"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set Chest=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@set Sack=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get Chest"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("get Sack"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give Chest=Sack"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("give Sack=Chest"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DestroyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@destroy #100"));

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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@nuke #100"));

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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@undestroy #100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "I don't see that here.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
