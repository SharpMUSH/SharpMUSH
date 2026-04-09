using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class HelpCommandTests
{
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public required ServerWebAppFactory WebAppFactoryArg { get; init; }

private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

[Test]
public async ValueTask HelpCommandWorks()
{
var testPlayer = await CreateTestPlayerAsync("Hlp");
var executor = testPlayer.DbRef;
// Test that help command runs and returns the main help page
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help"));

// Verify that NotifyService was called with content containing "help newbie"
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("help newbie")) ||
(msg.IsT1 && msg.AsT1.Contains("help newbie"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask HelpWithTopicWorks()
{
var testPlayer = await CreateTestPlayerAsync("HlpTpc");
var executor = testPlayer.DbRef;
// Test help with the "newbie" topic
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help newbie"));

// Verify that NotifyService was called with content about newbie help
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("MUSH")) ||
(msg.IsT1 && msg.AsT1.Contains("MUSH"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask HelpWithWildcardWorks()
{
var testPlayer = await CreateTestPlayerAsync("HlpWC");
var executor = testPlayer.DbRef;
// Test help with wildcard pattern - should list matching topics
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help help*"));

// Verify that NotifyService was called with a list of matching topics
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && (msg.AsT0.ToString().Contains("help") || msg.AsT0.ToString().Contains("helpfile"))) ||
(msg.IsT1 && (msg.AsT1.Contains("help") || msg.AsT1.Contains("helpfile")))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask HelpSearchWorks()
{
var testPlayer = await CreateTestPlayerAsync("HlpSrch");
var executor = testPlayer.DbRef;
// Test help/search switch - should find topics whose body CONTAINS the search term (content search)
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help/search newbie"));

// Verify that NotifyService was called with "Matches:" format (content search result)
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("Matches:")) ||
(msg.IsT1 && msg.AsT1.Contains("Matches:"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask HelpNonExistentTopic()
{
var testPlayer = await CreateTestPlayerAsync("HlpNE");
var executor = testPlayer.DbRef;
// Test help with a topic that doesn't exist
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help nonexistenttopicxyz123"));

// Verify that NotifyService was called with "No entry for" (PennMUSH-compatible message)
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("No entry for")) ||
(msg.IsT1 && msg.AsT1.Contains("No entry for"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask HelpWithPrefixMatchWorks()
{
var testPlayer = await CreateTestPlayerAsync("HlpPfx");
var executor = testPlayer.DbRef;
// Test that a prefix match finds the topic (PennMUSH behavior: 'help newb' finds 'newbie')
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help newb"));

// Should show the 'newbie' entry content (contains "MUSHing")
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("MUSH")) ||
(msg.IsT1 && msg.AsT1.Contains("MUSH"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}
}
