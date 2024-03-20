using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands
{
	[TestClass]
	public class GeneralCommandTests : BaseUnitTest
	{
		[TestMethod]
		[DataRow("@pemit #1=This is a test", "This is a test")]
		public async Task Test(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser();
			await parser.CommandParse("1", str);

			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(Arg.Any<DBRef>(), expected);
		}
	}
}
