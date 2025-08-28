using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Tests.Commands;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// , DependsOn<SharpMUSH.Tests.Commands.RoomsAndMovementTests>
	[Test]
	public async Task PCreate()
	{
		var result = (await Parser!.FunctionParse(MModule.single("pcreate(John,SomePassword)")))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDBRef(result).AsValue();
		var db = await Parser.Mediator.Send(new GetObjectNodeQuery(a));
		var player = db!.AsPlayer;

		await Assert.That(Parser.PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash)).IsTrue();
		await Assert.That(Parser.PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash)).IsFalse();
	}
}
