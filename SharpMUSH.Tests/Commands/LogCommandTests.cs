using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class LogCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask LogCommand_DefaultSwitch_LogsToCommandCategory()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log Test log entry"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Command log")) ||
				(msg.IsT1 && msg.AsT1.Contains("Command log"))));
	}

	[Test]
	public async ValueTask LogCommand_WithCmdSwitch_LogsToCommandCategory()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/cmd Test command log entry"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Command log")) ||
				(msg.IsT1 && msg.AsT1.Contains("Command log"))));
	}

	[Test]
	public async ValueTask LogCommand_WithWizSwitch_LogsToWizardCategory()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/wiz Test wizard log entry"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Wizard log")) ||
				(msg.IsT1 && msg.AsT1.Contains("Wizard log"))));
	}

	[Test]
	public async ValueTask LogCommand_WithErrSwitch_LogsToErrorCategory()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/err Test error log entry"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Error log")) ||
				(msg.IsT1 && msg.AsT1.Contains("Error log"))));
	}

	[Test]
	public async ValueTask LogCommand_NoMessage_ReturnsError()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Usage")) ||
				(msg.IsT1 && msg.AsT1.Contains("Usage"))));
	}

	[Test]
	public async ValueTask LogCommand_RecallSwitch_RetrievesLogs()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@log/recall"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask LogwipeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@logwipe command"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask LsetCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@lset #1/ATTR=value"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
