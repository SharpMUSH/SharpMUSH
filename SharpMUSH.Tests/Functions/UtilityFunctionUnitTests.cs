using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		parser = await FullTestParser();
	}

	[Test]
	public async Task PCreate()
	{
		var result = (await parser!.FunctionParse(MModule.single("pcreate(John,SomePassword)")))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDBRef(result).AsValue();
		var db = await parser.Database!.GetObjectNodeAsync(a);
		var player = db!.AsPlayer;

		await Assert.That(parser.PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash)).IsTrue();
		await Assert.That(parser.PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash)).IsFalse();
	}
}
