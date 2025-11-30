using Core.Arango;
using Core.Arango.Serialization.Json;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server.Strategy.ArangoDB;

public class ArangoTestContainerStartupStrategy : ArangoStartupStrategy
{
	public override async ValueTask<ArangoConfiguration> ConfigureArango()
	{

		var container = new ArangoDbBuilder()
			.WithReuse(false)
			.WithLabel("reuse-id", "SharpMUSH")
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await container.StartAsync();

		return new ArangoConfiguration
		{
			ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoJsonSerializer(new ArangoJsonDefaultPolicy())
		};
	}
}