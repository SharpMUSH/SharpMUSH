using SharpMUSH.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions
{
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
			var result = parser.FunctionParse("pcreate(John,SomePassword)")?.Message?.ToString()!;

			var a = Implementation.Functions.Functions.ParseDBRef(result).AsT0;
			var db = await database!.GetObjectNode(a);
			var player = db!.Value.AsT0;

			Assert.IsTrue(parser.PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash));
			Assert.IsFalse(parser.PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash));
		}
	}
}
