using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class UserDefinedCommandsTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;
	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser(
			ns: Substitute.For<INotifyService>()
			);
	}
	
	[Test]
	public async Task SetAndResetCacheTest()
	{
		await _parser!.CommandParse(1, MModule.single("&cmd`setandresetcache #1=$test:@pemit #1=Value 1 received"));
		await _parser.CommandParse(1, MModule.single("test"));
		
		await _parser.CommandParse(1, MModule.single("&cmd`setandresetcache #1=$test2:@pemit #1=Value 2 received"));
		await _parser.CommandParse(1, MModule.single("test2"));

		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Value 1 received");
		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Value 2 received");
	}
}