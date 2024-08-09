using SharpMUSH.Library;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

[TestClass]
public class UtilityFunctionUnitTests : BaseUnitTest
{
	private static ISharpDatabase? database;

	[ClassInitialize()]
	public static async Task OneTimeSetup(TestContext _)
	{
		database = await IntegrationServer();
	}

	[TestMethod]
	public async Task PCreate()
	{
		var parser = TestParser(
			ds: database, 
			pws: new PasswordService(new Microsoft.AspNetCore.Identity.PasswordHasher<string>()));
		var result = parser.FunctionParse(MModule.single("pcreate(John,SomePassword)"))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDBRef(result).AsT1.Value;
		var db = await database!.GetObjectNodeAsync(a);
		var player = db!.AsT0;

		Assert.IsTrue(parser.PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash));
		Assert.IsFalse(parser.PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash));
	}
}
