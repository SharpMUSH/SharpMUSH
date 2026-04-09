using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class GameCommandTests
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
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask BuyCommand()
{
var testPlayer = await CreateTestPlayerAsync("Buy");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("buy sword"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask ScoreCommand()
{
var testPlayer = await CreateTestPlayerAsync("Scr");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("score"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask TeachCommand()
{
var testPlayer = await CreateTestPlayerAsync("Tch");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"teach {testPlayer.DbRef}=skill"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask FollowCommand()
{
var testPlayer = await CreateTestPlayerAsync("Flw");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"follow {testPlayer.DbRef}"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask UnfollowCommand()
{
var testPlayer = await CreateTestPlayerAsync("Unf");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("unfollow"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask DesertCommand()
{
var testPlayer = await CreateTestPlayerAsync("Dsr");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("desert"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask DismissCommand()
{
var testPlayer = await CreateTestPlayerAsync("Dsm");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"dismiss {testPlayer.DbRef}"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
public async ValueTask EmptyCommand()
{
var testPlayer = await CreateTestPlayerAsync("Empt");
// Create a container and some items to put in it
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create EmptyTestContainer"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create EmptyTestItem1"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create EmptyTestItem2"));

// Set container as ENTER_OK so we can access its contents
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set EmptyTestContainer=ENTER_OK"));

// Get the container
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("get EmptyTestContainer"));

// Put items inside the container
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give EmptyTestContainer=EmptyTestItem1"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give EmptyTestContainer=EmptyTestItem2"));

// Empty the container
var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("empty EmptyTestContainer"));

// Verify result is not null
await Assert.That(result).IsNotNull();
}

[Test]
public async ValueTask EmptyCommandSameLocation()
{
var testPlayer = await CreateTestPlayerAsync("EmptSL");
// Create a container and an item in the same room
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create EmptyTestBox"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@create EmptyTestThing"));

// Set box as ENTER_OK
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@set EmptyTestBox=ENTER_OK"));

// Put thing inside the box
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("give EmptyTestBox=EmptyTestThing"));

// Drop the box in the room
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("drop EmptyTestBox"));

// Empty the box (should move item from box to room, passing through our hands)
var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("empty EmptyTestBox"));

// Verify result is not null
await Assert.That(result).IsNotNull();
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask WithCommand()
{
var testPlayer = await CreateTestPlayerAsync("With");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"with {testPlayer.DbRef}"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}
}
