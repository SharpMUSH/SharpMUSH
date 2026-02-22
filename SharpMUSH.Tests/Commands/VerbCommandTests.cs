using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class VerbCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Skip("Test environment issue with @verb notification capture")]
	public async ValueTask VerbWithDefaultMessages()
	{

		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,,,VerbActorDefault_Value_52830,,,VerbOthersDefault_Value_52830"));

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
	[Skip("Test environment issue with @verb notification capture")]
	public async ValueTask VerbWithAttributes()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WHAT_74102 #1=VerbAction_Value_74102"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&OWHAT_74102 #1=VerbOther_Value_74102"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,WHAT_74102,DefaultWhat,OWHAT_74102,DefaultOwhat"));

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
	[Skip("Test environment issue with @verb notification capture")]
	public async ValueTask VerbWithStackArguments()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WHAT_ARGS_91605 #1=VerbArgs_Value_91605"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,WHAT_ARGS_91605,Default"));

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
	[Skip("Test environment issue with notification capture")]
	public async ValueTask VerbInsufficientArgs()
	{

		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#2"));

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
	[Skip("Requires proper permission setup")]
	public async ValueTask VerbPermissionDenied()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires AWHAT command list execution verification")]
	public async ValueTask VerbExecutesAwhat()
	{
		await ValueTask.CompletedTask;
	}
}
