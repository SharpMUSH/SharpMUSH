namespace SharpMUSH.Server.Strategy.ArangoDB;

public class ArangoStartupStrategyProvider
{
	public ArangoStartupStrategy GetStrategy()
	{
		var arangoConnStr = Environment.GetEnvironmentVariable("ARANGO_CONNECTION_STRING");
		if (string.IsNullOrWhiteSpace(arangoConnStr))
		{
			return new ArangoTestContainerStartupStrategy();
		}
		else
		{
			return new ArangoKubernetesStartupStrategy(arangoConnStr);
		}
	}
}
