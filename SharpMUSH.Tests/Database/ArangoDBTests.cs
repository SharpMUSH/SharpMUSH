using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpMUSH.Database;
using System.Diagnostics;
using Testcontainers.ArangoDb;

namespace SharpMUSH.IntegrationTests
{
	[TestClass]
	public class ArangoDBTests
	{
		private readonly HttpClient httpClient;
		private ArangoDbContainer? container;
		private ArangoDatabase? database;

		public ArangoDBTests()
		{
			httpClient = new HttpClient();
			OneTimeSetup().ConfigureAwait(false).GetAwaiter().GetResult();
		}
    
		public async Task OneTimeSetup()
		{

			await Task.CompletedTask;
			
			var TestServer = new Infrastructure();
			database = (ArangoDatabase)TestServer.Services.GetService(typeof(ArangoDatabase))!;

			container= new ArangoDbBuilder()
				.WithImage("arangodb:3.11.8")
				.Build();

			await container.StartAsync()
				.ConfigureAwait(false);

			// Construct the request URI by specifying the scheme, hostname, assigned random host port, and the endpoint "uuid".
			var requestUri = new UriBuilder(Uri.UriSchemeHttp, container.Hostname, container.GetMappedPublicPort(8529), "uuid").Uri;

			// Send an HTTP GET request to the specified URI and retrieve the response as a string.
			var guid = await httpClient.GetStringAsync(requestUri)
				.ConfigureAwait(false);

			// Ensure that the retrieved UUID is a valid GUID.
			Debug.Assert(Guid.TryParse(guid, out _));

			await database.Migrate();
		}

		[TestMethod]
		public async Task Test()
		{
			Assert.IsTrue(true);
			await Task.CompletedTask;
			//Assert.IsNotNull(await database!.GetObjectNode(0));
		}
	}
}
