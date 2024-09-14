using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands
{
	[TestClass]
	public class GeneralCommandTests : BaseUnitTest
	{
		private static ISharpDatabase? database;

		[ClassInitialize()]
		public static async Task OneTimeSetup(TestContext _)
		{
			database = await IntegrationServer();
		}

		[TestMethod]
		[DataRow("@pemit #1=This is a test", "This is a test")]
		public async Task Test(string str, string expected)
		{
			var permission = Substitute.For<IPermissionService>();
			permission.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
			permission.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
			permission.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<IPermissionService.InteractType>()).Returns(true);

			Console.WriteLine("Testing: {0}", str);
			var parser = TestParser(ds: database, ls: new LocateService(), ps: permission);
			await parser.CommandParse("1", MModule.single(str));

			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(Arg.Any<DBRef>(), expected);
		}
	}
}
