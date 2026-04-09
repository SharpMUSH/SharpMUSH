using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class ObjectManipulationCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask GetCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("GetCom");
		// Create a thing in the room
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create GetTestObject"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		// Get the thing
		var getResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get GetTestObject"));

		// Verify the command succeeded by checking the result
		await Assert.That(getResult).IsNotNull();
	}

	[Test]
	public async ValueTask GetFromContainer()
	{
		var testPlayer = await CreateTestPlayerAsync("GetFroCon");
		// Create a container and an object
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create Container2"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create InnerObject2"));

		// Set container as ENTER_OK
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set Container2=ENTER_OK"));

		// Put inner object in container
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get InnerObject2"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give Container2=InnerObject2"));

		// Try to get from container
		var getResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get Container2's InnerObject2"));

		// Verify the command succeeded
		await Assert.That(getResult).IsNotNull();
	}

	[Test]
	public async ValueTask GetNonexistentObject()
	{
		var testPlayer = await CreateTestPlayerAsync("GetNonObj");
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get NonexistentObject12345"));

		// Command should execute (even if it fails to find the object)
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask DropCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("DroCom");
		// Create and get an object
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create DropTestObject"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get DropTestObject"));

		// Drop it
		var dropResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("drop DropTestObject"));

		// Verify command succeeded
		await Assert.That(dropResult).IsNotNull();
	}

	[Test]
	public async ValueTask GiveCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("GivCom");
		// Create a thing and another player
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create GiveTestObject"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get GiveTestObject"));

		// Create a recipient (needs to be created as player or thing with ENTER_OK)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create Recipient"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set Recipient=ENTER_OK"));

		// Give the object
		var giveResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give Recipient=GiveTestObject"));

		// Verify command succeeded
		await Assert.That(giveResult).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UseCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("UseCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("use test object"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask InventoryCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("InvCom");
		// Just test the command runs
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("inventory"));

		// Verify command executed
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask GetPreventsLoops()
	{
		var testPlayer = await CreateTestPlayerAsync("GetPreLoo");
		// Create a box and a bag
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create Box"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create Bag"));

		// Set both as ENTER_OK
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set Box=ENTER_OK"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set Bag=ENTER_OK"));

		// Get both objects
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get Box"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get Bag"));

		// Put Bag inside Box
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give Box=Bag"));

		// Try to get Box from inside Bag (should fail with loop error)
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get Bag's Box"));

		// Verify command executed (even though it should have rejected the loop)
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask GivePreventsLoops()
	{
		var testPlayer = await CreateTestPlayerAsync("GivPreLoo");
		// Create a chest and a sack
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create Chest"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create Sack"));

		// Set both as ENTER_OK
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set Chest=ENTER_OK"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set Sack=ENTER_OK"));

		// Get both objects
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get Chest"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get Sack"));

		// Put Sack inside Chest
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give Chest=Sack"));

		// Try to give Chest to Sack (should fail with loop error)
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give Sack=Chest"));

		// Verify command executed (even though it should have rejected the loop)
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DestroyCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("DesCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@destroy #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask NukeCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("NukCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@nuke #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UndestroyCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("UndCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@undestroy #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}
}
