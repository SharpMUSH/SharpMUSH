using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class NewsCommandTests
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
public async ValueTask NewsCommandWorks()
{
var testPlayer = await CreateTestPlayerAsync("News");
var executor = testPlayer.DbRef;
// Test that news command runs and returns the main news page
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("news"));

// Verify that NotifyService was called with content about news
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("news")) ||
(msg.IsT1 && msg.AsT1.Contains("news"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask NewsWithTopicWorks()
{
var testPlayer = await CreateTestPlayerAsync("NewsTpc");
var executor = testPlayer.DbRef;
// Test news with the "welcome" topic
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("news welcome"));

// Verify that NotifyService was called with content about welcome
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("SharpMUSH")) ||
(msg.IsT1 && msg.AsT1.Contains("SharpMUSH"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask NewsWithWildcardWorks()
{
var testPlayer = await CreateTestPlayerAsync("NewsWC");
var executor = testPlayer.DbRef;
// Test news with wildcard pattern - should list matching topics
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("news *news*"));

// Verify that NotifyService was called with matching topics
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask NewsNonExistentTopic()
{
var testPlayer = await CreateTestPlayerAsync("NewsNE");
var executor = testPlayer.DbRef;
// Test news with a topic that doesn't exist
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("news nonexistenttopicxyz123"));

// Verify that NotifyService was called with "No news available"
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("No news available")) ||
(msg.IsT1 && msg.AsT1.Contains("No news available"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}
}

[NotInParallel]
public class AhelpCommandTests
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
public async ValueTask AhelpCommandWorks()
{
var testPlayer = await CreateTestPlayerAsync("Ahlp");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test that ahelp command runs for wizard player
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("ahelp"));

// Verify that NotifyService was called with content about ahelp
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && (msg.AsT0.ToString().Contains("ahelp") || msg.AsT0.ToString().Contains("admin"))) ||
(msg.IsT1 && (msg.AsT1.Contains("ahelp") || msg.AsT1.Contains("admin")))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask AhelpWithTopicWorks()
{
var testPlayer = await CreateTestPlayerAsync("AhlpTpc");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test ahelp with the "security" topic
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("ahelp security"));

// Verify that NotifyService was called with content about security
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("Security")) ||
(msg.IsT1 && msg.AsT1.Contains("Security"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask AnewsAliasWorks()
{
var testPlayer = await CreateTestPlayerAsync("Anews");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test that anews is an alias for ahelp
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("anews"));

// Verify that NotifyService was called with ahelp content
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && (msg.AsT0.ToString().Contains("ahelp") || msg.AsT0.ToString().Contains("admin"))) ||
(msg.IsT1 && (msg.AsT1.Contains("ahelp") || msg.AsT1.Contains("admin")))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}

[Test]
public async ValueTask AhelpNonExistentTopic()
{
var testPlayer = await CreateTestPlayerAsync("AhlpNE");
var executor = testPlayer.DbRef;
await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
// Test ahelp with a topic that doesn't exist
await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("ahelp nonexistenttopicxyz123"));

// Verify that NotifyService was called with "No admin help available"
await NotifyService
.Received()
.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
(msg.IsT0 && msg.AsT0.ToString().Contains("No admin help available")) ||
(msg.IsT1 && msg.AsT1.Contains("No admin help available"))), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
}
}
