﻿using NSubstitute.ReceivedExtensions;
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
		[DataRow("think %q0", "q0")]
		[DataRow("think %q<start>", "start")]
		public async Task Test(string str, string expected)
		{
			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser();
			await parser.CommandParse("1", str);

			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(parser.CurrentState.Executor!.Value, expected);
		}
	}
}
