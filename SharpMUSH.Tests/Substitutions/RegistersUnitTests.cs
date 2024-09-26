using NSubstitute.ReceivedExtensions;
using Serilog;

namespace SharpMUSH.Tests.Substitutions;

public class RegistersUnitTests : BaseUnitTest
{
	public RegistersUnitTests()
	{
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Debug()
			.CreateLogger();
	}

	[Test]
	[Arguments("think [setq(0,foo)]%q0", "foo")]
	[Arguments("think [setq(start,bar)]%q<start>", "bar")]
	[Arguments("think [setr(0,foo)]%q0", "foofoo")]
	[Arguments("think [setr(start,bar)]%q<start>", "barbar")]
	[Arguments("think [setr(start,foo)][letq(start,bar,%q<start>)]", "foobar")]
	// [Arguments("think %wv", "")] // TODO: Requires full server Integration
	// [Arguments("think %vv", "")] // TODO: Requires full server Integration
	// [Arguments("think %xv", "")] // TODO: Requires full server Integration
	[Arguments("think %i0", "#-1 OUT OF RANGE")]
	[Arguments("think %$0", "#-1 OUT OF RANGE")]
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