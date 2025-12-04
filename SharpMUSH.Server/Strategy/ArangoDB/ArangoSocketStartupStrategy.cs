using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using SharpMUSH.Server.Connectors;

namespace SharpMUSH.Server.Strategy.ArangoDB;

public class ArangoSocketStartupStrategy : ArangoStartupStrategy
{
	public override async ValueTask<ArangoConfiguration> ConfigureArango()
	{
		await ValueTask.CompletedTask;
		return new ArangoConfiguration
		{
			HttpClient = new HttpClient(UnixSocketHandler.CreateHandlerForUnixSocket("/var/run/arangodb3/arangodb.sock"))
			{
				BaseAddress = new Uri("http://localhost:8529/") // Workaround for SocketsHttpHandler requiring an absolute URI
			},
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};
	}
}