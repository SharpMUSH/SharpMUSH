using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Tests.Commands;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser();
	}

	[Test, DependsOn<RoomsAndMovementTests>]
	public async Task PCreate()
	{
		var result = (await _parser!.FunctionParse(MModule.single("pcreate(John,SomePassword)")))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDBRef(result).AsValue();
		var db = await _parser.Mediator.Send(new GetObjectNodeQuery(a));
		var player = db!.AsPlayer;

		await Assert.That(_parser.PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash)).IsTrue();
		await Assert.That(_parser.PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash)).IsFalse();
	}
}
