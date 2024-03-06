using Core.Arango.Serialization.Newtonsoft;
using Core.Arango;
using SharpMUSH.Database;
using SharpMUSH.Database.Types;
using Testcontainers.ArangoDb;

namespace SharpMUSH.IntegrationTests
{
	[TestClass]
	public class ArangoDBTests
	{
		private ArangoDbContainer? container;
		private ISharpDatabase? database;

		public ArangoDBTests()
		{
			OneTimeSetup().ConfigureAwait(false).GetAwaiter().GetResult();
		}
    
		public async Task OneTimeSetup()
		{
			container= new ArangoDbBuilder()
				.WithImage("arangodb:3.11.8")
				.WithPassword("password")
				.Build();

			await container.StartAsync()
				.ConfigureAwait(false);

			var config = new ArangoConfiguration()
			{
				ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
				Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
			};

			var TestServer = new Infrastructure(config);
			database = TestServer.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase;

			await database!.Migrate();
		}

		[TestMethod]
		public async Task TestRoomZero()
		{
			var roomZero = (await database!.GetObjectNode(0)).Value.AsT1;

			Assert.AreEqual(typeof(SharpRoom), roomZero.GetType());
			Assert.AreEqual("Room Zero", roomZero!.Object!.Name);
			Assert.AreEqual(0, roomZero!.Object!._key);
		}

		[TestMethod]
		public async Task TestRoomTwo()
		{
			var masterRoom = (await database!.GetObjectNode(2)).Value.AsT1;

			Assert.AreEqual(typeof(SharpRoom), masterRoom.GetType());
			Assert.AreEqual("Master Room", masterRoom!.Object!.Name);
			Assert.AreEqual(2, masterRoom!.Object!._key);
		}

		[TestMethod]
		public async Task TestPlayerOne()
		{
			var playerOne = (await database!.GetObjectNode(1)).Value.AsT0;

			Assert.AreEqual(typeof(SharpPlayer), playerOne.GetType());
			Assert.AreEqual("God", playerOne!.Object!.Name);
			Assert.AreEqual(1, playerOne!.Object!._key);
		}
	}
}
