using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandFlowUnitTests : TestClassFactory
{

	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	
	[Arguments("@ifelse 1=@pemit #1=1 True,@pemit #1=1 False", "1 True")]
	[Arguments("@ifelse 0=@pemit #1=2 True,@pemit #1=2 False", "2 False")]
	[Arguments("@ifelse 1=@pemit #1=3 True", "3 True")]
	[Arguments("@ifelse 1={@pemit #1=4 True},{@pemit #1=4 False}", "4 True")]
	[Arguments("@ifelse 0={@pemit #1=5 True},{@pemit #1=5 False}", "5 False")]
	[Arguments("@ifelse 1={@pemit #1=6 True}", "6 True")]
	public async ValueTask IfElse(string str, string expected)
	{
		// Clear any previous calls to the mock

		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandListParse(MModule.single(str));

		await NotifyService.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == expected) ||
				(msg.IsT1 && msg.AsT1 == expected)), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Explicit] // Currently failing. Needs investigation.
	public async ValueTask Retry()
	{
		// Clear any previous calls to the mock

		await Parser.CommandListParse(MModule.single("think %0; @retry gt(%0,-1)=dec(%0)"));

		await NotifyService.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single(""), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single("-1"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}