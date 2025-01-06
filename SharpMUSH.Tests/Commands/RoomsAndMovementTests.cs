using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class RoomsAndMovementTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser(ns: Substitute.For<INotifyService>());
	}

	[Test]
	[DependsOn<GeneralCommandTests>(nameof(GeneralCommandTests.DoDigForCommandlistCheck2))]
	public async Task DigAndMoveTest()
	{
		await _parser!.CommandParse("1", MModule.single("@dig NewRoom=Forward;F,Backward;B"));
		await _parser!.CommandParse("1", MModule.single("think %l"));
		await _parser!.CommandParse("1", MModule.single("goto Forward"));
		await _parser!.CommandParse("1", MModule.single("think %l"));
		await _parser!.CommandParse("1", MModule.single("goto Backward"));
		
		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#0");
		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#9");
	}
	
}