using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MessageCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask MessageBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGBASIC #1=Formatted: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default message,TESTFORMAT_MSGBASIC,TestArg"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask MessageWithAttribute()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGATTR #1=Custom format: [add(%0,%1)]"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default,#1/TESTFORMAT_MSGATTR,5,10"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask MessageUsesDefaultWhenAttributeMissing()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default message shown,NONEXISTENT_ATTR,TestArg"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask MessageSilentSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGSILENT #1=Silent: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/silent #1=Test,TESTFORMAT_MSGSILENT,TestValue"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask MessageNoisySwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOISY #1=Noisy: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/noisy #1=Test,TESTFORMAT_MSGNOISY,TestValue"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Requires room setup")]
	public async ValueTask MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires multiple objects")]
	public async ValueTask MessageOemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask MessageNospoofSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOSPOOF #1=Nospoof: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/nospoof #1=Test,TESTFORMAT_MSGNOSPOOF,TestValue"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}
}
