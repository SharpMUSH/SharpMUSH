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
			container = new ArangoDbBuilder()
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
			Assert.AreEqual(0, roomZero!.Object!.Key);
		}

		[TestMethod]
		public async Task TestRoomTwo()
		{
			var masterRoom = (await database!.GetObjectNode(2)).Value.AsT1;

			Assert.AreEqual(typeof(SharpRoom), masterRoom.GetType());
			Assert.AreEqual("Master Room", masterRoom!.Object!.Name);
			Assert.AreEqual(2, masterRoom!.Object!.Key);
		}

		[TestMethod]
		public async Task TestPlayerOne()
		{
			var playerOne = (await database!.GetObjectNode(1)).Value.AsT0;

			Assert.AreEqual(typeof(SharpPlayer), playerOne.GetType());
			Assert.AreEqual("God", playerOne!.Object!.Name);
			Assert.AreEqual(1, playerOne!.Object!.Key);
		}

		[TestMethod]
		public async Task SetAndGetAnAttribute()
		{
			var playerOne = (await database!.GetObjectNode(1)).Value.AsT0;

			Assert.AreEqual(typeof(SharpPlayer), playerOne.GetType());
			Assert.AreEqual("God", playerOne!.Object!.Name);
			Assert.AreEqual(1, playerOne!.Object!.Key);

			var playerOneDBRef = playerOne!.Object!.Key!.Value;

			await database!.SetAttribute(playerOneDBRef, ["SingleLayer"], "Single", playerOne);
			await database!.SetAttribute(playerOneDBRef, ["Two"], "Twin", playerOne);
			await database!.SetAttribute(playerOneDBRef, ["Two", "Layers"], "Layer", playerOne);
			await database!.SetAttribute(playerOneDBRef, ["Two", "Leaves"], "Leaf", playerOne);
			await database!.SetAttribute(playerOneDBRef, ["Two", "Leaves2"], "Leaf2", playerOne);
			await database!.SetAttribute(playerOneDBRef, ["Three", "Layers", "Deep"], "Deep1", playerOne);
			await database!.SetAttribute(playerOneDBRef, ["Three", "Layers", "Deep2"], "Deeper", playerOne);

			var existingSingle = await database!.GetAttribute(playerOneDBRef, ["SingleLayer"]);
			var existingLayer = await database!.GetAttribute(playerOneDBRef, ["Two", "Layers"]);
			var existingLeaf = await database!.GetAttribute(playerOneDBRef, ["Two", "Leaves"]);

			Assert.AreEqual(1, existingSingle!.Length);
			Assert.AreEqual(2, existingLayer!.Length);
			Assert.AreEqual(2, existingLeaf!.Length);
			Assert.AreEqual("Single", existingSingle!.Last().Value);
			Assert.AreEqual("Layer", existingLayer!.Last().Value);
			Assert.AreEqual("Leaf", existingLeaf!.Last().Value);
		}
	}
}
