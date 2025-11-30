using Core.Arango;
using Core.Arango.Serialization.Json;

namespace SharpMUSH.Server.Strategy.ArangoDB;

public class ArangoKubernetesStartupStrategy(string arangoConnectionString) : ArangoStartupStrategy
{
	public override async ValueTask<ArangoConfiguration> ConfigureArango()
	{
		await ValueTask.CompletedTask;
		return new ArangoConfiguration
		{
			ConnectionString = arangoConnectionString,
			Serializer = new ArangoJsonSerializer(new ArangoJsonDefaultPolicy())
		};
	}
}