using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AdminCommandTests
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
[Category("TestInfrastructure")]
[Skip("Test infrastructure issue - state pollution from other tests")]
public async ValueTask PcreateCommand()
{
var testPlayer = await CreateTestPlayerAsync("Pcr");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@pcreate TestPlayerPcreate=passwordPcreate"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
[Category("TestInfrastructure")]
[Skip("Test infrastructure issue - state pollution from other tests")]
public async ValueTask NewpasswordCommand()
{
var testPlayer = await CreateTestPlayerAsync("Newpw");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@newpassword {testPlayer.DbRef}=newpassNewpassword"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
[Category("TestInfrastructure")]
[Skip("Test infrastructure issue - state pollution from other tests")]
public async ValueTask PasswordCommand()
{
var testPlayer = await CreateTestPlayerAsync("Pw");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@password oldpassPassword=newpassPassword"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask ShutdownCommand()
{
var testPlayer = await CreateTestPlayerAsync("Shut");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@shutdown"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask RestartCommand()
{
var testPlayer = await CreateTestPlayerAsync("Rst");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@restart"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask PurgeCommand()
{
var testPlayer = await CreateTestPlayerAsync("Prg");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@purge"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("TestInfrastructure")]
[Skip("Test infrastructure issue - state pollution from other tests")]
public async ValueTask PoorCommand()
{
var testPlayer = await CreateTestPlayerAsync("Poor");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@poor #1001"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask ReadcacheCommand()
{
var testPlayer = await CreateTestPlayerAsync("Rdc");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@readcache"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("TestInfrastructure")]
[Skip("Test infrastructure issue - state pollution from other tests")]
public async ValueTask ChownallCommand()
{
var testPlayer = await CreateTestPlayerAsync("Chown");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@chownall #1002=#2002"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}

[Test]
[Category("TestInfrastructure")]
[Skip("Test infrastructure issue - state pollution from other tests")]
public async ValueTask ChzoneallCommand()
{
var testPlayer = await CreateTestPlayerAsync("Chzone");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@chzoneall #1003=#2003"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
}
}
