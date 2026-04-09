using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ConfigCommandTests
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
public async ValueTask ConfigCommand_NoArgs_ListsCategories()
{
var testPlayer = await CreateTestPlayerAsync("CfgNA");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@config"));

// Should notify with "Configuration Categories:"
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Configuration Categories:")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask ConfigCommand_CategoryArg_ShowsCategoryOptions()
{
var testPlayer = await CreateTestPlayerAsync("CfgCat");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@config Net"));

// Should notify with "Options in Net:"
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Options in Net:")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask ConfigCommand_OptionArg_ShowsOptionValue()
{
var testPlayer = await CreateTestPlayerAsync("CfgOpt");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@config mud_name"));

// Should receive at least one notification about mud_name
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "mud_name")),
Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask ConfigCommand_InvalidOption_ReturnsNotFound()
{
var testPlayer = await CreateTestPlayerAsync("CfgInv");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@config test_string_CONFIG_invalid_option"));

// Should notify that option was not found
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "No configuration category or option")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask MonikerCommand()
{
var testPlayer = await CreateTestPlayerAsync("Mnkr");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@moniker {testPlayer.DbRef}=Test"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask MotdCommand()
{
var testPlayer = await CreateTestPlayerAsync("Motd");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@motd"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
public async ValueTask ListmotdCommand()
{
var testPlayer = await CreateTestPlayerAsync("LstMotd");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@listmotd"));

// Should notify with MOTD settings
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Message of the Day settings")),
Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask WizmotdCommand()
{
var testPlayer = await CreateTestPlayerAsync("WzMotd");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@wizmotd"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask RejectmotdCommand()
{
var testPlayer = await CreateTestPlayerAsync("RjMotd");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@rejectmotd"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
[Category("NotImplemented")]
[Skip("Not Yet Implemented")]
public async ValueTask DoingCommand()
{
var testPlayer = await CreateTestPlayerAsync("Doing");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@doing {testPlayer.DbRef}=Test activity"));

await NotifyService
.Received(Quantity.Exactly(1))
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
}

[Test]
public async ValueTask DoingPollCommand()
{
var testPlayer = await CreateTestPlayerAsync("DoPoll");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("doing"));

// Should notify with player list - verify we got a notification
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>());
}

[Test]
public async ValueTask DoingPollCommand_WithPattern()
{
var testPlayer = await CreateTestPlayerAsync("DoPollP");
var executor = testPlayer.DbRef;
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("doing Wiz*"));

// Should notify with filtered player list
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>());
}

[Test]
public async ValueTask Enable_BooleanOption_ShowsImplementationMessage()
{
var testPlayer = await CreateTestPlayerAsync("EnBool");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @enable with a known boolean option
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@enable noisy_whisper"));

// Should notify about the equivalent @config/set command
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("@enable") &&
s.Value.ToString()!.Contains("@config/set") &&
s.Value.ToString()!.Contains("noisy_whisper")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Disable_BooleanOption_ShowsImplementationMessage()
{
var testPlayer = await CreateTestPlayerAsync("DsBool");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @disable with a known boolean option
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@disable noisy_whisper"));

// Should notify about the equivalent @config/set command
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("@disable") &&
s.Value.ToString()!.Contains("@config/set") &&
s.Value.ToString()!.Contains("noisy_whisper")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Enable_InvalidOption_ReturnsNotFound()
{
var testPlayer = await CreateTestPlayerAsync("EnInv");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @enable with a non-existent option
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@enable test_string_ENABLE_invalid_option_xyz"));

// Should notify that option was not found
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("No configuration option")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Disable_InvalidOption_ReturnsNotFound()
{
var testPlayer = await CreateTestPlayerAsync("DsInv");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @disable with a non-existent option
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@disable test_string_DISABLE_invalid_option_xyz"));

// Should notify that option was not found
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("No configuration option")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Enable_NonBooleanOption_ReturnsInvalidType()
{
var testPlayer = await CreateTestPlayerAsync("EnNBool");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @enable with a non-boolean option (e.g., mud_name)
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@enable mud_name"));

// Should notify that it's not a boolean option
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("not a boolean option")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Disable_NonBooleanOption_ReturnsInvalidType()
{
var testPlayer = await CreateTestPlayerAsync("DsNBool");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @disable with a non-boolean option (e.g., probate_judge)
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@disable probate_judge"));

// Should notify that it's not a boolean option
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("not a boolean option")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Enable_NoArguments_ShowsUsage()
{
var testPlayer = await CreateTestPlayerAsync("EnNA");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @enable without arguments
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@enable"));

// Should show usage message
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("Usage:") &&
s.Value.ToString()!.Contains("@enable")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask Disable_NoArguments_ShowsUsage()
{
var testPlayer = await CreateTestPlayerAsync("DsNA");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test @disable without arguments
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@disable"));

// Should show usage message
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor),
Arg.Is<OneOf.OneOf<MString, string>>(s =>
s.Value.ToString()!.Contains("Usage:") &&
s.Value.ToString()!.Contains("@disable")),
Arg.Any<AnySharpObject>(),
Arg.Any<INotifyService.NotificationType>());
}
}
