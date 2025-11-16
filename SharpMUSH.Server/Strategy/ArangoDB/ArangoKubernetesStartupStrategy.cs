using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;

namespace SharpMUSH.Server.Strategy.ArangoDB;

internal class ArangoKubernetesStartupStrategy(string arangoConnectionString) : ArangoStartupStrategy
{
	public override async ValueTask<ArangoConfiguration> ConfigureArango()
	{
		await ValueTask.CompletedTask;
		return new ArangoConfiguration
		{
			ConnectionString = arangoConnectionString,
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};
	}
}