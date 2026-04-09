using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MiscCommandTests
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
public async ValueTask VerbCommand()
{
var testPlayer = await CreateTestPlayerAsync("Verb");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@verb {testPlayer.DbRef}=greet,greets,greeting"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask SweepCommand()
{
var testPlayer = await CreateTestPlayerAsync("Swp");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@sweep"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask EditCommand()
{
var testPlayer = await CreateTestPlayerAsync("Edit");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@edit {testPlayer.DbRef}/DESC=old=new"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
public async ValueTask GrepCommand()
{
var testPlayer = await CreateTestPlayerAsync("Grep");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@grep {testPlayer.DbRef}=pattern"));

// Verify that Notify was called at least once (could be "No matching attributes" or a list)
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
public async ValueTask GrepCommand_WithPrintSwitch()
{
var testPlayer = await CreateTestPlayerAsync("GrepP");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@grep/print {testPlayer.DbRef}=pattern"));

// Verify that Notify was called at least once
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
public async ValueTask GrepCommand_WithWildSwitch()
{
var testPlayer = await CreateTestPlayerAsync("GrepW");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@grep/wild {testPlayer.DbRef}=*pattern*"));

// Verify that Notify was called at least once
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
public async ValueTask GrepCommand_WithRegexpSwitch()
{
var testPlayer = await CreateTestPlayerAsync("GrepR");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@grep/regexp {testPlayer.DbRef}=.*pattern.*"));

// Verify that Notify was called at least once
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
public async ValueTask GrepCommand_WithNocaseSwitch()
{
var testPlayer = await CreateTestPlayerAsync("GrepN");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@grep/nocase {testPlayer.DbRef}=PATTERN"));

// Verify that Notify was called at least once
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
public async ValueTask GrepCommand_WithAttributePattern()
{
var testPlayer = await CreateTestPlayerAsync("GrepA");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@grep {testPlayer.DbRef}/DESC*=pattern"));

// Verify that Notify was called at least once
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask BriefCommand()
{
var testPlayer = await CreateTestPlayerAsync("Brf");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("brief"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask WhoCommand()
{
var testPlayer = await CreateTestPlayerAsync("Who");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("who"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask SessionCommand()
{
var testPlayer = await CreateTestPlayerAsync("Sess");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("session"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask QuitCommand()
{
var testPlayer = await CreateTestPlayerAsync("Quit");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("quit"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask ConnectCommand()
{
var testPlayer = await CreateTestPlayerAsync("Conn");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("connect player password"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask PromptCommand()
{
var testPlayer = await CreateTestPlayerAsync("Prmt");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@prompt {testPlayer.DbRef}=Enter value:"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask NspromptCommand()
{
var testPlayer = await CreateTestPlayerAsync("Nsprmt");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@nsprompt {testPlayer.DbRef}=Enter value:"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}
}
