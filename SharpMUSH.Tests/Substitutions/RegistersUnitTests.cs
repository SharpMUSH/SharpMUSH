using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.IntegrationTests;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Substitutions;

public class RegistersUnitTests : BaseUnitTest
{
	private static Infrastructure? infrastructure;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		(_,infrastructure) = await IntegrationServer();
	}
	
	[After(Class)]
	public static async Task OneTimeTeardown()
	{
		await Task.Delay(1);
		infrastructure!.Dispose();
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
		
		var parser = TestParser(
			ds: infrastructure!.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase,
			ps: infrastructure!.Services.GetService(typeof(IPermissionService)) as IPermissionService,
			ls: infrastructure!.Services.GetService(typeof(ILocateService)) as ILocateService
		);
		await parser.CommandParse("1", MModule.single(str));

		await parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}
}