using NSubstitute.ReceivedExtensions;
using Serilog;

namespace SharpMUSH.Tests.Substitutions
{
	[TestClass]
	public class RegistersUnitTests : BaseUnitTest
	{
		public RegistersUnitTests()
		{
			Log.Logger = new LoggerConfiguration()
												.WriteTo.Console()
												.MinimumLevel.Debug()
												.CreateLogger();
		}

		[TestMethod]
		[DataRow("think [setq(0,foo)]%q0", "foo")]
		[DataRow("think [setq(start,bar)]%q<start>", "bar")]
		[DataRow("think [setr(0,foo)]%q0", "foofoo")]
		[DataRow("think [setr(start,bar)]%q<start>", "barbar")]
		[DataRow("think [setr(start,foo)][letq(start,bar,%q<start>)]", "foobar")]
		[DataRow("think %wv", "wv")]
		[DataRow("think %vv", "vv")]
		[DataRow("think %xv", "xv")]
		[DataRow("think %i0", "0")]
		[DataRow("think %$0", "0")]
		public async Task Test(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser();
			await parser.CommandParse("1", MModule.single(str));

			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(parser.CurrentState.Executor!.Value, expected);
		}
	}
}
