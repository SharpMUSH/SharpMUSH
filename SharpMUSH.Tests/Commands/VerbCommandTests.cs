using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

public class VerbCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask VerbWithDefaultMessages()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbDefault");

		// Syntax: @verb victim=actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		// Empty what-attr means use the default string directly
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@verb {verbObj}={verbObj},,VerbActorDefault_Value_52830,,VerbOthersDefault_Value_52830,,"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(verbObj),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "VerbActorDefault_Value_52830")),
				TestHelpers.MatchingObject(verbObj),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask VerbWithAttributes()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbAttr");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&WHAT_74102 {verbObj}=VerbAction_Value_74102"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OWHAT_74102 {verbObj}=VerbOther_Value_74102"));

		// 7 RHS args: actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@verb {verbObj}={verbObj},WHAT_74102,DefaultWhat,OWHAT_74102,DefaultOwhat,,"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(verbObj),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "VerbAction_Value_74102")),
				TestHelpers.MatchingObject(verbObj),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask VerbWithStackArguments()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbArgs");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&WHAT_ARGS_91605 {verbObj}=VerbArgs_Value_91605"));

		// 7 RHS args: actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@verb {verbObj}={verbObj},WHAT_ARGS_91605,Default,,,,"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(verbObj),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "VerbArgs_Value_91605")),
				TestHelpers.MatchingObject(verbObj),
				INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// An <c>awhat</c> naming an attribute the victim does not have queues nothing, as in PennMUSH's
	/// <c>do_verb</c>; the verb's messages are still delivered and nothing is raised.
	/// </summary>
	[Test]
	public async ValueTask VerbWithAMissingAwhatRunsNothing()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<IMediator>(), ConnectionService,
			"VerbNoAwhat");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain("@verb me=me,,VerbNoAwhat_What_31907,,,NO_SUCH_AWHAT_31907"));

		var messages = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(messages.Any(message => message.StartsWith("#-1 EXCEPTION:"))).IsFalse();
		await Assert.That(messages).Contains("VerbNoAwhat_What_31907");
	}

	[Test]
	public async ValueTask VerbInsufficientArgs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbInsuf");

		// Provide only the victim with no actor/message args — args.Count < 2 triggers the Usage error
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@verb {verbObj}"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(executor),
				"Usage: @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>[,<args>]",
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires proper permission setup")]
	public async ValueTask VerbPermissionDenied()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires AWHAT command list execution verification")]
	public async ValueTask VerbExecutesAwhat()
	{
		await ValueTask.CompletedTask;
	}
}
