using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;

namespace SharpMUSH.Server.Strategy.ArangoDB;

internal class ArangoSocketStartupStrategy(string arangoConnectionString) : ArangoStartupStrategy
{
	public override async ValueTask<ArangoConfiguration> ConfigureArango()
	{
		await ValueTask.CompletedTask;
		return new ArangoConfiguration
		{
			ConnectionString = arangoConnectionString,
			HttpClient = new HttpClient(new SocketsHttpHandler())
			{
				BaseAddress = new Uri("http://localhost:8529/") // Workaround for SocketsHttpHandler requiring an absolute URI
			},
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};
	}
}