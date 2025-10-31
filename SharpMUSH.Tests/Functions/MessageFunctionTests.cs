using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class MessageFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task MessageBasicReturnsEmpty()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC #1=Function: %0"));
		
		var result = (await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC,TestValue)")))?.Message!;
		
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageBasicSendsNotification()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC2 #1=Function result: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC2,TestValue)"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task MessageWithAttributeEvaluation()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGEVAL #1=Result: [mul(%0,%1)]"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGEVAL,3,7)"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task MessageUsesDefaultWhenAttributeMissing()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default shown here,MISSING_ATTR,Arg1)"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async Task MessageWithMultipleArguments()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGARGS #1=Args: %0 %1 %2"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGARGS,First,Second,Third)"));
		
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async Task MessageHashHashReplacement()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires configuration setup")]
	public async Task MessageNoSideFxDisabled()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires room setup")]
	public async Task MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires multiple objects setup")]
	public async Task MessageOemitSwitch()
	{
		await ValueTask.CompletedTask;
	}
}
