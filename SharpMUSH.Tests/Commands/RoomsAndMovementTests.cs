using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

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
	[DependsOn<GeneralCommandTests>]
	public async Task DigAndMoveTest()
	{
		if(_parser is null) throw new Exception("Parser is null");
		await _parser.CommandParse(1, MModule.single("@dig NewRoom=Forward;F,Backward;B"));
		await _parser.CommandParse(1, MModule.single("think %l"));
		await _parser.CommandParse(1, MModule.single("goto Forward"));
		await _parser.CommandParse(1, MModule.single("think %l"));
		await _parser.CommandParse(1, MModule.single("goto Backward"));
		await _parser.CommandParse(1, MModule.single("think %l back"));
		
		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#0");
		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#9");
		await _parser.NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "#0 back");
	}
}