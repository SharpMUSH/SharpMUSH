using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Serilog;

namespace SharpMUSH.Tests.Commands;

[TestClass]
public class CommandUnitTests : BaseUnitTest
{
	public CommandUnitTests()
	{
		Log.Logger = new LoggerConfiguration()
											.WriteTo.Console()
											.MinimumLevel.Debug()
											.CreateLogger();
	}

	[TestMethod]
	[DataRow("think add(1,2)", "3")]
	[DataRow("think [add(1,2)]", "3")]
	[DataRow("[ansi(hr,think)] Words", "Words")]
	[DataRow("think Command1 Arg;think Command2 Arg", "Command1 Arg;think Command2 Arg")]
	public async Task Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		await parser.CommandParse("1", str);

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState.Executor!.Value, expected);
	}

	[TestMethod]
	[Ignore("Command List Parse is not functioning as needed yet.")]
	[DataRow("think add(1,2);think add(2,3)", "3", "5")]
	[DataRow("think [add(1,2)];think add(3,2)","3","5")]
	[DataRow("[ansi(hy,think)] [ansi(hr,red)];[ansi(hg,think)] [ansi(hg,green)]", "red", "green")]
	[DataRow("think Command1 Arg;think Command2 Arg", "Command1 Arg", "Command2 Arg")]
	public async Task TestSingle(string str, string expected1, string expected2)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		var _ = parser.CommandListParse(str);

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState.Executor!.Value, expected1);

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(parser.CurrentState.Executor!.Value, expected2);
	}
}