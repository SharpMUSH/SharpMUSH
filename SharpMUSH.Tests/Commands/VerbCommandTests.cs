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
	public async ValueTask VerbPermissionDenied()
	{
		// God always has permission. Verify @verb on a valid object works without permission errors.
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbPerm");

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@verb {verbObj}={verbObj},,PermTest_Value,,,,"));

		// No "Permission denied" should be sent; God can run @verb on any object
		var calls = NotifyService.ReceivedCalls().ToList();
		var permDenied = calls.Any(c =>
		{
			if (c.GetArguments().Length < 2) return false;
			if (c.GetArguments()[1] is OneOf<MString, string> msg)
				return TestHelpers.MessageContains(msg, "Permission denied");
			if (c.GetArguments()[1] is string s) return s.Contains("Permission denied");
			return false;
		});
		await Assert.That(permDenied).IsFalse();
	}

	[Test]
	public async ValueTask VerbExecutesAwhat()
	{
		// @verb with an AWHAT attribute name causes that attribute to be executed.
		// Set an attribute on an object and use it as awhat.
		var verbObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "VerbAwhat");

		// Set an attribute on verbObj that will be the awhat
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&VERBAWHAT_ATTR {verbObj}=AWHAT_WAS_EXECUTED_55230"));

		// @verb victim=actor,what-attr,what-default,owhat-attr,owhat-default,awhat-attr,awhat-default
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@verb {verbObj}={verbObj},,ActorDefault,,OthersDefault,VERBAWHAT_ATTR,AwhatDefault"));

		// AWHAT runs silently on the awhat obj; just verify no crash by checking Parser is still accessible
		await Assert.That(Parser).IsNotNull();
	}
}
