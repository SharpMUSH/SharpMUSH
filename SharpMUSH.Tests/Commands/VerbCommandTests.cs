using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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
			MModule.single($"@verb {verbObj}={verbObj},,VerbActorDefault_Value_52830,,VerbOthersDefault_Value_52830,,"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(verbObj),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "VerbActorDefault_Value_52830")),
				Arg.Is<AnySharpObject?>(s => s == null),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask VerbWithAttributes()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbAttr");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WHAT_74102 {verbObj}=VerbAction_Value_74102"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&OWHAT_74102 {verbObj}=VerbOther_Value_74102"));

		// 7 RHS args: actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@verb {verbObj}={verbObj},WHAT_74102,DefaultWhat,OWHAT_74102,DefaultOwhat,,"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(verbObj),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "VerbAction_Value_74102")),
				Arg.Is<AnySharpObject?>(s => s == null),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask VerbWithStackArguments()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbArgs");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WHAT_ARGS_91605 {verbObj}=VerbArgs_Value_91605"));

		// 7 RHS args: actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@verb {verbObj}={verbObj},WHAT_ARGS_91605,Default,,,,"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(verbObj),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "VerbArgs_Value_91605")),
				Arg.Is<AnySharpObject?>(s => s == null),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask VerbInsufficientArgs()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbInsuf");

		// Provide only the victim with no actor/message args — args.Count < 2 triggers the Usage error
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@verb {verbObj}"));

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
