using Core.Arango;

namespace SharpMUSH.Server.Strategy.ArangoDB;

public abstract class ArangoStartupStrategy
{
	public abstract ValueTask<ArangoConfiguration> ConfigureArango();
}
