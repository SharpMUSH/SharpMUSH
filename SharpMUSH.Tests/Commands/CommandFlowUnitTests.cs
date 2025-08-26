using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommandFlowUnitTests: BaseUnitTest
{
	private static readonly IMUSHCodeParser Parser = TestParser(ns: Substitute.For<INotifyService>())
		.ConfigureAwait(false)
		.GetAwaiter()
		.GetResult();

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

		await Parser.NotifyService.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}
}