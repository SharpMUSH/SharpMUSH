using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandFlowUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>()!;

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Arguments("@ifelse 1=@pemit #1=1 True,@pemit #2=1 False", "1 True")]
	[Arguments("@ifelse 0=@pemit #1=2 True,@pemit #2=2 False", "2 False")]
	[Arguments("@ifelse 1=@pemit #1=3 True", "3 True")]
	[Arguments("@ifelse 1={@pemit #1=4 True},{@pemit #2=4 False}", "4 True")]
	[Arguments("@ifelse 0={@pemit #1=5 True},{@pemit #2=5 False}", "5 False")]
	[Arguments("@ifelse 1={@pemit #1=6 True}", "6 True")]
	public async ValueTask IfElse(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		await Parser.CommandListParse(MModule.single(str));

		await NotifyService!.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Explicit] // Currently failing. Needs investigation.
	public async ValueTask Retry()
	{
		await Parser.CommandListParse(MModule.single("think %0; @retry gt(%0,-1)=dec(%0)"));

		await NotifyService!.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single(""));

		await NotifyService!.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single("-1"));
	}
}