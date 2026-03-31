using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "VerbActorDefault_Value_52830");
		});

		await Assert.That(messageCall).IsNotNull();
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

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "VerbAction_Value_74102");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask VerbWithStackArguments()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbArgs");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&WHAT_ARGS_91605 {verbObj}=VerbArgs_Value_91605"));

		// 7 RHS args: actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@verb {verbObj}={verbObj},WHAT_ARGS_91605,Default,,,,"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "VerbArgs_Value_91605");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask VerbInsufficientArgs()
	{
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbInsuf");

		// Provide only the victim with no actor/message args — args.Count < 2 triggers the Usage error
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@verb {verbObj}"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "Usage: @verb");
		});

		await Assert.That(messageCall).IsNotNull();
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
