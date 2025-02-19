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
	[DependsOn<GeneralCommandTests>]
	public async Task DigAndMoveTest()
	{
		if(_parser is null) throw new Exception("Parser is null");
		// line 1:33 reportAttemptingFullContext d=9 (explicitEvaluationString), input='=Forward;F,Backward;B'
		// line 1:31 reportContextSensitivity d=9 (explicitEvaluationString), input=';'
		// This is in single command mode, this should not care about ;s for full context.

		// line 1:33 reportAttemptingFullContext d=9 (explicitEvaluationString), input=',Backward;B'
		// line 1:22 reportContextSensitivity d=9 (explicitEvaluationString), input=','
		await _parser.CommandParse("1", MModule.single("@dig NewRoom=Forward;F,Backward;B"));
		await _parser.CommandParse("1", MModule.single("think %l"));
		await _parser.CommandParse("1", MModule.single("goto Forward"));
		await _parser.CommandParse("1", MModule.single("think %l"));
		await _parser.CommandParse("1", MModule.single("goto Backward"));
		await _parser.CommandParse("1", MModule.single("think %l back"));
		
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