using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Substitutions;

public class RegistersUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => (IMUSHCodeParser)WebAppFactoryArg.Services.GetService(typeof(IMUSHCodeParser))!;

	[Test]
	[Arguments("think [setq(0,foo)]%q0", "foo")]
	[Arguments("think [setq(start,bar)]%q<start>", "bar")]
	[Arguments("think [setr(0,foo)]%q0", "foofoo")]
	[Arguments("think [setr(start,bar)]%q<start>", "barbar")]
	[Arguments("think [setr(start,foo)][letq(start,bar,%q<start>)]", "foobar")]
	// [Arguments("think %wv", "")] // TODO: Requires full server Integration
	// [Arguments("think %vv", "")] // TODO: Requires full server Integration
	// [Arguments("think %xv", "")] // TODO: Requires full server Integration
	[Arguments("think %i0 1", "#-1 OUT OF RANGE 1")]
	[Arguments("think %$0 2", "#-1 OUT OF RANGE 2")]
	public async Task Test(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		await Parser.CommandParse(1, MModule.single(str));

		await Parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}
}