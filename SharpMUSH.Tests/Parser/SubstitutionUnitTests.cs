using NSubstitute.ReceivedExtensions;

namespace SharpMUSH.Tests.Parser
{
	[TestClass]
	public class SubstitutionUnitTests : BaseUnitTest
	{
		[TestMethod]
		[DataRow("think %t", "\t")]
		[DataRow("think %#", "#1")]
		[DataRow("think %!", "#1")]
		[DataRow("think %@", "#1")]
		[DataRow("think [strcat(%!,5)]", "#15")]
		[DataRow("think %!5", "#15")]
		// [DataRow("strcat(%q0)")]
		// [DataRow("strcat(%q<test>)")]
		// [DataRow("%s")]
		// [DataRow("%q<test>")]
		// [DataRow("%q<0>")]
		// [DataRow("strcat(%q<0>)")]
		// [DataRow("strcat(%q<word()>)")]
		// [DataRow("strcat(%q<[strcat(1,0)]>)")]
		// [DataRow("strcat(%q<%s>)")]
		// [DataRow("strcat(%q<%q0>)")]
		// [DataRow("strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
		public void Test(string str, string? expected = null)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser();
			var result = parser.CommandParse(str)?.Message?.ToString();

			Console.WriteLine(string.Join("", result));

			if (expected != null)
			{
				parser.NotifyService
					.Received(Quantity.Exactly(1))
					.Notify(parser.CurrentState.Executor, expected);
			}
		}
	}
}
