using SharpMUSH.Library;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests : BaseUnitTest
{
	private static ISharpDatabase? database;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		database = await IntegrationServer();
	}

	[Test]
	public async Task PCreate()
	{
		var parser = TestParser(
			ds: database, 
			pws: new PasswordService(new Microsoft.AspNetCore.Identity.PasswordHasher<string>()));
		var result = (await parser.FunctionParse(MModule.single("pcreate(John,SomePassword)")))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDBRef(result).AsT1.Value;
		var db = await database!.GetObjectNodeAsync(a);
		var player = db!.AsT0;

		await Assert.That(parser.PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash)).IsTrue();
		await Assert.That(parser.PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash)).IsFalse();
	}
}
