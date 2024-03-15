using NSubstitute.ReceivedExtensions;

namespace SharpMUSH.Tests.Commands
{
	[TestClass]
	public class GeneralCommandTests : BaseUnitTest
	{
		[TestMethod]
		[DataRow("@pemit #1=This is a test", "This is a test")]
		public void Test(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser();
			_ = parser.CommandParse(str);

			parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(parser.CurrentState().Executor, expected);
		}
	}
}
