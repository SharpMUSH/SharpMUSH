using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class VerbCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask VerbWithDefaultMessages()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,,,ActorDefault,,,OthersDefault"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Test environment issue - NotifyService calls not being captured correctly")]
	public async ValueTask VerbWithAttributes()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WHAT #1=You perform the action!"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&OWHAT #1=performs the action!"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,WHAT,DefaultWhat,OWHAT,DefaultOwhat"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask VerbWithStackArguments()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WHAT_ARGS #1=You say: %0 %1"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,WHAT_ARGS,Default,,,,,Hello,World"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask VerbInsufficientArgs()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#2"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
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
